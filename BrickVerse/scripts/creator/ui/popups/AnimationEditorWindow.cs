// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Creator.Utils;
using BrickVerse.Formats;
using BrickVerse.Shared;
using BrickVerse.Datamodel.Creator;
using DatamodelWorld = BrickVerse.Datamodel.World;
using DatamodelDynamic = BrickVerse.Datamodel.Dynamic;
using BrickVerse.Shared.AssetLoaders;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>
/// Native Creator editor for BrickVerse skeletal animation clips. FBX/GLTF files
/// are imported through Godot and converted to editable .bvanim tracks.
/// </summary>
public sealed partial class AnimationEditorWindow : PopupWindowBase
{
	private enum PoseTool
	{
		None,
		Move,
		Rotate,
		Scale,
	}

	private BVAnimationClip _clip = CreateDefaultClip();
	private Tree _tracks = null!;
	private ItemList _keys = null!;
	private LineEdit _name = null!;
	private SpinBox _length = null!;
	private OptionButton _loop = null!;
	private SpinBox _time = null!;
	private SpinBox _transition = null!;
	private OptionButton _interpolation = null!;
	private SpinBox[] _values = [];
	private Label _status = null!;
	private AnimationTimeline _timeline = null!;
	private AnimationPlayer? _previewPlayer;
	private SubViewport? _previewViewport;
	private Camera3D? _previewCamera;
	private Skeleton3D? _previewSkeleton;
	private MeshInstance3D? _boneGizmo;
	private BrickVerse.Creator.Gizmos? _previewGizmos;
	private DatamodelDynamic? _poseAdapter;
	private OptionButton _boneChoice = null!;
	private CheckButton _autoKey = null!;
	private HSlider _playhead = null!;
	private Button _play = null!;
	private Vector3 _cameraTarget = new(0, 2.6f, 0);
	private float _cameraYaw;
	private float _cameraPitch = -0.08f;
	private float _cameraDistance = 8.5f;
	private bool _orbiting;
	private bool _panning;
	private bool _posingBone;
	private bool _previewHovered;
	private BVAnimationKey? _draggedTimelineKey;
	private int _draggedTimelineTrack = -1;
	private bool _timelineDragUndoCaptured;
	private PoseTool _poseTool = PoseTool.Rotate;
	private int _activeGizmoAxis = -1;
	private int _selectedTrack = -1;
	private int _selectedKey = -1;
	public string? InitialFilePath { get; set; }
	private Control _welcome = null!;
	private Control _editorRoot = null!;
	private BrickVerse.Datamodel.AnimationTrack? _documentTrack;
	private PopupMenu _keyframeContext = null!;
	private const int ContextEdit = 1;
	private const int ContextReset = 2;
	private const int ContextDelete = 3;
	private const int ContextDuplicate = 4;
	private readonly List<byte[]> _undoHistory = [];
	private readonly List<byte[]> _redoHistory = [];
	private bool _restoringHistory;

	public override void _Ready()
	{
		base._Ready();
		BuildInterface();
		RefreshAll();
		if (!string.IsNullOrWhiteSpace(InitialFilePath))
		{
			LoadAnimationFile(InitialFilePath);
			ShowEditor();
		}
	}

	private void BuildInterface()
	{
		_welcome = GetNode<Control>("Welcome");
		_editorRoot = GetNode<Control>("Editor");
		_tracks = GetNode<Tree>("Editor/Main/Tracks/Tree");
		_keys = GetNode<ItemList>("Editor/Main/Workspace/Bottom/Keys/List");
		_keys.AllowRmbSelect = true;
		_name = GetNode<LineEdit>("Editor/Toolbar/Name");
		_length = GetNode<SpinBox>("Editor/Toolbar/Length");
		_loop = GetNode<OptionButton>("Editor/Toolbar/Loop");
		_time = GetNode<SpinBox>("Editor/KeyframePopup/Inspector/Time");
		_transition = GetNode<SpinBox>("Editor/KeyframePopup/Inspector/Transition");
		_interpolation = GetNode<OptionButton>("Editor/KeyframePopup/Inspector/Interpolation");
		_values = [
			GetNode<SpinBox>("Editor/KeyframePopup/Inspector/Values/X"),
			GetNode<SpinBox>("Editor/KeyframePopup/Inspector/Values/Y"),
			GetNode<SpinBox>("Editor/KeyframePopup/Inspector/Values/Z"),
			GetNode<SpinBox>("Editor/KeyframePopup/Inspector/Values/W"),
		];
		_status = GetNode<Label>("Editor/Status");
		_boneChoice = GetNode<OptionButton>("Editor/Main/Workspace/Playback/Bone");
		_autoKey = GetNode<CheckButton>("Editor/Main/Workspace/Playback/AutoKey");
		_playhead = GetNode<HSlider>("Editor/Main/Workspace/Playback/Playhead");
		_play = GetNode<Button>("Editor/Main/Workspace/Playback/Play");
		_keyframeContext = GetNode<PopupMenu>("Editor/KeyframeContext");
		_keyframeContext.AddItem("Edit properties", ContextEdit);
		_keyframeContext.AddItem("Duplicate  Ctrl+D", ContextDuplicate);
		_keyframeContext.AddSeparator();
		_keyframeContext.AddItem("Reset keyframe", ContextReset);
		_keyframeContext.AddItem("Delete keyframe  Del", ContextDelete);
		_keyframeContext.IdPressed += HandleKeyframeContext;

		foreach (string value in new[] { "None", "Linear", "Pingpong" }) _loop.AddItem(value);
		foreach (string value in new[] { "Linear", "Nearest", "Cubic" }) _interpolation.AddItem(value);
		_tracks.SetColumnTitle(0, "Bone");
		_tracks.SetColumnTitle(1, "Channel");

		_timeline = new AnimationTimeline { CustomMinimumSize = new Vector2(0, 165) };
		GetNode<PanelContainer>("Editor/Main/Workspace/TimelineHost").AddChild(_timeline);
		_timeline.GuiInput += HandleTimelineInput;
		CreatePreview(GetNode<Control>("Editor/Main/Workspace/Preview"));
		PopulateBonePicker();

		GetNode<Button>("Welcome/Card/Stack/NewTrack").Pressed += () => { NewClip(); ShowEditor(); };
		GetNode<Button>("Welcome/Card/Stack/ImportFile").Pressed += OpenImport;
		GetNode<Button>("Welcome/Card/Stack/AssetRow/ImportAsset").Pressed += ImportAssetId;
		GetNode<Button>("Editor/Toolbar/Back").Pressed += ShowWelcome;
		GetNode<Button>("Editor/Toolbar/New").Pressed += () => { NewClip(); EnsureDocumentTrack(); };
		GetNode<Button>("Editor/Toolbar/Import").Pressed += OpenImport;
		GetNode<Button>("Editor/Toolbar/Save").Pressed += SaveClip;
		GetNode<Button>("Editor/Toolbar/Publish").Pressed += OpenPublish;
		GetNode<Button>("Editor/Main/Tracks/Actions/Position").Pressed += () => AddTrack("position");
		GetNode<Button>("Editor/Main/Tracks/Actions/Rotation").Pressed += () => AddTrack("rotation");
		GetNode<Button>("Editor/Main/Tracks/Actions/Delete").Pressed += DeleteTrack;
		GetNode<Button>("Editor/Main/Workspace/Playback/Stop").Pressed += StopPlayback;
		_play.Pressed += TogglePlayback;
		Button moveButton = GetNode<Button>("Editor/Main/Workspace/Tools/Move");
		Button rotateButton = GetNode<Button>("Editor/Main/Workspace/Tools/Rotate");
		Button scaleButton = GetNode<Button>("Editor/Main/Workspace/Tools/Scale");
		ButtonGroup poseTools = new();
		moveButton.ButtonGroup = poseTools;
		rotateButton.ButtonGroup = poseTools;
		scaleButton.ButtonGroup = poseTools;
		moveButton.Pressed += () => SetPoseTool(PoseTool.Move);
		rotateButton.Pressed += () => SetPoseTool(PoseTool.Rotate);
		scaleButton.Pressed += () => SetPoseTool(PoseTool.Scale);
		GetNode<CheckButton>("Editor/Main/Workspace/Playback/Wireframe").Toggled += SetPreviewWireframe;
		GetNode<Button>("Editor/Main/Workspace/Playback/ResetCamera").Pressed += ResetPreviewCamera;
		GetNode<Button>("Editor/Main/Workspace/Bottom/Keys/Actions/Add").Pressed += AddKey;
		GetNode<Button>("Editor/Main/Workspace/Bottom/Keys/Actions/Delete").Pressed += DeleteKey;
		_keys.GuiInput += HandleKeyListInput;

		_tracks.ItemSelected += SelectTrack;
		_tracks.ItemEdited += CommitTrackPath;
		_keys.ItemSelected += SelectKey;
		_boneChoice.ItemSelected += index => SelectPreviewBone((int)index);
		_playhead.ValueChanged += SeekPreview;
		_name.TextChanged += value => { if (_clip.Name != value) PushUndo(); _clip.Name = value; _timeline.QueueRedraw(); RefreshPreview(); };
		_length.ValueChanged += value => { if (!Mathf.IsEqualApprox(_clip.Length, (float)value)) PushUndo(); _clip.Length = (float)value; _timeline.QueueRedraw(); RefreshPreview(); };
		_loop.ItemSelected += index => { string mode = _loop.GetItemText((int)index); if (_clip.LoopMode != mode) PushUndo(); _clip.LoopMode = mode; RefreshPreview(); };
		_interpolation.ItemSelected += _ => ApplyTrackInterpolation();
		_time.ValueChanged += _ => ApplyKeyFields();
		_transition.ValueChanged += _ => ApplyKeyFields();
		foreach (SpinBox value in _values) value.ValueChanged += _ => ApplyKeyFields();
	}

	public override void _UnhandledKeyInput(InputEvent input)
	{
		if (!_editorRoot.Visible || input is not InputEventKey { Pressed: true, Echo: false } key) return;
		if (key.CtrlPressed && key.Keycode == Key.Z && key.ShiftPressed) Redo();
		else if (key.CtrlPressed && key.Keycode == Key.Z) Undo();
		else if (key.CtrlPressed && key.Keycode == Key.Y) Redo();
		else if (key.CtrlPressed && key.Keycode == Key.D) DuplicateSelectedKey();
		else if (key.CtrlPressed && key.Keycode == Key.S) SaveClip();
		else if (IsEditingText()) return;
		else if (key.Keycode == Key.Key1) SetPoseTool(PoseTool.Move);
		else if (key.Keycode == Key.Key2) SetPoseTool(PoseTool.Rotate);
		else if (key.Keycode == Key.Key3) SetPoseTool(PoseTool.Scale);
		else if (key.Keycode == Key.K) AddKey();
		else if (key.Keycode == Key.Delete) DeleteKey();
		else if (key.Keycode == Key.F) ResetPreviewCamera();
		else if (key.Keycode == Key.Space) TogglePlayback();
		else return;
		GetViewport().SetInputAsHandled();
	}

	private void ProcessPreviewFreecam(double delta)
	{
		if (!_previewHovered || _previewCamera == null || IsEditingText()) return;
		Vector3 direction = Vector3.Zero;
		Vector3 forward = -_previewCamera.GlobalBasis.Z;
		Vector3 right = _previewCamera.GlobalBasis.X;
		if (Input.IsKeyPressed(Key.W)) direction += forward;
		if (Input.IsKeyPressed(Key.S)) direction -= forward;
		if (Input.IsKeyPressed(Key.D)) direction += right;
		if (Input.IsKeyPressed(Key.A)) direction -= right;
		if (Input.IsKeyPressed(Key.E)) direction += Vector3.Up;
		if (Input.IsKeyPressed(Key.Q)) direction -= Vector3.Up;
		if (direction.IsZeroApprox()) return;
		float speed = Input.IsKeyPressed(Key.Shift) ? 10f : 4f;
		_cameraTarget += direction.Normalized() * speed * (float)delta;
		UpdatePreviewCamera();
	}

	private bool IsEditingText()
	{
		Control? focus = GetViewport().GuiGetFocusOwner();
		return focus is LineEdit || focus is TextEdit || focus is SpinBox;
	}

	private void ShowEditor() { _welcome.Visible = false; _editorRoot.Visible = true; EnsureDocumentTrack(); }
	private void ShowWelcome() { _editorRoot.Visible = false; _welcome.Visible = true; }

	private void EnsureDocumentTrack()
	{
		bool created = false;
		if (_documentTrack == null && DatamodelWorld.Current != null)
		{
			_documentTrack = DatamodelWorld.Current.New<BrickVerse.Datamodel.AnimationTrack>(DatamodelWorld.Current);
			created = true;
		}
		if (_documentTrack == null) return;
		_documentTrack.SetClip(_clip);
		if (created) DatamodelWorld.Current?.CreatorContext.Selections.SelectOnly(_documentTrack);
	}

	private void SelectTimelinePosition(Vector2 pointer)
	{
		if (_clip.Length <= 0 || _clip.Tracks.Count == 0 || pointer.X < 150) return;
		float timelineWidth = Math.Max(1, _timeline.Size.X - 162);
		double time = Math.Clamp((pointer.X - 150) / timelineWidth * _clip.Length, 0, _clip.Length);
		float rowHeight = Math.Max(22, (_timeline.Size.Y - 22) / Math.Max(1, _clip.Tracks.Count));
		int trackIndex = Math.Clamp((int)((pointer.Y - 24) / rowHeight), 0, _clip.Tracks.Count - 1);
		_selectedTrack = trackIndex;
		_selectedKey = -1;
		if (_clip.Tracks.Count > 0)
		{
			BVAnimationTrack track = _clip.Tracks[trackIndex];
			for (int index = 0; index < track.Keys.Count; index++)
			{
				float keyX = 150 + timelineWidth * (float)(track.Keys[index].Time / _clip.Length);
				if (Math.Abs(pointer.X - keyX) <= 8) { _selectedKey = index; time = track.Keys[index].Time; break; }
			}
		}
		_playhead.SetValueNoSignal(time);
		SeekPreview(time);
		RefreshAll();
	}

	private void HandleTimelineInput(InputEvent input)
	{
		if (input is InputEventMouseButton button)
		{
			if (button.ButtonIndex == MouseButton.Left)
			{
				if (button.Pressed && TryHitTimelineKey(button.Position, out int track, out int key))
				{
					SelectTimelineKey(track, key);
					_draggedTimelineTrack = track;
					_draggedTimelineKey = _clip.Tracks[track].Keys[key];
					_timelineDragUndoCaptured = false;
				}
				else if (button.Pressed) SelectTimelinePosition(button.Position);
				else { _draggedTimelineKey = null; _draggedTimelineTrack = -1; _timelineDragUndoCaptured = false; }
			}
			else if (button.ButtonIndex == MouseButton.Right && button.Pressed)
			{
				if (TryHitTimelineKey(button.Position, out int track, out int key))
				{
					SelectTimelineKey(track, key);
					ShowKeyframeContext();
				}
				else _status.Text = "Right-click a keyframe diamond for actions.";
				_timeline.AcceptEvent();
			}
		}
		else if (input is InputEventMouseMotion motion && _draggedTimelineKey != null)
		{
			if (!_timelineDragUndoCaptured) { PushUndo(); _timelineDragUndoCaptured = true; }
			float width = Math.Max(1, _timeline.Size.X - 162);
			_draggedTimelineKey.Time = Math.Clamp((motion.Position.X - 150) / width * _clip.Length, 0, _clip.Length);
			BVAnimationTrack track = _clip.Tracks[_draggedTimelineTrack];
			track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
			_selectedKey = track.Keys.IndexOf(_draggedTimelineKey);
			_playhead.SetValueNoSignal(_draggedTimelineKey.Time);
			_timeline.Playhead = _draggedTimelineKey.Time;
			RefreshKeys(false);
		}
	}

	private bool TryHitTimelineKey(Vector2 pointer, out int trackIndex, out int keyIndex)
	{
		trackIndex = -1;
		keyIndex = -1;
		if (_clip.Length <= 0 || pointer.X < 142 || _clip.Tracks.Count == 0) return false;
		float width = Math.Max(1, _timeline.Size.X - 162);
		float rowHeight = Math.Max(22, (_timeline.Size.Y - 22) / Math.Max(1, _clip.Tracks.Count));
		int row = (int)((pointer.Y - 24) / rowHeight);
		if (row < 0 || row >= _clip.Tracks.Count) return false;
		for (int index = 0; index < _clip.Tracks[row].Keys.Count; index++)
		{
			float x = 150 + width * (float)(_clip.Tracks[row].Keys[index].Time / _clip.Length);
			float y = 24 + row * rowHeight + rowHeight * 0.5f;
			if (pointer.DistanceTo(new Vector2(x, y)) > 15) continue;
			trackIndex = row;
			keyIndex = index;
			return true;
		}
		return false;
	}

	private void SelectTimelineKey(int trackIndex, int keyIndex)
	{
		_selectedTrack = trackIndex;
		_selectedKey = keyIndex;
		SelectBoneForTrack(trackIndex);
		double time = _clip.Tracks[trackIndex].Keys[keyIndex].Time;
		_playhead.SetValueNoSignal(time);
		SeekPreview(time);
		RefreshAll();
	}

	private void HandleKeyListInput(InputEvent input)
	{
		if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } mouse)
		{
			int item = _keys.GetItemAtPosition(mouse.Position, true);
			if (item >= 0) { _keys.Select(item); SelectKey(item); ShowKeyframeContext(); }
			_keys.AcceptEvent();
		}
	}

	private void ShowKeyframeContext()
	{
		if (_selectedTrack < 0 || _selectedKey < 0)
		{
			_status.Text = "Select a keyframe first.";
			return;
		}
		_keyframeContext.Popup(new Rect2I((Vector2I)GetViewport().GetMousePosition(), Vector2I.Zero));
	}

	private void HandleKeyframeContext(long id)
	{
		switch (id)
		{
			case ContextEdit: OpenKeyframeProperties(); break;
			case ContextDuplicate: DuplicateSelectedKey(); break;
			case ContextReset: ResetSelectedKey(); break;
			case ContextDelete: DeleteKey(); break;
		}
	}

	private void ResetSelectedKey()
	{
		if (_selectedTrack < 0 || _selectedKey < 0) return;
		PushUndo();
		BVAnimationTrack track = _clip.Tracks[_selectedTrack];
		BVAnimationKey key = track.Keys[_selectedKey];
		key.Value = track.Channel == "rotation" ? [0, 0, 0, 1] : track.Channel == "scale" ? [1, 1, 1] : [0, 0, 0];
		key.Transition = 1;
		RefreshKeys();
		_status.Text = "Keyframe reset";
	}

	private byte[] CaptureClip() => BVAnimationFormat.Write(_clip);

	private void PushUndo()
	{
		if (_restoringHistory) return;
		byte[] state;
		try { state = CaptureClip(); }
		catch { return; }
		if (_undoHistory.Count > 0 && _undoHistory[^1].SequenceEqual(state)) return;
		_undoHistory.Add(state);
		if (_undoHistory.Count > 100) _undoHistory.RemoveAt(0);
		_redoHistory.Clear();
	}

	private void Undo()
	{
		if (_undoHistory.Count == 0) { _status.Text = "Nothing to undo"; return; }
		byte[] target = _undoHistory[^1];
		_undoHistory.RemoveAt(_undoHistory.Count - 1);
		_redoHistory.Add(CaptureClip());
		RestoreHistory(target, "Undo");
	}

	private void Redo()
	{
		if (_redoHistory.Count == 0) { _status.Text = "Nothing to redo"; return; }
		byte[] target = _redoHistory[^1];
		_redoHistory.RemoveAt(_redoHistory.Count - 1);
		_undoHistory.Add(CaptureClip());
		RestoreHistory(target, "Redo");
	}

	private void RestoreHistory(byte[] state, string action)
	{
		_restoringHistory = true;
		try
		{
			_clip = BVAnimationFormat.Read(state);
			_selectedTrack = Math.Clamp(_selectedTrack, -1, _clip.Tracks.Count - 1);
			_selectedKey = _selectedTrack >= 0
				? Math.Clamp(_selectedKey, -1, _clip.Tracks[_selectedTrack].Keys.Count - 1)
				: -1;
			RefreshAll();
			_status.Text = action;
		}
		finally { _restoringHistory = false; }
	}

	private void DuplicateSelectedKey()
	{
		if (_selectedTrack < 0 || _selectedKey < 0) { _status.Text = "Select a keyframe to duplicate."; return; }
		PushUndo();
		BVAnimationTrack track = _clip.Tracks[_selectedTrack];
		BVAnimationKey source = track.Keys[_selectedKey];
		BVAnimationKey duplicate = new()
		{
			Time = Math.Min(_clip.Length, source.Time + Math.Max(1f / 30f, _clip.Length / 100f)),
			Transition = source.Transition,
			Value = source.Value.ToArray(),
		};
		track.Keys.Add(duplicate);
		track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
		_selectedKey = track.Keys.IndexOf(duplicate);
		RefreshKeys();
		_status.Text = "Keyframe duplicated";
	}

	private void SelectBoneForTrack(int trackIndex)
	{
		if (_previewSkeleton == null || trackIndex < 0 || trackIndex >= _clip.Tracks.Count) return;
		string path = _clip.Tracks[trackIndex].Path;
		int separator = path.LastIndexOf(':');
		string boneName = separator >= 0 ? path[(separator + 1)..] : path.GetFile();
		int bone = _previewSkeleton.FindBone(boneName);
		if (bone < 0) return;
		_boneChoice.Select(bone);
		SelectPreviewBone(bone);
	}

	private void OpenKeyframeProperties()
	{
		if (_selectedTrack < 0 || _selectedKey < 0)
		{
			_status.Text = "Select a keyframe to edit its properties.";
			return;
		}
		LoadKeyFields();
		GetNode<PopupPanel>("Editor/KeyframePopup").PopupCentered(new Vector2I(390, 300));
	}

	private void ImportAssetId()
	{
		string id = GetNode<LineEdit>("Welcome/Card/Stack/AssetRow/AssetId").Text.Trim();
		if (id.Length == 0) return;
		GetNode<Button>("Welcome/Card/Stack/AssetRow/ImportAsset").Disabled = true;
		AssetLoader.Singleton.GetResource(new CacheItem { Type = ResourceType.Animation, ID = id }, resource =>
		{
			GetNode<Button>("Welcome/Card/Stack/AssetRow/ImportAsset").Disabled = false;
			if (resource is not AnimationLibrary library || library.GetAnimationList().Count == 0)
			{
				CreatorService.Interface.PopupAlert("That asset does not contain an animation.", "Animation Import Failed");
				return;
			}
			string animationName = library.GetAnimationList()[0];
			_documentTrack = null;
			_clip = BVAnimationFormat.FromAnimation(animationName, library.GetAnimation(animationName));
			RefreshAll();
			ShowEditor();
		});
	}

	private void BuildLegacyInterface()
	{
		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		AddChild(margin);
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		VBoxContainer root = new();
		margin.AddChild(root);
		root.AddThemeConstantOverride("separation", 8);

		HBoxContainer toolbar = new();
		root.AddChild(toolbar);
		AddButton(toolbar, "New", NewClip);
		AddButton(toolbar, "Import FBX / GLTF", OpenImport);
		AddButton(toolbar, "Open .bvanim", OpenImport);
		AddButton(toolbar, "Save", SaveClip);
		AddButton(toolbar, "Publish", OpenPublish);

		_name = new LineEdit { PlaceholderText = "Animation name", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_name.TextChanged += value => { _clip.Name = value; _timeline.QueueRedraw(); RefreshPreview(); };
		toolbar.AddChild(_name);

		_length = new SpinBox { MinValue = 0.01, MaxValue = 3600, Step = 0.01, Suffix = " s" };
		_length.ValueChanged += value =>
		{
			_clip.Length = (float)value;
			_timeline.QueueRedraw();
			RefreshPreview();
		};
		toolbar.AddChild(_length);

		_loop = new OptionButton();
		foreach (string value in new[] { "None", "Linear", "Pingpong" })
			_loop.AddItem(value);
		_loop.ItemSelected += index => _clip.LoopMode = _loop.GetItemText((int)index);
		toolbar.AddChild(_loop);

		HSplitContainer split = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		root.AddChild(split);

		VBoxContainer trackPanel = new() { CustomMinimumSize = new Vector2(310, 0) };
		split.AddChild(trackPanel);
		Label trackTitle = new() { Text = "Tracks" };
		trackPanel.AddChild(trackTitle);
		_tracks = new Tree { Columns = 2, HideRoot = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		_tracks.SetColumnTitle(0, "Target");
		_tracks.SetColumnTitle(1, "Channel");
		_tracks.ColumnTitlesVisible = true;
		_tracks.ItemSelected += SelectTrack;
		_tracks.ItemEdited += CommitTrackPath;
		trackPanel.AddChild(_tracks);
		HBoxContainer trackActions = new();
		trackPanel.AddChild(trackActions);
		AddButton(trackActions, "+ Position", () => AddTrack("position"));
		AddButton(trackActions, "+ Rotation", () => AddTrack("rotation"));
		AddButton(trackActions, "+ Scale", () => AddTrack("scale"));
		AddButton(trackActions, "Delete", DeleteTrack);

		VBoxContainer editor = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		split.AddChild(editor);

		PanelContainer previewSurface = new()
		{
			CustomMinimumSize = new Vector2(0, 300),
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		previewSurface.AddThemeStyleboxOverride("panel", SurfaceStyle(new Color("15181d")));
		editor.AddChild(previewSurface);
		CreatePreview(previewSurface);

		HBoxContainer playback = new();
		editor.AddChild(playback);
		_play = AddButton(playback, "▶", TogglePlayback);
		AddButton(playback, "■", StopPlayback);
		ButtonGroup poseTools = new();
		Button moveTool = AddButton(playback, "Move", () => SetPoseTool(PoseTool.Move));
		moveTool.ToggleMode = true;
		moveTool.ButtonGroup = poseTools;
		moveTool.TooltipText = "Move the selected bone using the XYZ gizmo";
		Button rotateTool = AddButton(playback, "Rotate", () => SetPoseTool(PoseTool.Rotate));
		rotateTool.ToggleMode = true;
		rotateTool.ButtonPressed = true;
		rotateTool.ButtonGroup = poseTools;
		rotateTool.TooltipText = "Rotate the selected bone using the XYZ gizmo";
		_boneChoice = new OptionButton { TooltipText = "Select a Brickversian bone to pose" };
		_boneChoice.ItemSelected += index => SelectPreviewBone((int)index);
		playback.AddChild(_boneChoice);
		_autoKey = new CheckButton
		{
			Text = "Auto Key",
			ButtonPressed = true,
			TooltipText = "Create or update a rotation key whenever a bone gizmo is moved",
		};
		playback.AddChild(_autoKey);
		CheckButton wireframe = new() { Text = "Wireframe" };
		wireframe.Toggled += enabled => SetPreviewWireframe(enabled);
		playback.AddChild(wireframe);
		AddButton(playback, "Reset Camera", ResetPreviewCamera);
		PopulateBonePicker();
		CreateBoneGizmo();
		_playhead = new HSlider
		{
			MinValue = 0,
			MaxValue = _clip.Length,
			Step = 0.001,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_playhead.ValueChanged += SeekPreview;
		playback.AddChild(_playhead);

		_timeline = new AnimationTimeline { CustomMinimumSize = new Vector2(0, 165) };
		PanelContainer timelineSurface = new();
		timelineSurface.AddThemeStyleboxOverride("panel", SurfaceStyle(new Color("15181d")));
		timelineSurface.AddChild(_timeline);
		editor.AddChild(timelineSurface);

		HSplitContainer keySplit = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		editor.AddChild(keySplit);
		VBoxContainer keyPanel = new() { CustomMinimumSize = new Vector2(250, 0) };
		keySplit.AddChild(keyPanel);
		keyPanel.AddChild(new Label { Text = "Keyframes" });
		_keys = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		_keys.ItemSelected += SelectKey;
		keyPanel.AddChild(_keys);
		HBoxContainer keyActions = new();
		keyPanel.AddChild(keyActions);
		AddButton(keyActions, "Add Key", AddKey);
		AddButton(keyActions, "Delete Key", DeleteKey);

		VBoxContainer inspector = new() { CustomMinimumSize = new Vector2(280, 0) };
		keySplit.AddChild(inspector);
		inspector.AddChild(new Label { Text = "Keyframe" });
		_interpolation = new OptionButton();
		foreach (string value in new[] { "Linear", "Nearest", "Cubic" })
			_interpolation.AddItem(value);
		_interpolation.ItemSelected += _ => ApplyTrackInterpolation();
		AddLabeledControl(inspector, "Interpolation", _interpolation);
		_time = AddSpin(inspector, "Time", 0, 3600, 0.01);
		_time.ValueChanged += _ => ApplyKeyFields();
		_transition = AddSpin(inspector, "Transition", -8, 8, 0.05);
		_transition.TooltipText = "Godot easing curve: 1 is linear; values above/below 1 ease the key.";
		_transition.ValueChanged += _ => ApplyKeyFields();
		_values = new[]
		{
			AddSpin(inspector, "X", -100000, 100000, 0.001),
			AddSpin(inspector, "Y", -100000, 100000, 0.001),
			AddSpin(inspector, "Z", -100000, 100000, 0.001),
			AddSpin(inspector, "W", -100000, 100000, 0.001),
		};
		foreach (SpinBox value in _values)
			value.ValueChanged += _ => ApplyKeyFields();
		inspector.AddChild(
			new Label
			{
				Text = "Tracks target native Brickversian bones.\nCamera navigation never changes animation data.",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			}
		);

		_status = new Label { Text = "Ready" };
		root.AddChild(_status);
	}

	public override void _Process(double delta)
	{
		ProcessPreviewFreecam(delta);
		if (_previewPlayer?.IsPlaying() == true)
		{
			_playhead.SetValueNoSignal(_previewPlayer.CurrentAnimationPosition);
			_timeline.Playhead = _previewPlayer.CurrentAnimationPosition;
			_timeline.QueueRedraw();
		}
		UpdateBoneGizmo();
	}

	private static StyleBoxFlat SurfaceStyle(Color color) =>
		new()
		{
			BgColor = color,
			BorderColor = new Color(1, 1, 1, 0.08f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 5,
			CornerRadiusTopRight = 5,
			CornerRadiusBottomLeft = 5,
			CornerRadiusBottomRight = 5,
		};

	private void CreatePreview(Control parent)
	{
		SubViewportContainer container = new() { Stretch = true, FocusMode = Control.FocusModeEnum.All };
		container.GuiInput += HandlePreviewInput;
		container.MouseEntered += () => _previewHovered = true;
		container.MouseExited += () => _previewHovered = false;
		parent.AddChild(container);
		_previewViewport = new SubViewport
		{
			Size = new Vector2I(700, 360),
			TransparentBg = false,
			OwnWorld3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		container.AddChild(_previewViewport);

		WorldEnvironment environment = new()
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color("20242b"),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color("dbe8ff"),
				AmbientLightEnergy = 0.85f,
			},
		};
		_previewViewport.AddChild(environment);
		DirectionalLight3D light = new()
		{
			RotationDegrees = new Vector3(-45, -25, 0),
			LightEnergy = 1.5f,
			ShadowEnabled = true,
		};
		_previewViewport.AddChild(light);
		_previewCamera = new Camera3D { Current = true };
		_previewViewport.AddChild(_previewCamera);
		UpdatePreviewCamera();

		Node3D rig = GD.Load<PackedScene>("res://scenes/datamodel/BrickversianModal.tscn").Instantiate<Node3D>();
		_previewViewport.AddChild(rig);
		_previewPlayer = rig.GetNodeOrNull<AnimationPlayer>("Character/AnimationPlayer");
		_previewSkeleton = rig.GetNodeOrNull<Skeleton3D>("Character/Poly/Skeleton3D");
		ApplyNoobPreviewColors(_previewSkeleton);
		CreateStudioPoseGizmos();
		RefreshPreview();
	}

	private void CreateStudioPoseGizmos()
	{
		if (_previewViewport == null || _previewCamera == null || DatamodelWorld.Current == null) return;
		_poseAdapter = DatamodelWorld.Current.New<DatamodelDynamic>(DatamodelWorld.Current.TemporaryContainer);
		_poseAdapter.Name = "Animator Bone Pose";
		_poseAdapter.AutoUpdateNetTransform = false;

		_previewGizmos = new BrickVerse.Creator.Gizmos
		{
			CameraOverride = _previewCamera,
			ToolModeOverride = ToolModeEnum.Rotate,
			SuppressSelectionInput = true,
		};
		_previewGizmos.Attach(DatamodelWorld.Current);
		_previewViewport.AddChild(_previewGizmos);
		_previewGizmos.Selected.Add(_poseAdapter);
		_previewGizmos.Move.Targets.Add(_poseAdapter);
		_previewGizmos.Rotate.Targets.Add(_poseAdapter);
		_previewGizmos.Scale.Targets.Add(_poseAdapter);
		_previewGizmos.Move.Dragged += _ => ApplyPoseAdapterToBone();
		_previewGizmos.Rotate.Dragged += _ => ApplyPoseAdapterToBone();
		_previewGizmos.Scale.Dragged += _ => ApplyPoseAdapterToBone();
		_previewGizmos.Move.DragEnded += CommitPoseAdapter;
		_previewGizmos.Rotate.DragEnded += CommitPoseAdapter;
		_previewGizmos.Scale.DragEnded += CommitPoseAdapter;
		SyncPoseAdapterFromBone();
	}

	private void SyncPoseAdapterFromBone()
	{
		if (_poseAdapter == null || _previewSkeleton == null || _boneChoice == null) return;
		int bone = _boneChoice.Selected;
		if (bone < 0 || bone >= _previewSkeleton.GetBoneCount()) return;
		_poseAdapter.SetGlobalTransform(_previewSkeleton.GlobalTransform * _previewSkeleton.GetBoneGlobalPose(bone));
	}

	private void ApplyPoseAdapterToBone()
	{
		if (_poseAdapter == null || _previewSkeleton == null) return;
		int bone = _boneChoice.Selected;
		if (bone < 0 || bone >= _previewSkeleton.GetBoneCount()) return;
		Transform3D desiredGlobalPose = _previewSkeleton.GlobalTransform.AffineInverse() * _poseAdapter.GetGlobalTransform();
		int parent = _previewSkeleton.GetBoneParent(bone);
		Transform3D desiredLocal = parent >= 0
			? _previewSkeleton.GetBoneGlobalPose(parent).AffineInverse() * desiredGlobalPose
			: desiredGlobalPose;
		Transform3D pose = _previewSkeleton.GetBoneRest(bone).AffineInverse() * desiredLocal;
		_previewSkeleton.SetBonePosePosition(bone, pose.Origin);
		_previewSkeleton.SetBonePoseRotation(bone, pose.Basis.GetRotationQuaternion().Normalized());
		_previewSkeleton.SetBonePoseScale(bone, pose.Basis.Scale);
	}

	private void CommitPoseAdapter()
	{
		ApplyPoseAdapterToBone();
		if (_autoKey.ButtonPressed)
			KeySelectedBone(_poseTool switch { PoseTool.Move => "position", PoseTool.Scale => "scale", _ => "rotation" });
	}

	private static void ApplyNoobPreviewColors(Skeleton3D? skeleton)
	{
		if (skeleton == null)
			return;
		foreach (Node child in skeleton.GetChildren())
		{
			if (child is not MeshInstance3D mesh || mesh.Mesh == null)
				continue;
			Color tint =
				mesh.Name.ToString() switch
				{
					"Torso" => new Color("1d62d0"),
					"LeftLeg" or "RightLeg" => new Color("43a047"),
					_ => new Color("f6d54a"),
				};
			for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				Material? source = mesh.Mesh.SurfaceGetMaterial(surface);
				if (source?.Duplicate() is BaseMaterial3D material)
				{
					// Tinting the existing material preserves the head's face texture.
					material.AlbedoColor = tint;
					mesh.SetSurfaceOverrideMaterial(surface, material);
				}
				else
					mesh.SetSurfaceOverrideMaterial(
						surface,
						new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.72f }
					);
			}
		}
	}

	private void PopulateBonePicker()
	{
		if (_previewSkeleton == null || _boneChoice == null)
			return;
		_boneChoice.Clear();
		for (int index = 0; index < _previewSkeleton.GetBoneCount(); index++)
			_boneChoice.AddItem(_previewSkeleton.GetBoneName(index));
		if (_boneChoice.ItemCount > 0)
			_boneChoice.Select(0);
	}

	private void CreateBoneGizmo()
	{
		if (_previewViewport == null || _boneGizmo != null)
			return;
		ImmediateMesh axes = new();
		axes.SurfaceBegin(Mesh.PrimitiveType.Lines);
		foreach ((Color color, Vector3 endpoint) in new[]
		{
			(Colors.Red, Vector3.Right),
			(Colors.LimeGreen, Vector3.Up),
			(Colors.DodgerBlue, Vector3.Back),
		})
		{
			axes.SurfaceSetColor(color);
			axes.SurfaceAddVertex(Vector3.Zero);
			axes.SurfaceSetColor(color);
			axes.SurfaceAddVertex(endpoint * 0.55f);
		}
		axes.SurfaceEnd();
		StandardMaterial3D material = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			VertexColorUseAsAlbedo = true,
			NoDepthTest = true,
		};
		axes.SurfaceSetMaterial(0, material);
		_boneGizmo = new MeshInstance3D { Mesh = axes, TopLevel = true };
		_previewViewport.AddChild(_boneGizmo);
		UpdateBoneGizmo();
	}

	private void SelectPreviewBone(int index)
	{
		if (_previewSkeleton == null || index < 0 || index >= _previewSkeleton.GetBoneCount())
			return;
		SyncPoseAdapterFromBone();
	}

	private void UpdateBoneGizmo()
	{
		if (_previewGizmos != null)
		{
			if (!_previewGizmos.IsTransformingSelected) SyncPoseAdapterFromBone();
			return;
		}
		if (_boneGizmo == null || _previewSkeleton == null || _boneChoice == null)
			return;
		int bone = _boneChoice.Selected;
		_boneGizmo.Visible = bone >= 0 && bone < _previewSkeleton.GetBoneCount();
		if (_boneGizmo.Visible)
			_boneGizmo.GlobalTransform =
				_previewSkeleton.GlobalTransform * _previewSkeleton.GetBoneGlobalPose(bone);
	}

	private void SetPreviewWireframe(bool enabled)
	{
		if (_previewViewport != null)
			_previewViewport.DebugDraw = enabled
				? Viewport.DebugDrawEnum.Wireframe
				: Viewport.DebugDrawEnum.Disabled;
	}

	private void SetPoseTool(PoseTool tool)
	{
		_poseTool = tool;
		GetNode<Button>("Editor/Main/Workspace/Tools/Move").SetPressedNoSignal(tool == PoseTool.Move);
		GetNode<Button>("Editor/Main/Workspace/Tools/Rotate").SetPressedNoSignal(tool == PoseTool.Rotate);
		GetNode<Button>("Editor/Main/Workspace/Tools/Scale").SetPressedNoSignal(tool == PoseTool.Scale);
		if (_previewGizmos != null)
			_previewGizmos.ToolModeOverride = tool switch { PoseTool.Move => ToolModeEnum.Move, PoseTool.Scale => ToolModeEnum.Scale, _ => ToolModeEnum.Rotate };
		_status.Text = tool switch
		{
			PoseTool.Move => "Move tool [1]: drag a colored gizmo axis",
			PoseTool.Scale => "Scale tool [3]: drag a colored gizmo handle",
			_ => "Rotate tool [2]: drag a colored gizmo ring",
		};
	}

	public override void _ExitTree()
	{
		if (_poseAdapter != null && !_poseAdapter.IsDeleted) _poseAdapter.Delete();
		_poseAdapter = null;
		base._ExitTree();
	}

	private void HandlePreviewInput(InputEvent input)
	{
		if (input is InputEventMouseButton button)
		{
			if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
			{
				_cameraDistance = Math.Max(1.2f, _cameraDistance * 0.88f);
				UpdatePreviewCamera();
			}
			else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
			{
				_cameraDistance = Math.Min(80, _cameraDistance * 1.12f);
				UpdatePreviewCamera();
			}
			else if (button.ButtonIndex == MouseButton.Right)
				_orbiting = button.Pressed;
			else if (button.ButtonIndex == MouseButton.Middle)
				_panning = button.Pressed;
			else if (button.ButtonIndex == MouseButton.Left)
			{
				if (button.Pressed && _previewGizmos?.HoveringGizmos != true)
				{
					TrySelectBoneAt(button.Position);
				}
			}
		}
		else if (input is InputEventMouseMotion motion)
		{
			if (_orbiting)
			{
				_cameraYaw -= motion.Relative.X * 0.008f;
				_cameraPitch = Math.Clamp(_cameraPitch - motion.Relative.Y * 0.008f, -1.35f, 1.35f);
				UpdatePreviewCamera();
			}
			else if (_panning && _previewCamera != null)
			{
				float scale = _cameraDistance * 0.0015f;
				_cameraTarget +=
					_previewCamera.GlobalBasis.X * (-motion.Relative.X * scale)
					+ _previewCamera.GlobalBasis.Y * (motion.Relative.Y * scale);
				UpdatePreviewCamera();
			}
			else if (_posingBone)
				TransformSelectedBone(motion.Relative);
		}
	}

	private bool TrySelectBoneAt(Vector2 pointer)
	{
		if (_previewCamera == null || _previewSkeleton == null) return false;
		float closest = 34f;
		int closestBone = -1;
		for (int bone = 0; bone < _previewSkeleton.GetBoneCount(); bone++)
		{
			Vector3 boneWorld = _previewSkeleton.GlobalTransform * _previewSkeleton.GetBoneGlobalPose(bone).Origin;
			if (_previewCamera.IsPositionBehind(boneWorld)) continue;
			Vector2 boneScreen = _previewCamera.UnprojectPosition(boneWorld);
			float distance = pointer.DistanceTo(boneScreen);
			int parent = _previewSkeleton.GetBoneParent(bone);
			if (parent >= 0)
			{
				Vector3 parentWorld = _previewSkeleton.GlobalTransform * _previewSkeleton.GetBoneGlobalPose(parent).Origin;
				if (!_previewCamera.IsPositionBehind(parentWorld))
					distance = Math.Min(distance, DistanceToSegment(pointer, _previewCamera.UnprojectPosition(parentWorld), boneScreen));
			}
			if (distance < closest) { closest = distance; closestBone = bone; }
		}
		if (closestBone < 0) return false;
		_boneChoice.Select(closestBone);
		SelectPreviewBone(closestBone);
		_status.Text = $"Selected {_previewSkeleton.GetBoneName(closestBone)}";
		return true;
	}

	private bool TryPickGizmoAxis(Vector2 pointer, out int selectedAxis)
	{
		selectedAxis = -1;
		if (_previewCamera == null || _boneGizmo == null)
			return false;
		Vector3 origin = _boneGizmo.GlobalPosition;
		Vector2 screenOrigin = _previewCamera.UnprojectPosition(origin);
		float bestDistance = 14;
		for (int axis = 0; axis < 3; axis++)
		{
			Vector3 direction = axis switch
			{
				0 => _boneGizmo.GlobalBasis.X,
				1 => _boneGizmo.GlobalBasis.Y,
				_ => _boneGizmo.GlobalBasis.Z,
			};
			Vector2 endpoint = _previewCamera.UnprojectPosition(origin + direction * 0.55f);
			float distance = DistanceToSegment(pointer, screenOrigin, endpoint);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				selectedAxis = axis;
			}
		}
		return selectedAxis >= 0;
	}

	private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
	{
		Vector2 segment = to - from;
		float lengthSquared = segment.LengthSquared();
		if (lengthSquared <= 0.0001f)
			return point.DistanceTo(from);
		float amount = Math.Clamp((point - from).Dot(segment) / lengthSquared, 0, 1);
		return point.DistanceTo(from + segment * amount);
	}

	private void UpdatePreviewCamera()
	{
		if (_previewCamera == null)
			return;
		Vector3 offset = new(
			Mathf.Sin(_cameraYaw) * Mathf.Cos(_cameraPitch),
			Mathf.Sin(_cameraPitch),
			Mathf.Cos(_cameraYaw) * Mathf.Cos(_cameraPitch)
		);
		_previewCamera.Position = _cameraTarget + offset * _cameraDistance;
		_previewCamera.LookAt(_cameraTarget, Vector3.Up);
	}

	private void ResetPreviewCamera()
	{
		_cameraTarget = new Vector3(0, 2.6f, 0);
		_cameraYaw = 0;
		_cameraPitch = -0.08f;
		_cameraDistance = 8.5f;
		UpdatePreviewCamera();
		_status.Text = "Preview camera reset";
	}

	private void TransformSelectedBone(Vector2 relative)
	{
		if (_previewSkeleton == null)
			return;
		int bone = _boneChoice.Selected;
		if (bone < 0 || bone >= _previewSkeleton.GetBoneCount())
			return;
		_previewPlayer?.Pause();
		_play.Text = "▶";
		Vector3 axis = _activeGizmoAxis switch
		{
			0 => Vector3.Right,
			1 => Vector3.Up,
			_ => Vector3.Back,
		};
		float drag = (relative.X - relative.Y) * 0.01f;
		if (_poseTool == PoseTool.Move)
			_previewSkeleton.SetBonePosePosition(
				bone,
				_previewSkeleton.GetBonePosePosition(bone) + axis * drag * 0.01f
			);
		else
			_previewSkeleton.SetBonePoseRotation(
				bone,
				(
					new Quaternion(axis, drag)
					* _previewSkeleton.GetBonePoseRotation(bone)
				).Normalized()
			);
		UpdateBoneGizmo();
	}

	private void KeySelectedBone(string channel)
	{
		if (_previewSkeleton == null)
			return;
		PushUndo();
		int bone = _boneChoice.Selected;
		if (bone < 0 || bone >= _previewSkeleton.GetBoneCount())
			return;
		string path = "Poly/Skeleton3D:" + _previewSkeleton.GetBoneName(bone);
		int trackIndex = _clip.Tracks.FindIndex(
			track => track.Channel == channel && track.Path == path
		);
		if (trackIndex < 0)
		{
			_clip.Tracks.Add(new BVAnimationTrack { Path = path, Channel = channel });
			trackIndex = _clip.Tracks.Count - 1;
		}
		BVAnimationTrack track = _clip.Tracks[trackIndex];
		double time = Math.Clamp(_playhead.Value, 0, _clip.Length);
		BVAnimationKey? key = track.Keys.FirstOrDefault(
			item => Math.Abs(item.Time - time) < 0.0005
		);
		if (key == null)
		{
			key = new BVAnimationKey { Time = time };
			track.Keys.Add(key);
		}
		if (channel == "position")
		{
			Vector3 pose = _previewSkeleton.GetBonePosePosition(bone);
			key.Value = [pose.X, pose.Y, pose.Z];
		}
		else if (channel == "scale")
		{
			Vector3 pose = _previewSkeleton.GetBonePoseScale(bone);
			key.Value = [pose.X, pose.Y, pose.Z];
		}
		else
		{
			Quaternion pose = _previewSkeleton.GetBonePoseRotation(bone);
			key.Value = [pose.X, pose.Y, pose.Z, pose.W];
		}
		track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
		_selectedTrack = trackIndex;
		_selectedKey = track.Keys.IndexOf(key);
		RefreshAll();
		_status.Text =
			$"Keyed {channel} for {_previewSkeleton.GetBoneName(bone)} at {time:0.###}s";
	}

	private static Button AddButton(Control parent, string text, Action pressed)
	{
		Button button = new() { Text = text };
		button.Pressed += pressed;
		parent.AddChild(button);
		return button;
	}

	private static SpinBox AddSpin(Control parent, string label, double min, double max, double step)
	{
		HBoxContainer row = new();
		parent.AddChild(row);
		row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(48, 0) });
		SpinBox field = new()
		{
			MinValue = min,
			MaxValue = max,
			Step = step,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		row.AddChild(field);
		return field;
	}

	private static void AddLabeledControl(Control parent, string label, Control control)
	{
		HBoxContainer row = new();
		parent.AddChild(row);
		row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) });
		control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(control);
	}

	private void NewClip()
	{
		PushUndo();
		_documentTrack = null;
		_clip = CreateDefaultClip();
		_selectedTrack = -1;
		_selectedKey = -1;
		RefreshAll();
	}

	private static BVAnimationClip CreateDefaultClip() =>
		new()
		{
			Name = "New Animation",
			Length = 1,
			Tracks =
			[
				new BVAnimationTrack
				{
					Path = "Poly/Skeleton3D:LowerTorso",
					Channel = "rotation",
					Keys =
					[
						new BVAnimationKey
						{
							Time = 0,
							Value = [0, 0, 0, 1],
						},
					],
				},
			],
		};

	private void OpenImport()
	{
		FileDialog dialog = new()
		{
			Access = FileDialog.AccessEnum.Filesystem,
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Filters = ["*.bvanim ; BrickVerse Animation", "*.fbx,*.glb,*.gltf ; FBX / GLTF Animation"],
			UseNativeDialog = true,
		};
		dialog.FileSelected += path =>
		{
			LoadAnimationFile(path);
			dialog.QueueFree();
		};
		AddChild(dialog);
		dialog.PopupCenteredRatio(0.75f);
	}

	private void LoadAnimationFile(string path)
	{
		try
		{
			_documentTrack = null;
			_clip = Path.GetExtension(path).Equals(".bvanim", StringComparison.OrdinalIgnoreCase)
				? BVAnimationFormat.Read(File.ReadAllBytes(path))
				: ImportSceneAnimation(path);
			_selectedTrack = -1;
			_selectedKey = -1;
			RefreshAll();
			ShowEditor();
			_status.Text = $"Imported {Path.GetFileName(path)}";
		}
		catch (Exception ex)
		{
			_status.Text = "Import failed";
			CreatorService.Interface.PopupAlert(ex.Message, "Animation Import Failed");
		}
	}

	private static BVAnimationClip ImportSceneAnimation(string path)
	{
		Resource resource = ResourceLoader.Load(path, cacheMode: ResourceLoader.CacheMode.Ignore);
		if (resource is not PackedScene scene)
			throw new InvalidOperationException("Godot could not import this FBX/GLTF animation.");
		Node root = scene.Instantiate();
		try
		{
			AnimationPlayer? player = FindAnimationPlayer(root);
			if (player == null)
				throw new InvalidOperationException("The imported scene has no AnimationPlayer.");
			string? animationName = player
				.GetAnimationList()
				.FirstOrDefault(name => !name.Equals("RESET", StringComparison.OrdinalIgnoreCase));
			if (animationName == null)
				throw new InvalidOperationException("The imported file contains no animation clips.");
			return BVAnimationFormat.FromAnimation(
				Path.GetFileNameWithoutExtension(path),
				player.GetAnimation(animationName)
			);
		}
		finally
		{
			root.Free();
		}
	}

	private static AnimationPlayer? FindAnimationPlayer(Node node)
	{
		if (node is AnimationPlayer player)
			return player;
		foreach (Node child in node.GetChildren())
		{
			AnimationPlayer? found = FindAnimationPlayer(child);
			if (found != null)
				return found;
		}
		return null;
	}

	private void SaveClip()
	{
		FileDialog dialog = new()
		{
			Access = FileDialog.AccessEnum.Filesystem,
			FileMode = FileDialog.FileModeEnum.SaveFile,
			Filters = ["*.bvanim ; BrickVerse Animation"],
			CurrentFile = _clip.Name + ".bvanim",
			UseNativeDialog = true,
		};
		dialog.FileSelected += path =>
		{
			try
			{
				if (!path.EndsWith(".bvanim", StringComparison.OrdinalIgnoreCase))
					path += ".bvanim";
				File.WriteAllBytes(path, BVAnimationFormat.Write(_clip));
				_status.Text = $"Saved {Path.GetFileName(path)}";
			}
			catch (Exception ex)
			{
				CreatorService.Interface.PopupAlert(ex.Message, "Animation Save Failed");
			}
			dialog.QueueFree();
		};
		AddChild(dialog);
		dialog.PopupCenteredRatio(0.75f);
	}

	private void OpenPublish()
	{
		try
		{
			BVAnimationFormat.Validate(_clip);
			CreatorService.Interface.PopupWindow(new AnimationPublishPopup(_clip));
		}
		catch (Exception ex)
		{
			CreatorService.Interface.PopupAlert(ex.Message, "Animation Validation Failed");
		}
	}

	private void RefreshPreview()
	{
		if (_previewPlayer == null)
			return;
		AnimationLibrary library;
		try
		{
			library = BVAnimationFormat.ToLibrary(_clip);
		}
		catch
		{
			return;
		}
		if (_previewPlayer.HasAnimationLibrary("editor"))
			_previewPlayer.RemoveAnimationLibrary("editor");
		_previewPlayer.AddAnimationLibrary("editor", library);
		if (_playhead != null)
			_playhead.MaxValue = _clip.Length;
		_documentTrack?.SetClip(_clip);
	}

	private void TogglePlayback()
	{
		if (_previewPlayer == null)
			return;
		if (_previewPlayer.IsPlaying())
		{
			_previewPlayer.Pause();
			_play.Text = "▶";
		}
		else
		{
			_previewPlayer.Play("editor/" + _clip.Name);
			_previewPlayer.Seek(_playhead.Value, true);
			_play.Text = "Ⅱ";
		}
	}

	private void StopPlayback()
	{
		_previewPlayer?.Stop();
		_playhead.SetValueNoSignal(0);
		_timeline.Playhead = 0;
		_timeline.QueueRedraw();
		_play.Text = "▶";
	}

	private void SeekPreview(double time)
	{
		if (_previewPlayer == null)
			return;
		if (!_previewPlayer.IsPlaying())
			_previewPlayer.Play("editor/" + _clip.Name);
		_previewPlayer.Seek(time, true);
		_previewPlayer.Pause();
		_play.Text = "▶";
		_timeline.Playhead = time;
		_timeline.QueueRedraw();
	}

	private void AddTrack(string channel)
	{
		PushUndo();
		int components = channel == "rotation" ? 4 : 3;
		float[] value = channel == "rotation" ? [0, 0, 0, 1] : channel == "scale" ? [1, 1, 1] : [0, 0, 0];
		_clip.Tracks.Add(
			new BVAnimationTrack
			{
				Path = "Poly/Skeleton3D:Bone",
				Channel = channel,
				Keys = [new BVAnimationKey { Time = 0, Value = value.Take(components).ToArray() }],
			}
		);
		_selectedTrack = _clip.Tracks.Count - 1;
		RefreshAll();
	}

	private void DeleteTrack()
	{
		if (_selectedTrack < 0 || _selectedTrack >= _clip.Tracks.Count)
			return;
		PushUndo();
		_clip.Tracks.RemoveAt(_selectedTrack);
		_selectedTrack = Math.Min(_selectedTrack, _clip.Tracks.Count - 1);
		_selectedKey = -1;
		RefreshAll();
	}

	private void AddKey()
	{
		if (_selectedTrack < 0 || _selectedTrack >= _clip.Tracks.Count)
			return;
		PushUndo();
		BVAnimationTrack track = _clip.Tracks[_selectedTrack];
		float[] value = track.Channel == "rotation" ? [0, 0, 0, 1] : track.Channel == "scale" ? [1, 1, 1] : [0, 0, 0];
		track.Keys.Add(new BVAnimationKey { Time = Math.Min(_clip.Length, track.Keys.Last().Time + 0.1), Value = value });
		track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
		_selectedKey = track.Keys.Count - 1;
		RefreshKeys();
	}

	private void DeleteKey()
	{
		if (_selectedTrack < 0 || _selectedKey < 0)
			return;
		BVAnimationTrack track = _clip.Tracks[_selectedTrack];
		if (track.Keys.Count <= 1)
		{
			CreatorService.Interface.PopupAlert("A track must contain at least one keyframe.");
			return;
		}
		PushUndo();
		track.Keys.RemoveAt(_selectedKey);
		_selectedKey = Math.Min(_selectedKey, track.Keys.Count - 1);
		RefreshKeys();
	}

	private void SelectTrack()
	{
		TreeItem? selected = _tracks.GetSelected();
		if (selected == null)
			return;
		_selectedTrack = (int)selected.GetMetadata(0);
		_selectedKey = -1;
		SelectBoneForTrack(_selectedTrack);
		RefreshKeys();
	}

	private void SelectKey(long index)
	{
		_selectedKey = (int)index;
		SelectBoneForTrack(_selectedTrack);
		LoadKeyFields();
	}

	private void ApplyKeyFields()
	{
		if (_selectedTrack < 0 || _selectedKey < 0 || _selectedTrack >= _clip.Tracks.Count)
			return;
		BVAnimationTrack track = _clip.Tracks[_selectedTrack];
		if (_selectedKey >= track.Keys.Count)
			return;
		PushUndo();
		BVAnimationKey key = track.Keys[_selectedKey];
		key.Time = Math.Clamp(_time.Value, 0, _clip.Length);
		key.Transition = (float)_transition.Value;
		int components = track.Channel == "rotation" ? 4 : 3;
		key.Value = _values.Take(components).Select(field => (float)field.Value).ToArray();
		track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
		RefreshKeys(false);
	}

	private void ApplyTrackInterpolation()
	{
		if (_selectedTrack < 0 || _selectedTrack >= _clip.Tracks.Count)
			return;
		PushUndo();
		_clip.Tracks[_selectedTrack].Interpolation = _interpolation.GetItemText(
			_interpolation.Selected
		);
		RefreshPreview();
	}

	private void LoadKeyFields()
	{
		bool active = _selectedTrack >= 0 && _selectedKey >= 0;
		_time.Editable = active;
		_transition.Editable = active;
		_interpolation.Disabled = _selectedTrack < 0;
		foreach (SpinBox field in _values)
			field.Editable = active;
		if (_selectedTrack >= 0 && _selectedTrack < _clip.Tracks.Count)
		{
			string interpolation = _clip.Tracks[_selectedTrack].Interpolation;
			for (int index = 0; index < _interpolation.ItemCount; index++)
			{
				if (_interpolation.GetItemText(index).Equals(interpolation, StringComparison.OrdinalIgnoreCase))
				{
					_interpolation.Select(index);
					break;
				}
			}
		}
		if (!active)
			return;
		BVAnimationTrack track = _clip.Tracks[_selectedTrack];
		BVAnimationKey key = track.Keys[_selectedKey];
		_time.SetValueNoSignal(key.Time);
		_transition.SetValueNoSignal(key.Transition);
		for (int index = 0; index < _values.Length; index++)
		{
			_values[index].Visible = index < key.Value.Length;
			_values[index].SetValueNoSignal(index < key.Value.Length ? key.Value[index] : 0);
		}
	}

	private void RefreshAll()
	{
		_name.Text = _clip.Name;
		_length.SetValueNoSignal(_clip.Length);
		int loopIndex = Array.IndexOf(new[] { "None", "Linear", "Pingpong" }, _clip.LoopMode);
		_loop.Select(Math.Max(0, loopIndex));
		_tracks.Clear();
		TreeItem root = _tracks.CreateItem();
		for (int index = 0; index < _clip.Tracks.Count; index++)
		{
			BVAnimationTrack track = _clip.Tracks[index];
			TreeItem item = _tracks.CreateItem(root);
			item.SetText(0, track.Path);
			item.SetText(1, track.Channel);
			item.SetMetadata(0, index);
			item.SetEditable(0, true);
			item.SetEditable(1, false);
			if (index == _selectedTrack)
				item.Select(0);
		}
		_timeline.Clip = _clip;
		_timeline.SelectedTrack = _selectedTrack;
		_timeline.SelectedKey = _selectedKey;
		RefreshPreview();
		RefreshKeys();
	}

	private void CommitTrackPath()
	{
		TreeItem? item = _tracks.GetEdited();
		if (item == null)
			return;
		int index = (int)item.GetMetadata(0);
		PushUndo();
		_clip.Tracks[index].Path = item.GetText(0).Trim();
		_timeline.QueueRedraw();
		RefreshPreview();
	}

	private void RefreshKeys(bool loadFields = true)
	{
		_keys.Clear();
		if (_selectedTrack >= 0 && _selectedTrack < _clip.Tracks.Count)
		{
			foreach (BVAnimationKey key in _clip.Tracks[_selectedTrack].Keys)
				_keys.AddItem($"{key.Time:0.###} s   [{string.Join(", ", key.Value.Select(v => v.ToString("0.###")))}]");
			if (_selectedKey >= 0 && _selectedKey < _keys.ItemCount)
				_keys.Select(_selectedKey);
		}
		if (loadFields)
			LoadKeyFields();
		_timeline.SelectedTrack = _selectedTrack;
		_timeline.SelectedKey = _selectedKey;
		_timeline.QueueRedraw();
		RefreshPreview();
	}
}

public sealed partial class AnimationTimeline : Control
{
	public BVAnimationClip? Clip { get; set; }
	public double Playhead { get; set; }
	public int SelectedTrack { get; set; } = -1;
	public int SelectedKey { get; set; } = -1;

	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color("171b22"));
		if (Clip == null || Clip.Length <= 0)
			return;
		const float left = 150;
		float width = Math.Max(1, Size.X - left - 12);
		for (int second = 0; second <= Math.Ceiling(Clip.Length); second++)
		{
			float x = left + width * second / Clip.Length;
			DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), new Color(1, 1, 1, 0.12f));
			DrawString(ThemeDB.FallbackFont, new Vector2(x + 3, 15), second + "s", HorizontalAlignment.Left, -1, 11, new Color(1, 1, 1, 0.65f));
		}
		float rowHeight = Math.Max(22, (Size.Y - 22) / Math.Max(1, Clip.Tracks.Count));
		for (int trackIndex = 0; trackIndex < Clip.Tracks.Count; trackIndex++)
		{
			BVAnimationTrack track = Clip.Tracks[trackIndex];
			float y = 24 + trackIndex * rowHeight;
			DrawString(ThemeDB.FallbackFont, new Vector2(6, y + 14), track.Channel + "  " + track.Path.GetFile(), HorizontalAlignment.Left, 138, 11, Colors.LightGray);
			DrawLine(new Vector2(left, y + rowHeight), new Vector2(Size.X, y + rowHeight), new Color(1, 1, 1, 0.08f));
			for (int keyIndex = 0; keyIndex < track.Keys.Count; keyIndex++)
			{
				BVAnimationKey key = track.Keys[keyIndex];
				float x = left + width * (float)(key.Time / Clip.Length);
				Vector2 center = new(x, y + rowHeight * 0.5f);
				bool selected = trackIndex == SelectedTrack && keyIndex == SelectedKey;
				float radius = selected ? 7 : 5;
				Vector2[] diamond = [center + new Vector2(0, -radius), center + new Vector2(radius, 0), center + new Vector2(0, radius), center + new Vector2(-radius, 0)];
				DrawColoredPolygon(diamond, selected ? new Color("ffd166") : new Color("32a9ff"));
			}
		}
		float playheadX = left + width * (float)(Playhead / Clip.Length);
		DrawLine(new Vector2(playheadX, 0), new Vector2(playheadX, Size.Y), new Color("ff5b62"), 2);
	}
}
