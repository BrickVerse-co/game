using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Datamodel.Services;
using BrickVerse.Scripting;
using Godot;
using System;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class UIAd : UIField
{
	private TextureRect _image = null!;
	private VideoStreamPlayer _video = null!;
	private Timer _refreshTimer = null!;
	private AdShape _adShape;
	private bool _enableVideoAds = true;
	private float _refreshInterval = 60;
	private string _adId = "";
	private string _ctaUri = "";
	private ResourceAsset? _creative;

	[Editable, ScriptProperty] public AdShape AdShape { get => _adShape; set { _adShape = value; if (value == AdShape.Video) EnableVideoAds = true; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(true)] public bool EnableVideoAds { get => _enableVideoAds; set { _enableVideoAds = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(60f)] public float RefreshInterval { get => _refreshInterval; set { _refreshInterval = Mathf.Clamp(value, 60, 300); if (_refreshTimer != null) _refreshTimer.WaitTime = _refreshInterval; OnPropertyChanged(); } }
	[ScriptProperty] public bool IsVideo { get; private set; }
	[ScriptProperty] public BVSignal<string> AdLoaded { get; private set; } = new();
	[ScriptProperty] public BVSignal<string> AdClicked { get; private set; } = new();

	public override Node CreateGDNode() => new Control { MouseFilter = Control.MouseFilterEnum.Stop };
	public override void InitGDNode()
	{
		base.InitGDNode();
		_image = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore };
		_video = new VideoStreamPlayer { Expand = true, Loop = true, Autoplay = true, MouseFilter = Control.MouseFilterEnum.Ignore };
		_refreshTimer = new Timer { WaitTime = _refreshInterval, Autostart = true };
		NodeControl.AddChild(_image); NodeControl.AddChild(_video); NodeControl.AddChild(_refreshTimer);
		_image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	public override void Init() { base.Init(); NodeControl.GuiInput += OnGuiInput; _refreshTimer.Timeout += OnRefreshTimer; _ = RefreshAsync(); }
	public override void PreDelete() { NodeControl.GuiInput -= OnGuiInput; _refreshTimer.Timeout -= OnRefreshTimer; UnlinkCreative(); base.PreDelete(); }
	private void OnRefreshTimer() => _ = RefreshAsync();

	[ScriptMethod]
	public async System.Threading.Tasks.Task RefreshAsync()
	{
		AdPlacement? ad = await Root.FindChild<AdService>("AdService")!.GetAdAsync(_adShape, _adShape == AdShape.Video && _enableVideoAds);
		if (ad == null || IsDeleted) return;
		UnlinkCreative(); _adId = ad.Id; _ctaUri = ad.CtaUri; IsVideo = ad.CreativeType == "VIDEO";
		_creative = IsVideo ? Root.New<BVVideoAsset>() : Root.New<BVImageAsset>();
		if (_creative is BVVideoAsset video) video.VideoID = ad.CreativeId;
		if (_creative is BVImageAsset image) image.ImageID = ad.CreativeId;
		_creative.LinkTo(this); _creative.ResourceLoaded += OnCreativeLoaded; _creative.QueueLoadResource(); OnPropertyChanged(nameof(IsVideo));
	}

	private void OnCreativeLoaded(Resource resource)
	{
		_video.Visible = IsVideo; _image.Visible = !IsVideo;
		if (IsVideo) { _video.Stream = (VideoStream)resource; _video.Play(); } else _image.Texture = (Texture2D)resource;
		AdLoaded.Invoke(_adId);
	}

	private async void OnGuiInput(InputEvent input)
	{
		if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || string.IsNullOrEmpty(_adId)) return;
		AdClicked.Invoke(_adId);
		try { await Root.FindChild<AdService>("AdService")!.RecordClickAsync(_adId); } catch (Exception error) { GD.PushWarning(error.Message); }
		if (!string.IsNullOrWhiteSpace(_ctaUri)) OS.ShellOpen(_ctaUri);
	}

	private void UnlinkCreative()
	{
		if (_creative == null) return;
		_creative.ResourceLoaded -= OnCreativeLoaded; _creative.UnlinkFrom(this); _creative = null;
	}
}
