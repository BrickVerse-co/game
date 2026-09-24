// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>Base class for instances that participate in the routable audio graph.</summary>
[Abstract]
public abstract partial class AudioNode : Instance, IAudioWireNode
{
	internal abstract string[] InputPins { get; }
	internal abstract string[] OutputPins { get; }

	[ScriptProperty] public BVSignal<bool, string, AudioWire, Instance> WiringChanged { get; private set; } = new();

	[ScriptMethod] public string[] GetInputPins() => [.. InputPins];
	[ScriptMethod] public string[] GetOutputPins() => [.. OutputPins];

	[ScriptMethod]
	public AudioWire[] GetConnectedWires(string pin)
	{
		if (Root == null || string.IsNullOrWhiteSpace(pin)) return [];
		return Root.GetDescendants().OfType<AudioWire>()
			.Where(wire => wire.Connected &&
				((wire.SourceInstance == this && string.Equals(wire.SourceName, pin, StringComparison.OrdinalIgnoreCase)) ||
				 (wire.TargetInstance == this && string.Equals(wire.TargetName, pin, StringComparison.OrdinalIgnoreCase))))
			.ToArray();
	}

	internal bool HasInputPin(string pin) => InputPins.Contains(pin, StringComparer.OrdinalIgnoreCase);
	internal bool HasOutputPin(string pin) => OutputPins.Contains(pin, StringComparer.OrdinalIgnoreCase);
	internal void EmitWiringChanged(bool connected, string pin, AudioWire wire, Instance other) => WiringChanged.Invoke(connected, pin, wire, other);
	string[] IAudioWireNode.InputPins => InputPins;
	string[] IAudioWireNode.OutputPins => OutputPins;
	bool IAudioWireNode.HasInputPin(string pin) => HasInputPin(pin);
	bool IAudioWireNode.HasOutputPin(string pin) => HasOutputPin(pin);
	void IAudioWireNode.EmitWiringChanged(bool connected, string pin, AudioWire wire, Instance other) => EmitWiringChanged(connected, pin, wire, other);

	public override void PreDelete()
	{
		if (Root != null)
		{
			foreach (AudioWire wire in Root.GetDescendants().OfType<AudioWire>()
				.Where(wire => wire.SourceInstance == this || wire.TargetInstance == this).ToArray())
			{
				if (wire.SourceInstance == this) wire.SourceInstance = null;
				if (wire.TargetInstance == this) wire.TargetInstance = null;
			}
		}
		WiringChanged.DisconnectAll();
		base.PreDelete();
	}
}

internal interface IAudioWireNode
{
	string[] InputPins { get; }
	string[] OutputPins { get; }
	bool HasInputPin(string pin);
	bool HasOutputPin(string pin);
	void EmitWiringChanged(bool connected, string pin, AudioWire wire, Instance other);
}
