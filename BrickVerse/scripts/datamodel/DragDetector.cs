using BrickVerse.Attributes;
using BrickVerse.Scripting;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class DragDetector : Grabbable
{
	private Player? _activeDragger;
	[ScriptProperty] public BVSignal<Player> DragStart { get; private set; } = new();
	[ScriptProperty] public BVSignal<Player> DragContinue { get; private set; } = new();
	[ScriptProperty] public BVSignal<Player> DragEnd { get; private set; } = new();
	public override void Init() { Grabbed.Connect(OnGrabbed); Released.Connect(OnReleased); base.Init(); }
	public override void PhysicsProcess(double delta) { if (Dragger != null) DragContinue.Invoke(Dragger); base.PhysicsProcess(delta); }
	private void OnGrabbed(Player player) { _activeDragger = player; DragStart.Invoke(player); }
	private void OnReleased() { if (_activeDragger != null) DragEnd.Invoke(_activeDragger); _activeDragger = null; }
}
