// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

[Abstract]
public abstract partial class AudioProcessor : AudioNode
{
	internal override string[] InputPins => ["Input"];
	internal override string[] OutputPins => ["Output"];
	internal string BusName => $"BVAudio_{NetworkedObjectID}";
	protected abstract AudioEffect CreateEffect();
	protected abstract void ConfigureEffect(AudioEffect effect);

	public override void Ready()
	{
		EnsureBus();
		ApplyEffect();
		ApplyRouting();
		base.Ready();
	}

	internal void ApplyRouting()
	{
		EnsureBus();
		int index = AudioServer.GetBusIndex(BusName);
		if (index >= 0) AudioServer.SetBusSend(index, AudioGraph.ResolveDownstreamBus(this));
	}

	protected void ApplyEffect()
	{
		EnsureBus();
		int index = AudioServer.GetBusIndex(BusName);
		if (index < 0) return;
		while (AudioServer.GetBusEffectCount(index) > 0) AudioServer.RemoveBusEffect(index, 0);
		AudioEffect effect = CreateEffect();
		ConfigureEffect(effect);
		AudioServer.AddBusEffect(index, effect);
	}

	private void EnsureBus()
	{
		if (AudioServer.GetBusIndex(BusName) >= 0) return;
		AudioServer.AddBus();
		AudioServer.SetBusName(AudioServer.BusCount - 1, BusName);
	}

	public override void PreDelete()
	{
		int index = AudioServer.GetBusIndex(BusName);
		if (index >= 0) AudioServer.RemoveBus(index);
		base.PreDelete();
	}
}
