using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class AudioListener : Dynamic
{
	private AudioListener3D _listener = null!;
	private bool _enabled = true;
	private string _interactionGroup = "Default";
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get => _enabled; set { _enabled = value; Apply(); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public string AudioInteractionGroup { get => _interactionGroup; set { _interactionGroup = value?.Trim() ?? ""; OnPropertyChanged(); } }
	[ScriptProperty, SaveIgnore, CloneIgnore] public bool IsCurrent => _listener?.IsCurrent() == true;
	public override void Init() { GDNode3D.AddChild(_listener = new AudioListener3D(), @internal: Node.InternalMode.Back); Apply(); base.Init(); }
	[ScriptMethod] public void MakeCurrent() { _enabled = true; _listener.MakeCurrent(); OnPropertyChanged(nameof(Enabled)); }
	private void Apply() { if (_listener == null) return; if (_enabled) _listener.MakeCurrent(); else _listener.ClearCurrent(); }
}
