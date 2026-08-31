using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Networking.RateLimiters;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel.Services;

/// <summary>Eligibility-gated spatial Opus voice carried outside the website/API request path.</summary>
[Static("VoiceChat"), ExplorerExclude, SaveIgnore]
public sealed partial class VoiceChatService : Instance
{
	private const string MicScene = "res://addons/twovoip/voiphelper/two_voip_mic.tscn";
	private const string SpeakerScene = "res://addons/twovoip/voiphelper/two_voip_speaker.tscn";
	private const string MicrophoneIcon = "res://assets/textures/ui-icons/microphone-bold.svg";
	private const string MutedIcon = "res://assets/textures/ui-icons/microphone-slash-bold.svg";
	private const int MaximumPacketBytes = 4096;
	private readonly Dictionary<Player, (AudioStreamPlayer3D Player, Node Decoder)> _speakers = [];
	private readonly Dictionary<Player, VoiceIndicator> _indicators = [];
	private readonly Dictionary<Player, SlidingWindowRateLimiter> _voiceLimits = [];
	private readonly Dictionary<Player, SlidingWindowRateLimiter> _levelLimits = [];
	private readonly Dictionary<string, float> _playerVolumes = [];
	private readonly Dictionary<string, float> _voiceLevels = [];
	private readonly Dictionary<string, ulong> _lastVoiceActivity = [];
	private readonly HashSet<string> _speakingUsers = [];
	private readonly HashSet<string> _mutedUsers = [];
	private Node? _microphone;
	private Button? _microphoneToggle;
	private bool _initialized;
	private bool _microphoneEnabled;
	private float _inputSensitivity = 1f;
	private float _microphoneVolume = 1f;
	private float _outputVolume = 1f;
	private double _levelSendTimer;
	private bool _indicatorEnabled = true;
	private Color _indicatorColor = new("35d06f");
	private Color _indicatorIdleColor = new("a7afb9");
	private Color _indicatorMutedColor = new("e45454");

	[ScriptProperty] public bool IsAvailable => _initialized && Root.Players.LocalPlayer?.CanVoiceChat == true;
	[ScriptProperty] public bool MicrophoneEnabled => _microphoneEnabled;
	[ScriptProperty] public float InputSensitivity => _inputSensitivity;
	[ScriptProperty] public float MicrophoneVolume => _microphoneVolume;
	[ScriptProperty] public float OutputVolume => _outputVolume;
	[ScriptProperty] public bool IndicatorEnabled => _indicatorEnabled;
	[ScriptProperty] public Color IndicatorColor => _indicatorColor;
	[ScriptProperty] public Color IndicatorIdleColor => _indicatorIdleColor;
	[ScriptProperty] public Color IndicatorMutedColor => _indicatorMutedColor;
	[ScriptProperty] public BVSignal<bool> MicrophoneChanged { get; private set; } = new();
	[ScriptProperty] public BVSignal<Player, bool> PlayerMutedChanged { get; private set; } = new();
	[ScriptProperty] public BVSignal<Player, bool> PlayerSpeakingChanged { get; private set; } = new();
	[ScriptProperty] public BVSignal<Player, float> VoiceLevelChanged { get; private set; } = new();

	public override void Init()
	{
		SetProcess(true);
		base.Init();
	}

	public override void Ready()
	{
		base.Ready();
		Root.Players.PlayerRemoved.Connect(OnPlayerRemoved);
		if (!Root.Network.IsServer)
		{
			Root.Input.GodotInputEvent += OnInput;
			SetProcess(true);
		}
	}

	private void InitializeCodec()
	{
		if (_initialized) return;
		if (!ClassDB.ClassExists("TwovoipOpusEncoder") || !ResourceLoader.Exists(MicScene) || !ResourceLoader.Exists(SpeakerScene))
		{
			BV.PrintErr("Voice chat unavailable: the twovoip GDExtension did not load for this platform."); return;
		}
		PackedScene scene = ResourceLoader.Load<PackedScene>(MicScene); _microphone = scene.Instantiate(); GDNode.AddChild(_microphone);
		_microphoneToggle = new Button { ToggleMode = true, ButtonPressed = false };
		Button ptt = new() { ToggleMode = true }; Button vox = new() { ToggleMode = true, ButtonPressed = true }; Button denoise = new() { ToggleMode = true, ButtonPressed = true };
		_microphone.Call("initvoipmic", _microphoneToggle, default(Variant), ptt, vox, denoise, default(Variant));
		_microphone.Call("setopusvalues", 48000, 20, 1, 16000, 5, true); _microphone.Call("set_voxthreshhold", 0.025f / _inputSensitivity);
		_microphone.Call("set_gain", _microphoneVolume);
		_microphone.Connect("transmitaudiopacket", Callable.From<byte[], long>(OnEncodedPacket));
		_microphone.Connect("transmitaudiojsonpacket", Callable.From<Godot.Collections.Dictionary>(OnStreamMetadata));
		_initialized = true;
	}

	[ScriptMethod]
	public void SetMicrophoneEnabled(bool enabled)
	{
		if (enabled && !_initialized && Root.Players.LocalPlayer?.CanVoiceChat == true)
			InitializeCodec();
		if (!_initialized || Root.Players.LocalPlayer?.CanVoiceChat != true) enabled = false;
		_microphoneEnabled = enabled; if (_microphoneToggle != null) _microphoneToggle.ButtonPressed = enabled;
		if (!enabled) AudioServer.SetInputDeviceActive(false);
		MicrophoneChanged.Invoke(enabled);
	}

	[ScriptMethod]
	public void SetInputSensitivity(float sensitivity)
	{
		_inputSensitivity = Mathf.Clamp(sensitivity, 0.1f, 4f);
		_microphone?.Call("set_voxthreshhold", 0.025f / _inputSensitivity);
	}

	[ScriptMethod]
	public void SetMicrophoneVolume(float volume)
	{
		_microphoneVolume = Mathf.Clamp(volume, 0f, 2f);
		_microphone?.Call("set_gain", _microphoneVolume);
	}

	[ScriptMethod]
	public void SetOutputVolume(float volume)
	{
		_outputVolume = Mathf.Clamp(volume, 0f, 2f);
		foreach (var (player, speaker) in _speakers)
		{
			float playerVolume = _playerVolumes.GetValueOrDefault(player.UserID, 1f);
			speaker.Player.VolumeDb = ToVolumeDb(playerVolume * _outputVolume);
		}
	}

	[ScriptMethod]
	public void SetIndicatorStyle(bool enabled, Color activeColor, Color idleColor, Color mutedColor)
	{
		_indicatorEnabled = enabled; _indicatorColor = activeColor; _indicatorIdleColor = idleColor; _indicatorMutedColor = mutedColor;
	}

	[ScriptMethod] public void MutePlayer(Player player) => SetPlayerMuted(player, true);
	[ScriptMethod] public void UnmutePlayer(Player player) => SetPlayerMuted(player, false);
	[ScriptMethod]
	public bool TogglePlayerMuted(Player player)
	{
		if (player == null) return false;
		bool muted = player == Root.Players.LocalPlayer ? MicrophoneEnabled : !IsPlayerMuted(player);
		SetPlayerMuted(player, muted); return muted;
	}
	[ScriptMethod] public bool IsPlayerMuted(Player player) => player != null && _mutedUsers.Contains(player.UserID);
	[ScriptMethod] public bool IsPlayerSpeaking(Player player) => player != null && _speakingUsers.Contains(player.UserID);
	[ScriptMethod] public float GetVoiceLevel(Player player) => player != null && _voiceLevels.TryGetValue(player.UserID, out float level) ? level : 0f;
	[ScriptMethod] public float GetPlayerVolume(Player player) => player != null && _playerVolumes.TryGetValue(player.UserID, out float volume) ? volume : 1f;
	[ScriptMethod]
	public void SetPlayerVolume(Player player, float volume)
	{
		if (player == null) return;
		volume = Mathf.Clamp(volume, 0f, 2f); _playerVolumes[player.UserID] = volume;
		if (_speakers.TryGetValue(player, out var speaker)) speaker.Player.VolumeDb = ToVolumeDb(volume * _outputVolume);
	}

	private void SetPlayerMuted(Player player, bool muted)
	{
		if (player == null) return;
		if (player == Root.Players.LocalPlayer) { SetMicrophoneEnabled(!muted); return; }
		bool changed = muted ? _mutedUsers.Add(player.UserID) : _mutedUsers.Remove(player.UserID);
		if (_speakers.TryGetValue(player, out var speaker)) speaker.Player.StreamPaused = muted;
		if (changed) PlayerMutedChanged.Invoke(player, muted);
	}

	private void OnEncodedPacket(byte[] packet, long _) => SendPacket(packet);
	private void OnStreamMetadata(Godot.Collections.Dictionary metadata) => SendPacket(Json.Stringify(metadata).ToUtf8Buffer());
	private void SendPacket(byte[] packet)
	{
		if (!_microphoneEnabled || !IsAvailable || packet.Length is < 4 or > MaximumPacketBytes) return;
		RpcId(1, nameof(NetServerVoicePacket), packet);
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (Root.Network.IsServer || !_initialized) return;
		Player? local = Root.Players.LocalPlayer;
		float localLevel = _microphoneEnabled && _microphone != null ? Mathf.Clamp(_microphone.Get("last_chunkmax").AsSingle() * _inputSensitivity, 0f, 1f) : 0f;
		if (local != null)
		{
			SetVoiceActivity(local, localLevel, alwaysVisible: true);
			_levelSendTimer += delta;
			if (_levelSendTimer >= 0.1) { _levelSendTimer = 0; RpcId(1, nameof(NetServerVoiceLevel), localLevel); }
		}
		ulong now = Time.GetTicksMsec();
		foreach (Player player in Root.Players.GetChildrenOfClass<Player>())
		{
			if (player == local) continue;
			float level = _voiceLevels.GetValueOrDefault(player.UserID);
			if (!_lastVoiceActivity.TryGetValue(player.UserID, out ulong last) || now - last > 350) level = 0f;
			SetVoiceActivity(player, level, alwaysVisible: false);
		}
	}

	private void SetVoiceActivity(Player player, float level, bool alwaysVisible)
	{
		level = Mathf.Clamp(level, 0f, 1f); _voiceLevels[player.UserID] = level;
		bool speaking = level >= 0.025f;
		if (speaking != _speakingUsers.Contains(player.UserID))
		{
			if (speaking) _speakingUsers.Add(player.UserID); else _speakingUsers.Remove(player.UserID);
			PlayerSpeakingChanged.Invoke(player, speaking);
		}
		VoiceLevelChanged.Invoke(player, level);
		VoiceIndicator indicator = GetIndicator(player);
		bool muted = player == Root.Players.LocalPlayer ? !_microphoneEnabled : IsPlayerMuted(player);
		indicator.UpdateVisual(_indicatorEnabled && (alwaysVisible || speaking || muted), level, muted, _indicatorColor, _indicatorIdleColor, _indicatorMutedColor);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Unreliable, TransferChannel = 3)]
	private async void NetServerVoiceLevel(float level)
	{
		Player? sender = Root.Players.GetPlayerFromPeerID(RemoteSenderId);
		if (sender == null || !sender.CanVoiceChat || !float.IsFinite(level)) return;
		if (!_levelLimits.TryGetValue(sender, out SlidingWindowRateLimiter? limiter)) _levelLimits[sender] = limiter = new(15, TimeSpan.FromSeconds(1));
		if (!limiter.TryAccept()) return;
		level = Mathf.Clamp(level, 0f, 1f);
		foreach (Player recipient in Root.Players.GetChildrenOfClass<Player>().Where(player => player != sender && player.CanVoiceChat))
		{
			if (await Root.Social.WebIsBlockedEitherWay(sender.UserID, recipient.UserID)) continue;
			RpcId(recipient.PeerID, nameof(NetReceiveVoiceLevel), sender.UserID, level);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Unreliable, TransferChannel = 3)]
	private void NetReceiveVoiceLevel(string senderId, float level)
	{
		if (!_initialized || _mutedUsers.Contains(senderId) || !float.IsFinite(level)) return;
		Player? sender = Root.Players.GetPlayerByID(senderId); if (sender == null || !sender.CanVoiceChat) return;
		_voiceLevels[senderId] = Mathf.Clamp(level, 0f, 1f); _lastVoiceActivity[senderId] = Time.GetTicksMsec();
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Unreliable, TransferChannel = 3)]
	private async void NetServerVoicePacket(byte[] packet)
	{
		Player? sender = Root.Players.GetPlayerFromPeerID(RemoteSenderId);
		if (sender == null || !sender.CanVoiceChat || packet.Length is < 4 or > MaximumPacketBytes) return;
		if (!_voiceLimits.TryGetValue(sender, out SlidingWindowRateLimiter? limiter)) _voiceLimits[sender] = limiter = new(70, TimeSpan.FromSeconds(1));
		if (!limiter.TryAccept()) return;
		foreach (Player recipient in Root.Players.GetChildrenOfClass<Player>().Where(player => player != sender && player.CanVoiceChat))
		{
			if (await Root.Social.WebIsBlockedEitherWay(sender.UserID, recipient.UserID)) continue;
			RpcId(recipient.PeerID, nameof(NetReceiveVoicePacket), sender.UserID, packet);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Unreliable, TransferChannel = 3)]
	private void NetReceiveVoicePacket(string senderId, byte[] packet)
	{
		if (!_initialized || _mutedUsers.Contains(senderId)) return;
		Player? sender = Root.Players.GetPlayerByID(senderId); if (sender == null || !sender.CanVoiceChat) return;
		Node decoder = GetSpeaker(sender).Decoder; decoder.Call("tv_incomingaudiopacket", packet);
	}

	private (AudioStreamPlayer3D Player, Node Decoder) GetSpeaker(Player sender)
	{
		if (_speakers.TryGetValue(sender, out var existing)) return existing;
		float volume = _playerVolumes.GetValueOrDefault(sender.UserID, 1f);
		AudioStreamPlayer3D spatialPlayer = new() { Name = "VoiceChatAudio", MaxDistance = 85, UnitSize = 8, VolumeDb = ToVolumeDb(volume * _outputVolume), AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance };
		sender.GDNode3D.AddChild(spatialPlayer, false, Node.InternalMode.Back);
		Node decoder = ResourceLoader.Load<PackedScene>(SpeakerScene).Instantiate(); spatialPlayer.AddChild(decoder);
		return _speakers[sender] = (spatialPlayer, decoder);
	}

	private VoiceIndicator GetIndicator(Player player)
	{
		if (_indicators.TryGetValue(player, out VoiceIndicator? indicator)) return indicator;
		indicator = new VoiceIndicator(player, ResourceLoader.Load<Texture2D>(MicrophoneIcon), ResourceLoader.Load<Texture2D>(MutedIcon));
		player.GDNode3D.AddChild(indicator, false, Node.InternalMode.Back); return _indicators[player] = indicator;
	}

	private void OnInput(InputEvent input)
	{
		if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mouse) return;
		Camera3D? camera = Root.Environment.CurrentGDCamera; if (camera == null) return;
		Player? closest = null; float distance = 28f;
		foreach ((Player player, VoiceIndicator indicator) in _indicators)
		{
			if (!indicator.Visible || camera.IsPositionBehind(indicator.GlobalPosition)) continue;
			float candidate = camera.UnprojectPosition(indicator.GlobalPosition).DistanceTo(mouse.Position);
			if (candidate < distance) { distance = candidate; closest = player; }
		}
		if (closest != null) TogglePlayerMuted(closest);
	}

	private void OnPlayerRemoved(Player player)
	{
		_voiceLimits.Remove(player); _levelLimits.Remove(player); _mutedUsers.Remove(player.UserID); _playerVolumes.Remove(player.UserID); _voiceLevels.Remove(player.UserID); _lastVoiceActivity.Remove(player.UserID); _speakingUsers.Remove(player.UserID);
		if (_speakers.Remove(player, out var speaker)) speaker.Player.QueueFree();
		if (_indicators.Remove(player, out VoiceIndicator? indicator)) indicator.QueueFree();
	}

	private static float ToVolumeDb(float volume) => volume <= 0f ? -80f : Mathf.LinearToDb(volume);

	public override void ExitTree()
	{
		SetMicrophoneEnabled(false); Root.Players.PlayerRemoved.Disconnect(OnPlayerRemoved);
		if (!Root.Network.IsServer) Root.Input.GodotInputEvent -= OnInput;
		base.ExitTree();
	}

	private sealed partial class VoiceIndicator : Sprite3D
	{
		private readonly Player _target;
		private readonly Texture2D _microphone;
		private readonly Texture2D _muted;

		public VoiceIndicator(Player target, Texture2D microphone, Texture2D muted)
		{
			_target = target; _microphone = microphone; _muted = muted;
			Name = "VoiceChatBubble"; Texture = microphone; Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
			NoDepthTest = true; FixedSize = true; PixelSize = 0.0012f; RenderPriority = 10; Visible = false;
		}

		public override void _Process(double delta)
		{
			Position = new Vector3(0, Mathf.Max(2.1f, _target.CalculateBounds().Size.Y * 0.5f + 0.35f), 0);
		}

		public void UpdateVisual(bool visible, float level, bool muted, Color active, Color idle, Color mutedColor)
		{
			Visible = visible; Texture = muted ? _muted : _microphone;
			Modulate = muted ? mutedColor : idle.Lerp(active, Mathf.Clamp(level * 2.5f, 0f, 1f));
			Scale = Vector3.One * (0.62f + Mathf.Clamp(level, 0f, 1f) * 0.08f);
		}
	}
}
