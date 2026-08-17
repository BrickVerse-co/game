// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Formats;
using BrickVerse.Networking;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System.Collections.Generic;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

[Instantiable]
public partial class Animator : Instance
{
	public AnimationTree AnimationTree = null!;
	internal AnimationPlayer AnimPlay => AnimationTree.GetNode<AnimationPlayer>(AnimationTree.AnimPlayer);

	private AnimationNodeBlendTree _blendTreeRoot = null!;
	private AnimationNodeBlendTree _blendTree = null!;
	private AnimationNodeBlend2? _dynBlend = null!;
	private AnimationNodeStateMachine _dynTrack = null!;
	private AnimationNodeStateMachinePlayback _dynPlayback = null!;
	private const string DynBlendPath = "parameters/DynBlend/blend_amount";
	private float _targetDynBlendValue = 0f;
	private bool _isPlaying = false;
	[ScriptProperty] public bool IsPlaying => _isPlaying;
	public HashSet<string> AnimationList = [];
	public Dictionary<string, MeshAnimationAsset> AnimationAssetList = [];

	private string _lastNodeName = null!;
	private string _lastNodeNameInDynState = null!;
	private bool _hasDynBlend = false;

	private string? _currentOneShot;
	private string? _pendingOneShot;

	private string _currentAnimation = "";
	private bool _queuePostAnimationImport = false;
	private readonly HashSet<string> _impluseOneShots = [];
	private int _customAnimCounter = 0;
	private readonly Dictionary<AnimationTrack, string> _loadedTracks = [];
	private AnimationTrack? _currentTrack;

	[ScriptProperty] public AnimationTrack? CurrentTrack => _currentTrack;
	[ScriptProperty] public BVSignal<AnimationTrack> AnimationPlayed { get; private set; } = new();
	[ScriptProperty] public BVSignal<AnimationTrack> AnimationStopped { get; private set; } = new();

	public float BlendSpeed = 10f;

	[SyncVar]
	public bool AutoInit { get; set; } = true;

	[ScriptProperty, SyncVar]
	public string CurrentAnimation
	{
		get => _currentAnimation;
		set
		{
			string oldA = _currentAnimation;
			_currentAnimation = value;

			if (oldA != _currentAnimation)
			{
				if (_currentAnimation == "")
				{
					// Stop animation if is empty
					InternalStopAnimation();
				}
				else
				{
					// Play new animation
					InternalPlayAnimation(_currentAnimation);
				}
			}
			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene(ClassName)!;
	}

	public override void Init()
	{
		if (HasAuthority)
		{
			Root?.Network?.PeerPreInit += OnPeerPreInit;
		}

		SetProcess(true);
		base.Init();
	}

	public override void Ready()
	{
		base.Ready();
		if (AutoInit)
		{
			AnimatorInit();
		}
		if (_queuePostAnimationImport)
		{
			PostAnimationImport();
		}
	}

	private void OnPeerPreInit(int peerID)
	{
		List<AnimationKeyVal> idPair = [];
		foreach (var (animKey, asset) in AnimationAssetList)
		{
			idPair.Add(new() { Key = animKey, ID = asset.NetworkedObjectID });
		}
		RpcId(peerID, nameof(NetRecvAnimationAssets), SerializeUtils.Serialize<AnimationKeyVal[]>([.. idPair]));
	}

	public override void PreDelete()
	{
		Root?.Network?.PeerPreInit -= OnPeerPreInit;
		CompleteCurrentTrack(false);
		foreach (AnimationTrack track in _loadedTracks.Keys)
			track.Bind(null, "");
		_loadedTracks.Clear();
		AnimationList.Clear();
		base.PreDelete();
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvAnimationAssets(byte[] raw)
	{
		AnimationKeyVal[] idPair = SerializeUtils.Deserialize<AnimationKeyVal[]>(raw) ?? [];
		foreach (var kv in idPair)
		{
			NetImportAnimAsset(kv.Key, kv.ID);
		}
	}

	internal void AnimatorInit()
	{
		AnimationTree ??= GDNode.GetNode<AnimationTree>("AnimationTree");
		// Get the TreeRoot
		if (AnimationTree.TreeRoot is not AnimationNodeBlendTree tree)
		{
			tree = new AnimationNodeBlendTree();
			AnimationTree.TreeRoot = tree;
		}

		_blendTreeRoot = tree;

		if (_blendTreeRoot.HasNode("DynBlend"))
		{
			_hasDynBlend = true;
			_dynBlend = (AnimationNodeBlend2)_blendTreeRoot.GetNode("DynBlend");
		}

		if (!_blendTreeRoot.HasNode("DynBlendTree"))
		{
			_blendTree = new();
			_blendTreeRoot.AddNode("DynBlendTree", _blendTree);
			if (_dynBlend != null)
			{
				_blendTreeRoot.ConnectNode("DynBlend", 0, "DynBlendTree");
			}
			else
			{
				_blendTreeRoot.ConnectNode("output", 0, "DynBlendTree");
			}
		}
		else
		{
			_blendTree = (AnimationNodeBlendTree)_blendTreeRoot.GetNode("DynBlendTree");
		}

		if (!_blendTree.HasNode("DynState"))
		{
			_dynTrack = new AnimationNodeStateMachine();
			_blendTree.AddNode("DynState", _dynTrack);
			_blendTree.ConnectNode("output", 0, "DynState");
		}
		else
		{
			_dynTrack = (AnimationNodeStateMachine)_blendTree.GetNode("DynState");
		}

		_dynPlayback = (AnimationNodeStateMachinePlayback)AnimationTree.Get("parameters/DynBlendTree/DynState/playback");
		if (_hasDynBlend)
		{
			_lastNodeName = "DynBlend";
		}
		else
		{
			_lastNodeName = "DynState";
		}

		_lastNodeNameInDynState = "DynState";

		QueuePostAnimationImport();
	}

	public void QueuePostAnimationImport()
	{
		_queuePostAnimationImport = true;
	}

	public void PostAnimationImport()
	{
		if (CurrentAnimation != "")
		{
			InternalPlayAnimation(CurrentAnimation);
		}
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		UpdateCurrentTrackState();
		if (_dynBlend == null || AnimationTree == null)
			return;
		if (!Node.IsInstanceValid(AnimationTree)) return;

		// Smooth step toward target
		float currentValue = (float)AnimationTree.Get(DynBlendPath);
		float newValue = Mathf.Lerp(currentValue, _targetDynBlendValue, MathUtils.ExpDecay((float)delta, BlendSpeed));
		AnimationTree.Set(DynBlendPath, newValue);

		// Check if pending oneshot became active
		if (_pendingOneShot != null)
		{
			bool isActive = (bool)AnimationTree.Get(_pendingOneShot + "/active");
			if (isActive)
			{
				// it's now active
				_currentOneShot = _pendingOneShot;
				_pendingOneShot = null;
			}
		}

		// Check active oneshot
		if (_currentOneShot != null)
		{
			bool isActive = (bool)AnimationTree.Get(_currentOneShot + "/active");

			// the animation finished or was aborted
			if (!isActive)
			{
				_dynPlayback.Start("End");
				_targetDynBlendValue = 0f;

				// Snap back to zero dynblend
				AnimationTree.Set(DynBlendPath, 0);
				_currentOneShot = null;
			}
		}
	}

	private void UpdateCurrentTrackState()
	{
		if (_currentTrack == null || !_currentTrack.IsPlaying || AnimPlay == null) return;
		float position = (float)AnimPlay.CurrentAnimationPosition;
		_currentTrack.UpdatePlayback(position, true);
		bool stateEnded = _dynPlayback != null && _dynPlayback.GetCurrentNode().ToString() == "End";
		if (_currentTrack.LoopMode == "None" && (stateEnded || position >= _currentTrack.Length - 0.002f))
			CompleteCurrentTrack(true);
	}

	[ScriptMethod]
	public AnimationTrack LoadAnimation(AnimationTrack track)
	{
		if (track == null) throw new System.ArgumentNullException(nameof(track));
		if (track.IsDeleted) throw new System.InvalidOperationException("Cannot load a deleted AnimationTrack.");
		if (track.Root != Root) throw new System.InvalidOperationException("AnimationTrack must belong to the same World as the Animator.");
		if (_dynTrack == null || AnimationTree == null)
			throw new System.InvalidOperationException("Animator is not initialized yet.");
		RegisterAnimationTrack(track);
		return track;
	}

	[ScriptMethod]
	public bool IsAnimationLoaded(AnimationTrack track) => track != null && _loadedTracks.ContainsKey(track);

	[ScriptMethod]
	public void UnloadAnimation(AnimationTrack track)
	{
		if (track == null || !_loadedTracks.Remove(track, out string? animationKey)) return;
		if (_currentTrack == track)
		{
			InternalStopAnimation();
			CompleteCurrentTrack(false);
			if (HasAuthority) Rpc(nameof(NetStopAnimationTrack), track.NetworkedObjectID);
		}
		string nodeKey = animationKey.Replace('/', '_');
		if (_dynTrack.HasNode(nodeKey)) _dynTrack.RemoveNode(nodeKey);
		AnimationList.Remove(animationKey);
		string libraryKey = TrackLibraryKey(track);
		if (AnimPlay.HasAnimationLibrary(libraryKey)) AnimPlay.RemoveAnimationLibrary(libraryKey);
		if (AnimationTree.HasAnimationLibrary(libraryKey)) AnimationTree.RemoveAnimationLibrary(libraryKey);
		track.Bind(null, "");
	}

	internal void InvalidateAnimationTrack(AnimationTrack track) => UnloadAnimation(track);

	private static string TrackLibraryKey(AnimationTrack track) =>
		"track_" + track.NetworkedObjectID.Replace('-', '_');

	private string RegisterAnimationTrack(AnimationTrack track)
	{
		if (_loadedTracks.TryGetValue(track, out string? existing)) return existing;
		BVAnimationClip clip = track.GetClip() ?? throw new System.InvalidOperationException("AnimationTrack has no valid animation data.");
		string libraryKey = TrackLibraryKey(track);
		string animationKey = "track/" + track.NetworkedObjectID;
		AnimationLibrary library = BVAnimationFormat.ToLibrary(clip);
		Godot.Animation animation = library.GetAnimation(clip.Name);
		PreprocessAnimation(animation);
		if (AnimPlay.HasAnimationLibrary(libraryKey)) AnimPlay.RemoveAnimationLibrary(libraryKey);
		AnimPlay.AddAnimationLibrary(libraryKey, library);
		if (AnimationTree.HasAnimationLibrary(libraryKey)) AnimationTree.RemoveAnimationLibrary(libraryKey);
		AnimationTree.AddAnimationLibrary(libraryKey, library);
		ImportAnimationRaw(animationKey, libraryKey + "/" + clip.Name, animation.LoopMode);
		_loadedTracks[track] = animationKey;
		track.Bind(this, animationKey);
		return animationKey;
	}

	[ScriptMethod]
	public void PlayAnimationTrack(AnimationTrack track)
	{
		string key = RegisterAnimationTrack(track);
		if (_currentTrack != null && _currentTrack != track) CompleteCurrentTrack(false);
		_currentTrack = track;
		track.NotifyPlayed();
		AnimationPlayed.Invoke(track);
		AnimPlay.SpeedScale = track.Speed;
		InternalPlayAnimation(key);
		if (HasAuthority) Rpc(nameof(NetPlayAnimationTrack), track.NetworkedObjectID, track.Speed);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private async void NetPlayAnimationTrack(string trackId, float speed)
	{
		AnimationTrack? track = (AnimationTrack?)await Root.WaitForNetObjectAsync(trackId);
		if (track == null) return;
		string key = RegisterAnimationTrack(track);
		track.AdjustSpeed(speed);
		if (_currentTrack != null && _currentTrack != track) CompleteCurrentTrack(false);
		_currentTrack = track;
		track.NotifyPlayed();
		AnimationPlayed.Invoke(track);
		AnimPlay.SpeedScale = track.Speed;
		InternalPlayAnimation(key);
	}

	[ScriptMethod]
	public void StopAnimationTrack(AnimationTrack track)
	{
		if (track == null || _currentTrack != track) return;
		InternalStopAnimation();
		CompleteCurrentTrack(false);
		if (HasAuthority) Rpc(nameof(NetStopAnimationTrack), track.NetworkedObjectID);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetStopAnimationTrack(string trackId)
	{
		if (_currentTrack?.NetworkedObjectID != trackId) return;
		InternalStopAnimation();
		CompleteCurrentTrack(false);
	}

	internal void SeekAnimationTrack(AnimationTrack track, float seconds)
	{
		if (_currentTrack != track) return;
		float position = Mathf.Clamp(seconds, 0, track.Length);
		AnimPlay.Seek(position, true);
		track.UpdatePlayback(position, true);
		if (HasAuthority) Rpc(nameof(NetSeekAnimationTrack), track.NetworkedObjectID, position);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetSeekAnimationTrack(string trackId, float seconds)
	{
		if (_currentTrack?.NetworkedObjectID != trackId) return;
		float position = Mathf.Clamp(seconds, 0, _currentTrack.Length);
		AnimPlay.Seek(position, true);
		_currentTrack.UpdatePlayback(position, true);
	}

	internal void SetAnimationTrackSpeed(AnimationTrack track, float speed)
	{
		if (_currentTrack != track) return;
		AnimPlay.SpeedScale = speed;
		if (HasAuthority) Rpc(nameof(NetSetAnimationTrackSpeed), track.NetworkedObjectID, speed);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetSetAnimationTrackSpeed(string trackId, float speed)
	{
		if (_currentTrack?.NetworkedObjectID != trackId) return;
		_currentTrack.AdjustSpeed(speed);
		AnimPlay.SpeedScale = _currentTrack.Speed;
	}

	private void CompleteCurrentTrack(bool ended)
	{
		if (_currentTrack == null) return;
		AnimationTrack track = _currentTrack;
		_currentTrack = null;
		_isPlaying = false;
		if (ended)
		{
			_currentAnimation = "";
			_targetDynBlendValue = 0f;
		}
		track.NotifyStopped(ended);
		AnimationStopped.Invoke(track);
	}

	// NOTE: This is disabled until we find a better solution to animations
	//[ScriptMethod]
	public void ImportMeshAnimation(string key, MeshAnimationAsset asset)
	{
		InternalImportMeshAnimation(key, asset);
		if (HasAuthority)
		{
			Rpc(nameof(NetImportAnimAsset), key, asset.NetworkedObjectID);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private async void NetImportAnimAsset(string key, string animID)
	{
		MeshAnimationAsset? animAsset = (MeshAnimationAsset?)await Root.WaitForNetObjectAsync(animID);

		if (animAsset != null)
		{
			InternalImportMeshAnimation(key, animAsset);
		}
		else
		{
			BV.PrintWarn(animID, " is not imported");
		}
	}

	internal void InternalImportMeshAnimation(string key, MeshAnimationAsset asset)
	{
		_customAnimCounter++;
		AnimationLibrary? prevLib = null;
		string internalKey = "custom_" + _customAnimCounter;
		AnimationAssetList.Add(key, asset);
		asset.LinkTo(this);

		// TODO: Untangle this so it allows for delete animation
		asset.ResourceLoaded += (res) =>
		{
			if (res is AnimationLibrary animLib)
			{
				if (prevLib == animLib) return;

				prevLib = animLib;
				AnimPlay.AddAnimationLibrary(internalKey, animLib);
				AnimationTree.AddAnimationLibrary(internalKey, animLib);

				var animList = animLib.GetAnimationList();
				List<string> importedAnim = [];
				foreach (var item in animList)
				{
					Animation anim = animLib.GetAnimation(item);
					PreprocessAnimation(anim);
					string k = key + "/" + item;
					string ik = internalKey + "/" + item;
					importedAnim.Add(k);
					if (asset.AnimationType == MeshAnimationAsset.MeshAnimationTypeEnum.OneShot)
					{
						ImportOneShotAnimationRaw(k, ik);
					}
					else if (asset.AnimationType == MeshAnimationAsset.MeshAnimationTypeEnum.OneShotImpluse)
					{
						ImportOneShotAnimationRaw(k, ik, true);
					}
					else if (asset.AnimationType == MeshAnimationAsset.MeshAnimationTypeEnum.Looped)
					{
						anim.LoopMode = Animation.LoopModeEnum.Linear;
						ImportAnimationRaw(k, ik, Animation.LoopModeEnum.Linear);
					}
					else if (asset.AnimationType == MeshAnimationAsset.MeshAnimationTypeEnum.PingPong)
					{
						anim.LoopMode = Animation.LoopModeEnum.Pingpong;
						ImportAnimationRaw(k, ik, Animation.LoopModeEnum.Pingpong);
					}
					else
					{
						anim.LoopMode = Animation.LoopModeEnum.None;
						ImportAnimationRaw(k, ik, Animation.LoopModeEnum.None);
					}
				}

				// Play animation if newly imported exists
				if (importedAnim.Contains(CurrentAnimation))
				{
					PlayAnimation(CurrentAnimation);
				}
			}
		};
		asset.LoadResource();
	}

	internal void ImportAnimationRaw(string animationKey, string animationName, Godot.Animation.LoopModeEnum loopMode = Godot.Animation.LoopModeEnum.Linear)
	{
		string filteredAnimKey = animationKey.Replace('/', '_');
		if (_dynTrack.HasNode(filteredAnimKey))
		{
			// already exists
			return;
		}

		AnimationList.Add(animationKey);

		// Create animation node
		AnimationNodeAnimation animationNode = new()
		{
			Animation = animationName,
			LoopMode = loopMode
		};
		_dynTrack.AddNode(filteredAnimKey, animationNode, new Vector2(0, 200));

		// Start -> AnimKey -> End
		_dynTrack.AddTransition("Start", filteredAnimKey, new()
		{
			AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Auto
		});
		_dynTrack.AddTransition(filteredAnimKey, "End", new()
		{
			AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Auto,
			SwitchMode = AnimationNodeStateMachineTransition.SwitchModeEnum.AtEnd,
		});
	}

	internal void ImportOneShotAnimationRaw(string animationKey, string animationName, bool impluse = false)
	{
		string oneshotKey = animationKey.Replace('/', '_') + "_oneshot";
		string animKey = animationKey.Replace('/', '_') + "_anim";
		if (_blendTree.HasNode(oneshotKey) || _blendTree.HasNode(animKey))
		{
			// already exists
			return;
		}
		AnimationList.Add(animationKey);

		AnimationNodeAnimation animationNode = new() { Animation = animationName };

		AnimationNodeBlendTree targetTree = _blendTreeRoot;

		if (!impluse)
		{
			targetTree = _blendTree;
		}
		else
		{
			_impluseOneShots.Add(animationKey);
		}

		// Animation -> OneShot
		AnimationNodeOneShot animationOneShot = new();
		targetTree.AddNode(animKey, animationNode, new Vector2(0, 100));
		targetTree.AddNode(oneshotKey, animationOneShot, new Vector2(100, 100));
		targetTree.ConnectNode(oneshotKey, 1, animKey);

		// Disconnect previous output
		targetTree.DisconnectNode("output", 0);
		if (!impluse)
		{
			// OneShot -> Last node in DynState
			targetTree.ConnectNode(oneshotKey, 0, _lastNodeNameInDynState);
		}
		else
		{
			// OneShot -> Last node
			targetTree.ConnectNode(oneshotKey, 0, _lastNodeName);
		}
		// Oneshot -> output
		targetTree.ConnectNode("output", 0, oneshotKey);

		Godot.Animation anim = AnimationTree.GetAnimation(animationName);
		animationOneShot.FilterEnabled = true;

		// Filter only nodes that have at least one track with more than 1 key
		for (int i = 0; i < anim.GetTrackCount(); i++)
		{
			if (anim.TrackGetKeyCount(i) > 1)
			{
				animationOneShot.SetFilterPath(anim.TrackGetPath(i), true);
			}
		}

		if (impluse)
		{
			_lastNodeName = oneshotKey;
		}
		else
		{
			_lastNodeNameInDynState = oneshotKey;
		}
	}

	private static void PreprocessAnimation(Animation anim)
	{
		// Get all track paths in the animation
		for (int i = anim.GetTrackCount() - 1; i >= 0; i--)
		{
			string trackPath = anim.TrackGetPath(i);
			Animation.TrackType trackType = anim.TrackGetType(i);

			if (trackType != Animation.TrackType.Position3D &&
				trackType != Animation.TrackType.Rotation3D &&
				trackType != Animation.TrackType.Scale3D)
			{
				// Remove non-transform tracks
				anim.RemoveTrack(i);
				continue;
			}

			if (!trackPath.Contains("Skeleton3D:"))
			{
				// Remove tracks that don't target skeleton bones
				anim.RemoveTrack(i);
			}
		}
	}

	internal void SetTrackEnabled(string trackName, bool enabled)
	{
		string[] anims = AnimationTree.GetAnimationList();
		foreach (string animName in anims)
		{
			Godot.Animation anim = AnimationTree.GetAnimation(animName);
			for (int i = 0; i < anim.GetTrackCount(); i++)
			{
				string path = anim.TrackGetPath(i).ToString();
				if (path.Contains(trackName))
				{
					anim.TrackSetEnabled(i, enabled);
				}
			}
		}
	}

	[ScriptMethod]
	public void PlayAnimation(string animationKey)
	{
		if (_currentTrack != null && _currentTrack.RuntimeKey != animationKey)
			CompleteCurrentTrack(false);
		InternalPlayAnimation(animationKey);

		if (HasAuthority)
		{
			Rpc(nameof(NetPlayAnimation), animationKey);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetPlayAnimation(string animationKey)
	{
		InternalPlayAnimation(animationKey);
	}

	private void InternalPlayAnimation(string animationKey)
	{
		CurrentAnimation = animationKey;
		string filteredAnimKey = animationKey.Replace('/', '_');

		AbortCurrentOneShot();

		_dynPlayback.Stop();

		// Call on next frame, wait for stop to run properly
		Callable.From(() =>
		{
			if (_dynTrack.HasNode(filteredAnimKey))
			{
				_dynPlayback.Start(filteredAnimKey);
			}
			_targetDynBlendValue = 1;
			_isPlaying = true;
		}).CallDeferred();
	}

	[ScriptMethod]
	public void PlayOneShotAnimation(string animationKey)
	{
		Rpc(nameof(NetPlayOneShotAnimation), animationKey);
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private async void NetPlayOneShotAnimation(string animationKey)
	{
		if (!AnimationList.Contains(animationKey)) { return; }
		string filteredAnimKey = animationKey.Replace('/', '_');

		string oneshotPath = "parameters/" + filteredAnimKey + "_oneshot";
		if (!_impluseOneShots.Contains(animationKey))
		{
			// Non impluse animation
			oneshotPath = "parameters/DynBlendTree/" + filteredAnimKey + "_oneshot";
		}
		_dynPlayback.Start("End");

		_targetDynBlendValue = 1;
		AbortCurrentOneShot();

		AnimationTree.Set(oneshotPath + "/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);

		_pendingOneShot = oneshotPath;
	}

	[ScriptMethod]
	public void StopAnimation()
	{
		// If dynblend value is already none, return
		if (_targetDynBlendValue == 0 && _currentTrack == null) return;
		InternalStopAnimation();
		CompleteCurrentTrack(false);

		if (HasAuthority)
		{
			Rpc(nameof(NetStopAnimation));
		}
	}

	[ScriptMethod]
	public void StopOneShotAnimation()
	{
		AbortCurrentOneShot();

		if (HasAuthority)
		{
			Rpc(nameof(NetAbortOneShot));
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetStopAnimation()
	{
		InternalStopAnimation();
	}


	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetAbortOneShot()
	{
		AbortCurrentOneShot();
	}

	private void InternalStopAnimation()
	{
		CurrentAnimation = "";
		// Stop Dyn playback & Reset blend value to zero
		_targetDynBlendValue = 0;
		_isPlaying = false;
	}

	private void AbortCurrentOneShot()
	{
		if (_currentOneShot != null)
		{
			AnimationTree.Set(_currentOneShot + "/request", (int)AnimationNodeOneShot.OneShotRequest.Abort);
			_currentOneShot = null;
		}
	}

	// We need this for now since memory pack doesn't like dictionaries in release builds
	[MemoryPackable]
	public partial class AnimationKeyVal
	{
		public string Key { get; set; } = "";
		public string ID { get; set; } = "";
	}
}
