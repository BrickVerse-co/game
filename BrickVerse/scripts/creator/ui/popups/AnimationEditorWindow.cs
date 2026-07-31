// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Creator.Utils;
using BrickVerse.Formats;
using BrickVerse.Shared;
using BrickVerse.Datamodel.Creator;
using Godot;
using System;
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
	private PoseTool _poseTool = PoseTool.Rotate;
	private int _activeGizmoAxis = -1;
	private int _selectedTrack = -1;
	private int _selectedKey = -1;

	public AnimationEditorWindow()
	{
		Title = "Animation Editor";
		Size = new Vector2I(1100, 680);
		MinSize = new Vector2I(760, 480);
	}

	public override void _Ready()
	{
		base._Ready();
		BuildInterface();
		RefreshAll();
	}

	private void BuildInterface()
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
		SubViewportContainer container = new() { Stretch = true };
		container.GuiInput += HandlePreviewInput;
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
		RefreshPreview();
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
		UpdateBoneGizmo();
	}

	private void UpdateBoneGizmo()
	{
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
		_status.Text = tool == PoseTool.Move
			? "Move tool: drag a colored gizmo axis"
			: "Rotate tool: drag a colored gizmo axis";
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
				if (button.Pressed)
					_posingBone =
						_poseTool != PoseTool.None
						&& _boneChoice.Selected >= 0
						&& TryPickGizmoAxis(button.Position, out _activeGizmoAxis);
				else
				{
					if (_posingBone && _autoKey.ButtonPressed)
						KeySelectedBone(
							_poseTool == PoseTool.Move ? "position" : "rotation"
						);
					_posingBone = false;
					_activeGizmoAxis = -1;
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
			try
			{
				_clip =
					Path.GetExtension(path).Equals(".bvanim", StringComparison.OrdinalIgnoreCase)
						? BVAnimationFormat.Read(File.ReadAllBytes(path))
						: ImportSceneAnimation(path);
				_selectedTrack = -1;
				_selectedKey = -1;
				RefreshAll();
				_status.Text = $"Imported {Path.GetFileName(path)}";
			}
			catch (Exception ex)
			{
				_status.Text = "Import failed";
				CreatorService.Interface.PopupAlert(ex.Message, "Animation Import Failed");
			}
			dialog.QueueFree();
		};
		AddChild(dialog);
		dialog.PopupCenteredRatio(0.75f);
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
		_clip.Tracks.RemoveAt(_selectedTrack);
		_selectedTrack = Math.Min(_selectedTrack, _clip.Tracks.Count - 1);
		_selectedKey = -1;
		RefreshAll();
	}

	private void AddKey()
	{
		if (_selectedTrack < 0 || _selectedTrack >= _clip.Tracks.Count)
			return;
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
		RefreshKeys();
	}

	private void SelectKey(long index)
	{
		_selectedKey = (int)index;
		LoadKeyFields();
	}

	private void ApplyKeyFields()
	{
		if (_selectedTrack < 0 || _selectedKey < 0 || _selectedTrack >= _clip.Tracks.Count)
			return;
		BVAnimationTrack track = _clip.Tracks[_selectedTrack];
		if (_selectedKey >= track.Keys.Count)
			return;
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
		RefreshPreview();
		RefreshKeys();
	}

	private void CommitTrackPath()
	{
		TreeItem? item = _tracks.GetEdited();
		if (item == null)
			return;
		int index = (int)item.GetMetadata(0);
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
		_timeline.QueueRedraw();
		RefreshPreview();
	}
}

public sealed partial class AnimationTimeline : Control
{
	public BVAnimationClip? Clip { get; set; }
	public double Playhead { get; set; }

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
			foreach (BVAnimationKey key in track.Keys)
			{
				float x = left + width * (float)(key.Time / Clip.Length);
				Vector2 center = new(x, y + rowHeight * 0.5f);
				Vector2[] diamond = [center + new Vector2(0, -5), center + new Vector2(5, 0), center + new Vector2(0, 5), center + new Vector2(-5, 0)];
				DrawColoredPolygon(diamond, new Color("32a9ff"));
			}
		}
		float playheadX = left + width * (float)(Playhead / Clip.Length);
		DrawLine(new Vector2(playheadX, 0), new Vector2(playheadX, Size.Y), new Color("ff5b62"), 2);
	}
}
