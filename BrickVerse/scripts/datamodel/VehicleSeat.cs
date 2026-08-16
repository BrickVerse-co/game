using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Scripting;
using Godot;
namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class VehicleSeat : Seat
{
	private float _throttle, _steer, _maxSpeed = 50, _torque = 1000; private Vector2 _lastSent = new(float.NaN, float.NaN);
	[ScriptProperty, SyncVar] public float Throttle { get => _throttle; private set { _throttle = Mathf.Clamp(value, -1, 1); OnPropertyChanged(); } }
	[ScriptProperty, SyncVar] public float Steer { get => _steer; private set { _steer = Mathf.Clamp(value, -1, 1); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(50f)] public float MaxSpeed { get => _maxSpeed; set { _maxSpeed = Mathf.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1000f)] public float Torque { get => _torque; set { _torque = Mathf.Max(0, value); OnPropertyChanged(); } }
	[ScriptProperty] public BVSignal<float, float> InputChanged { get; private set; } = new();
	public override void Init() { SetProcess(true); base.Init(); }
	public override void Process(double delta)
	{
		if (!Root.Network.IsServer && Occupant == Root.Players.LocalPlayer)
		{
			Vector2 input = new(Input.GetActionStrength("rightward") - Input.GetActionStrength("leftward"), Input.GetActionStrength("forward") - Input.GetActionStrength("backward"));
			if (input != _lastSent) { _lastSent = input; RpcId(1, nameof(NetSetVehicleInput), input.Y, input.X); }
		}
		base.Process(delta);
	}
	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetSetVehicleInput(float throttle, float steer)
	{
		if (!Root.Network.IsServer || Occupant is not Player player || player.PeerID != RemoteSenderId) return;
		Throttle = throttle; Steer = steer; InputChanged.Invoke(Throttle, Steer);
	}
	internal override void InvokeVacated(NPC npc) { base.InvokeVacated(npc); Throttle = 0; Steer = 0; InputChanged.Invoke(0, 0); }
}
