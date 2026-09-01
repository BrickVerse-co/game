// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;
using System;

namespace BrickVerse.Datamodel;

/// <summary>A placeable camera that renders the live 3D world into a texture.</summary>
[Instantiable]
public sealed partial class SceneCapture : Dynamic
{
	private SubViewport _viewport = null!;
	private Camera3D _camera = null!;
	private Vector2I _resolution = new(512, 512);
	private float _fieldOfView = 70;
	private float _nearClip = 0.05f;
	private float _farClip = 1000;
	private bool _orthographic;
	private float _orthographicSize = 10;
	private bool _enabled = true;
	private CaptureUpdateMode _updateMode = CaptureUpdateMode.Continuous;
	private uint _cullMask = uint.MaxValue;
	private bool _transparentBackground;

	[Editable, ScriptProperty]
	public Vector2I Resolution
	{
		get => _resolution;
		set
		{
			Vector2I next = new(Math.Clamp(value.X, 64, 2048), Math.Clamp(value.Y, 64, 2048));
			if (_resolution == next) return;
			_resolution = next;
			if (_viewport != null) _viewport.Size = next;
			OnPropertyChanged();
			TextureChanged.Invoke();
		}
	}

	[Editable, ScriptProperty, DefaultValue(70f)]
	public float FieldOfView { get => _fieldOfView; set { _fieldOfView = Mathf.Clamp(value, 1, 179); ApplyCamera(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(0.05f)]
	public float NearClip { get => _nearClip; set { _nearClip = Mathf.Clamp(value, 0.001f, _farClip - 0.001f); ApplyCamera(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(1000f)]
	public float FarClip { get => _farClip; set { _farClip = Mathf.Max(value, _nearClip + 0.001f); ApplyCamera(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool Orthographic { get => _orthographic; set { _orthographic = value; ApplyCamera(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(10f)]
	public float OrthographicSize { get => _orthographicSize; set { _orthographicSize = Mathf.Max(0.01f, value); ApplyCamera(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Enabled { get => _enabled; set { _enabled = value; ApplyUpdateMode(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(CaptureUpdateMode.Continuous)]
	public CaptureUpdateMode UpdateMode { get => _updateMode; set { _updateMode = value; ApplyUpdateMode(); OnPropertyChanged(); } }

	[Editable(CustomPropertyControl = "Bitmap32"), ScriptProperty]
	public uint CullMask { get => _cullMask; set { _cullMask = value; if (_camera != null) _camera.CullMask = value; OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool TransparentBackground { get => _transparentBackground; set { _transparentBackground = value; if (_viewport != null) _viewport.TransparentBg = value; OnPropertyChanged(); } }

	[ScriptProperty] public BVSignal TextureChanged { get; private set; } = new();
	[ScriptProperty] public BVSignal Captured { get; private set; } = new();

	public override void Init()
	{
		base.Init();
		_viewport = new SubViewport
		{
			Size = _resolution,
			World3D = Root.World3D,
			HandleInputLocally = false,
			TransparentBg = _transparentBackground,
			RenderTargetClearMode = SubViewport.ClearMode.Always,
			Msaa3D = Viewport.Msaa.Msaa4X,
		};
		_camera = new Camera3D { Current = true };
		_viewport.AddChild(_camera);
		GDNode.AddChild(_viewport, false, Node.InternalMode.Back);
		SetProcess(true);
		ApplyCamera();
		ApplyUpdateMode();
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (_camera != null) _camera.GlobalTransform = GDNode3D.GlobalTransform;
	}

	[ScriptMethod]
	public Texture2D GetTexture() => _viewport.GetTexture();

	[ScriptMethod]
	public void Capture()
	{
		if (!_enabled) return;
		_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
		Callable.From(() => Captured.Invoke()).CallDeferred();
	}

	private void ApplyCamera()
	{
		if (_camera == null) return;
		_camera.Projection = _orthographic ? Camera3D.ProjectionType.Orthogonal : Camera3D.ProjectionType.Perspective;
		_camera.Fov = _fieldOfView;
		_camera.Size = _orthographicSize;
		_camera.Near = _nearClip;
		_camera.Far = _farClip;
		_camera.CullMask = _cullMask;
	}

	private void ApplyUpdateMode()
	{
		if (_viewport == null) return;
		_viewport.RenderTargetUpdateMode = !_enabled
			? SubViewport.UpdateMode.Disabled
			: _updateMode == CaptureUpdateMode.Continuous ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
	}

	[ScriptEnum]
	public enum CaptureUpdateMode { Continuous, OnDemand }
}
