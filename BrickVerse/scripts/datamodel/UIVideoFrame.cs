using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Scripting;
using Godot;
namespace BrickVerse.Datamodel;
[Instantiable]
public sealed partial class UIVideoFrame : UIField
{
	private VideoStreamPlayer _player = null!; private VideoAsset? _video; private bool _autoplay, _loop; private float _volume = 1; private Color _color = Colors.White;
	[Editable, ScriptProperty] public VideoAsset? Video { get => _video; set { if (_video != null) { _video.ResourceLoaded -= OnLoaded; _video.UnlinkFrom(this); } _video = value; _player.Stream = null; if (_video != null) { _video.LinkTo(this); _video.ResourceLoaded += OnLoaded; if (_video.IsResourceLoaded && _video.Resource != null) OnLoaded(_video.Resource); else _video.QueueLoadResource(); } OnPropertyChanged(); } }
	[Editable, ScriptProperty] public bool Autoplay { get => _autoplay; set { _autoplay = value; if (value && _player.Stream != null) _player.Play(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public bool Loop { get => _loop; set { _loop = value; _player.Loop = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1f)] public float Volume { get => _volume; set { _volume = Mathf.Clamp(value, 0, 2); _player.Volume = _volume; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public Color Color { get => _color; set { _color = value; _player.SelfModulate = value; OnPropertyChanged(); } }
	[ScriptProperty] public bool Playing => _player.IsPlaying();
	[ScriptProperty] public double TimePosition => _player.StreamPosition;
	[ScriptProperty] public double Length => _player.GetStreamLength();
	[ScriptProperty] public BVSignal Ended { get; private set; } = new();
	public override Node CreateGDNode() => new VideoStreamPlayer { Expand = true, MouseFilter = Control.MouseFilterEnum.Ignore };
	public override void InitGDNode() { _player = (VideoStreamPlayer)GDNode; base.InitGDNode(); }
	public override void Init() { _player.Finished += OnFinished; base.Init(); IgnoreMouse = true; }
	public override void PreDelete() { _player.Finished -= OnFinished; if (_video != null) { _video.ResourceLoaded -= OnLoaded; _video.UnlinkFrom(this); } base.PreDelete(); }
	[ScriptMethod] public void Play() => _player.Play();
	[ScriptMethod] public void Pause() => _player.Paused = true;
	[ScriptMethod] public void Resume() => _player.Paused = false;
	[ScriptMethod] public void Stop() => _player.Stop();
	[ScriptMethod] public void Seek(double seconds) => _player.StreamPosition = Mathf.Clamp(seconds, 0, Length);
	private void OnLoaded(Resource resource) { _player.Stream = (VideoStream)resource; _player.Loop = _loop; _player.Volume = _volume; if (_autoplay) _player.Play(); }
	private void OnFinished() => Ended.Invoke();
}
