using BrickVerse.Datamodel;
using System;
using System.Threading;

namespace BrickVerse.Scripting;

internal static class ParallelLuauContext
{
	private static readonly AsyncLocal<Actor?> CurrentActorSlot = new();
	private static readonly AsyncLocal<bool> ParallelSlot = new();

	public static Actor? CurrentActor => CurrentActorSlot.Value;
	public static bool IsParallel => ParallelSlot.Value;

	public static IDisposable Enter(Actor actor, bool parallel)
	{
		Actor? previousActor = CurrentActorSlot.Value;
		bool previousParallel = ParallelSlot.Value;
		CurrentActorSlot.Value = actor;
		ParallelSlot.Value = parallel;
		return new Scope(previousActor, previousParallel);
	}

	private sealed class Scope(Actor? actor, bool parallel) : IDisposable
	{
		public void Dispose()
		{
			CurrentActorSlot.Value = actor;
			ParallelSlot.Value = parallel;
		}
	}
}
