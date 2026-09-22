using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrickVerse.Attributes;
using BrickVerse.Datamodel;
using BrickVerse.Formats;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class AnimationEditorWindow
{
	private Node3D? _objectPreview;
	private Node3D? _sampleRig;
	private OptionButton _objectChoice = null!;
	private OptionButton _propertyChoice = null!;
	private readonly List<Node> _objectNodes = [];
	private readonly List<string> _objectProperties = [];
	private SpinBox _fps = null!;
	private CheckButton _snap = null!;
	private Instance? _instanceTarget;
	private Node3D? SelectedObject => _objectPreview != null && _objectChoice.Selected >= 0 ? _objectNodes[_objectChoice.Selected] as Node3D : null;

	private double SnapTime(double time) => Math.Clamp(_snap?.ButtonPressed == true ? Math.Round(time * _fps.Value) / _fps.Value : time, 0, _clip.Length);

	private void BuildObjectControls()
	{
		VBoxContainer controls = GetNode<VBoxContainer>("Editor/Main/Tracks");
		AddButton(controls, "Use selected 3D instance", UseSelectedObject);
		AddButton(controls, "Use selected Instance properties", AddSelectedInstanceProperty);
		_objectChoice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		controls.AddChild(_objectChoice);
		_objectChoice.ItemSelected += _ => { PopulateProperties(); SyncPoseAdapterFromBone(); };
		_propertyChoice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		controls.AddChild(_propertyChoice);
		AddButton(controls, "+ Property track", () => {
			if (_propertyChoice.Selected >= 0) AddObjectProperty(_objectProperties[_propertyChoice.Selected]);
		});
		VBoxContainer editor = (VBoxContainer)_editorRoot;
		HBoxContainer transport = new();
		editor.AddChild(transport);
		editor.MoveChild(transport, 2);
		AddButton(transport, "Start", () => SeekAt(0));
		AddButton(transport, "< Frame", () => SeekAt(_playhead.Value - 1 / _fps.Value));
		AddButton(transport, "Frame >", () => SeekAt(_playhead.Value + 1 / _fps.Value));
		_fps = AddSpin(transport, "FPS", 1, 120, 1); _fps.Value = 30;
		_snap = new CheckButton { Text = "Snap", ButtonPressed = true }; transport.AddChild(_snap);
		SpinBox zoom = AddSpin(transport, "Zoom", 1, 8, 0.25); zoom.Value = 1;
		zoom.ValueChanged += value => _timeline.CustomMinimumSize = new Vector2((float)(700 * value), _timeline.CustomMinimumSize.Y);
		AddButton(transport, "Edit key", OpenKeyframeProperties);
		Control host = GetNode<Control>("Editor/Main/Workspace/TimelineHost");
		host.Reparent(editor); editor.MoveChild(host, 3);
		host.CustomMinimumSize = new Vector2(0, 200);
		host.SizeFlagsVertical = Control.SizeFlags.Fill;
		if (_initialTarget != null) UseSelectedObject();
	}

	private void AddSelectedInstanceProperty()
	{
		Instance? target = Datamodel.World.Current?.CreatorContext.Selections.SelectedInstances.FirstOrDefault();
		if (target == null) { _status.Text = "Select a Part, GUI, UIField, pawn, or other Instance first."; return; }
		PropertyInfo? property = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.FirstOrDefault(p => p.IsDefined(typeof(EditableAttribute)) && p.CanRead && p.CanWrite && BVAnimationFormat.ValueTypeName(p.PropertyType) != null && p.Name != "Name");
		if (property == null) { _status.Text = "No animatable properties found on this Instance."; return; }
		// PlayOn(target) binds the clip to the selected instance, so the selected
		// object is always the relative root. Child tracks can be added later by
		// selecting a descendant and using the same relative-root rule.
		string path = ".:" + property.Name;
		string type = BVAnimationFormat.ValueTypeName(property.PropertyType)!;
		if (_clip.Tracks.Any(t => t.Path == path)) return;
		PushUndo();
		_clip.Tracks.Add(new BVAnimationTrack { Path = path, Channel = "instance", ValueType = type, Keys = [new BVAnimationKey { Value = BVAnimationFormat.EncodeValue(InstanceAnimationBinding.Read(target, property)), Text = property.GetValue(target)?.ToString() ?? "" }] });
		_selectedTrack = _clip.Tracks.Count - 1;
		RefreshAll();
		_status.Text = $"Added {property.Name} track for {target.Name}. Use K at the playhead to add inherited keys.";
	}

	private void SeekAt(double time)
	{
		time = SnapTime(time); _playhead.SetValueNoSignal(time); SeekPreview(time);
		SyncPoseAdapterFromBone();
	}

	private void UseSelectedObject()
	{
		var target = Datamodel.World.Current?.CreatorContext.Selections.SelectedInstances.OfType<Datamodel.Dynamic>().FirstOrDefault() ?? _initialTarget;
		if (target == null || target.IsDeleted) { _status.Text = "Select a mesh, pawn, model, or other 3D instance in Explorer first."; return; }
		_initialTarget = target;
		_instanceTarget = target;
		StopPlayback();
		if (_objectPreview != null) { _previewViewport!.RemoveChild(_objectPreview); _objectPreview.QueueFree(); }
		_sampleRig!.Visible = false;
		_previewSkeleton = null;
		_boneChoice.Clear();
		_objectPreview = (Node3D)target.GDNode3D.Duplicate(0);
		_objectPreview.ProcessMode = Node.ProcessModeEnum.Disabled;
		_previewViewport!.AddChild(_objectPreview);
		_previewPlayer = new AnimationPlayer { RootNode = new NodePath(".."), ProcessMode = Node.ProcessModeEnum.Always };
		_objectPreview.AddChild(_previewPlayer);
		_objectNodes.Clear(); _objectChoice.Clear();
		void Visit(Node node)
		{
			if (node is Node3D) { _objectNodes.Add(node); _objectChoice.AddItem(node == _objectPreview ? target.Name : _objectPreview.GetPathTo(node).ToString()); }
			foreach (Node child in node.GetChildren()) Visit(child);
		}
		Visit(_objectPreview);
		_objectChoice.Select(0); PopulateProperties(); SyncPoseAdapterFromBone();
		_cameraTarget = _objectPreview.GlobalPosition; UpdatePreviewCamera();
		_undoHistory.Clear(); _redoHistory.Clear();
		_documentTrack = null;
		_clip = new BVAnimationClip { Name = target.Name + " Animation", Length = 3 };
		_selectedTrack = -1; _selectedKey = -1;
		RefreshAll();
		_status.Text = "Choose an object and property, add a track, then scrub and press K. Double-click a key to edit its values.";
	}

	private void PopulateProperties()
	{
		_propertyChoice.Clear(); _objectProperties.Clear();
		if (_objectChoice.Selected < 0) return;
		Node node = _objectNodes[_objectChoice.Selected];
		foreach (var property in node.GetPropertyList())
		{
			string name = property["name"].AsString();
			if ((property["usage"].AsInt32() & (int)PropertyUsageFlags.Editor) == 0 || !BVAnimationFormat.IsSupportedValue(node.Get(name))) continue;
			_objectProperties.Add(name); _propertyChoice.AddItem(name.Capitalize());
		}
	}

	private void AddObjectProperty(string property)
	{
		if (_objectPreview == null || _objectChoice.Selected < 0) return;
		Node node = _objectNodes[_objectChoice.Selected];
		string path = _objectPreview.GetPathTo(node) + ":" + property;
		int existing = _clip.Tracks.FindIndex(t => t.Path == path && t.Channel == "property");
		if (existing >= 0) { _selectedTrack = existing; RefreshAll(); return; }
		Variant value = node.Get(property);
		if (!BVAnimationFormat.IsSupportedValue(value)) return;
		PushUndo();
		_clip.Version = BVAnimationFormat.CurrentVersion;
		_clip.Tracks.Add(new BVAnimationTrack { Path = path, Channel = "property", ValueType = value.VariantType.ToString(), Keys = [new BVAnimationKey { Value = BVAnimationFormat.EncodeValue(value) }] });
		_selectedTrack = _clip.Tracks.Count - 1; _selectedKey = 0;
		RefreshAll();
	}

	private float[] ReadTrackValue(BVAnimationTrack track)
	{
		if (track.Channel == "instance" && _instanceTarget != null)
		{
			int split = track.Path.LastIndexOf(':');
			PropertyInfo? property = _instanceTarget.GetType().GetProperty(track.Path[(split + 1)..], BindingFlags.Instance | BindingFlags.Public);
			if (property != null) return BVAnimationFormat.EncodeValue(InstanceAnimationBinding.Read(_instanceTarget, property));
		}
		if (track.Channel != "property") return ReadSelectedBoneValue(track.Channel);
		int separator = track.Path.IndexOf(':');
		Node? node = _objectPreview?.GetNodeOrNull(new NodePath(track.Path[..separator]));
		return node != null ? BVAnimationFormat.EncodeValue(node.GetIndexed(new NodePath(track.Path[(separator + 1)..]))) : (float[])track.Keys[0].Value.Clone();
	}
}
