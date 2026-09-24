// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Datamodel;

internal static class AudioGraph
{
	internal readonly record struct Route(AudioEmitter? Emitter, string Bus);

	internal static void Rebuild(World root)
	{
		if (root == null) return;
		foreach (AudioProcessor processor in root.GetDescendants().OfType<AudioProcessor>()) processor.ApplyRouting();
		foreach (IAudioSource source in root.GetDescendants().OfType<IAudioSource>()) source.RebuildAudioRoutes();
	}

	internal static bool WouldCycle(AudioWire candidate)
	{
		if (candidate.SourceInstance == null || candidate.TargetInstance == null) return false;
		return CanReach(candidate.TargetInstance, candidate.SourceInstance, candidate, []);
	}

	internal static Route[] ResolveRoutes(Instance source)
	{
		List<Route> routes = [];
		Walk(source, null, null, [], routes);
		return routes.Distinct().ToArray();
	}

	internal static string ResolveDownstreamBus(Instance source)
	{
		List<Route> routes = [];
		foreach (AudioWire wire in Outgoing(source))
			Walk(wire.TargetInstance!, null, null, [], routes);
		return routes.Count > 0 ? routes[0].Bus : "Master";
	}

	private static void Walk(Instance current, AudioEmitter? emitter, string? firstBus, HashSet<Instance> visited, List<Route> routes)
	{
		if (!visited.Add(current)) return;
		if (current is AudioProcessor processor) firstBus ??= processor.BusName;
		if (current is AudioEmitter spatial) emitter = spatial;
		if (current is AudioDeviceOutput output)
		{
			routes.Add(new(emitter, firstBus ?? output.BusName));
			return;
		}

		AudioWire[] outgoing = Outgoing(current);
		if (outgoing.Length == 0)
		{
			if (emitter != null) routes.Add(new(emitter, firstBus ?? "Master"));
			return;
		}
		foreach (AudioWire wire in outgoing) Walk(wire.TargetInstance!, emitter, firstBus, new HashSet<Instance>(visited), routes);
	}

	private static AudioWire[] Outgoing(Instance node) => node.Root.GetDescendants().OfType<AudioWire>()
		.Where(wire => wire.Connected && wire.SourceInstance == node).ToArray();

	private static bool CanReach(Instance current, Instance wanted, AudioWire ignored, HashSet<Instance> visited)
	{
		if (current == wanted) return true;
		if (!visited.Add(current) || current.Root == null) return false;
		return current.Root.GetDescendants().OfType<AudioWire>()
			.Where(wire => wire != ignored && IsStructurallyValid(wire) && wire.SourceInstance == current && wire.TargetInstance != null)
			.Any(wire => CanReach(wire.TargetInstance!, wanted, ignored, visited));
	}

	private static bool IsStructurallyValid(AudioWire wire) =>
		wire.SourceInstance is IAudioWireNode source && wire.TargetInstance is IAudioWireNode target &&
		wire.SourceInstance != wire.TargetInstance && source.HasOutputPin(wire.SourceName) && target.HasInputPin(wire.TargetName);
}

internal interface IAudioSource
{
	void RebuildAudioRoutes();
}
