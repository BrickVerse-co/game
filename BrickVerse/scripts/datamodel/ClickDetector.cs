using BrickVerse.Attributes;
using BrickVerse.Scripting;
namespace BrickVerse.Datamodel;
[Instantiable]
public sealed partial class ClickDetector : Instance
{
	private Physical? _target; private float _maxActivationDistance = 32;
	[Editable, ScriptProperty, DefaultValue(32f)] public float MaxActivationDistance { get => _maxActivationDistance; set { _maxActivationDistance = System.Math.Max(0, value); OnPropertyChanged(); } }
	[ScriptProperty] public BVSignal<Player> MouseClick { get; private set; } = new();
	[ScriptProperty] public BVSignal MouseHoverEnter { get; private set; } = new();
	[ScriptProperty] public BVSignal MouseHoverLeave { get; private set; } = new();
	public override void EnterTree() { if (Parent is Physical target) { _target = target; target.Clicked.Connect(OnClicked); target.MouseEnter.Connect(OnEnter); target.MouseExit.Connect(OnLeave); } base.EnterTree(); }
	public override void ExitTree() { Disconnect(); base.ExitTree(); }
	public override void PreDelete() { Disconnect(); base.PreDelete(); }
	private void OnClicked(Player player) { if (_target != null && player.Position.DistanceTo(_target.Position) <= _maxActivationDistance) MouseClick.Invoke(player); }
	private void OnEnter() => MouseHoverEnter.Invoke(); private void OnLeave() => MouseHoverLeave.Invoke();
	private void Disconnect() { if (_target == null) return; _target.Clicked.Disconnect(OnClicked); _target.MouseEnter.Disconnect(OnEnter); _target.MouseExit.Disconnect(OnLeave); _target = null; }
}
