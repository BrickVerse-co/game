using BrickVerse.Attributes;
using BrickVerse.Shared;
using Godot;
using System;
using System.Collections.Generic;

namespace BrickVerse.Datamodel.Services;

/// <summary>Internal server trust checks. Intentionally unavailable to Luau and Explorer.</summary>
[ExplorerExclude, SaveIgnore]
internal sealed partial class AntiCheatService : Instance
{
	private sealed class PlayerState
	{
		public Vector3 Position;
		public Vector3 Velocity;
		public float Score;
		public float AirborneSeconds;
		public float HoverSeconds;
		public ulong LastSampleMsec;
		public ulong GraceUntilMsec;
		public int ConsecutiveSpeedSamples;
		public bool WasInGrace = true;
		public bool EnforcementRequested;
	}

	private readonly Dictionary<Player, PlayerState> _states = [];
	private const float SampleInterval = 0.2f;
	private const float TeleportDistance = 120f;
	private const float EnforcementScore = 12f;
	private const ulong MovementWarmupMsec = 3000;
	private const float MaximumWorldCoordinate = 10_000_000f;
	private const long ManagedMemoryWarningBytes = 6L * 1024 * 1024 * 1024;
	private const long WorkingSetWarningBytes = 10L * 1024 * 1024 * 1024;
	private const long SuspiciousGrowthBytes = 512L * 1024 * 1024;
	private double _sampleElapsed;
	private double _memoryElapsed;
	private long _lastManagedMemory;
	internal bool Enabled { get; private set; } = true;

	public override void Init()
	{
		SetProcess(true);
		base.Init();
	}

	public override void Ready()
	{
		base.Ready();
		if (Root?.Network != null) Root.Network.SuspiciousPeerActivity += OnSuspiciousPeerActivity;
		if (Root != null) { Root.WorldInfoReady += OnWorldInfoReady; if (Root.WorldInfo.HasValue) Enabled = Root.WorldInfo.Value.AntiCheatEnabled; }
	}

	public override void ExitTree()
	{
		if (Root?.Network != null) Root.Network.SuspiciousPeerActivity -= OnSuspiciousPeerActivity;
		if (Root != null) Root.WorldInfoReady -= OnWorldInfoReady;
		base.ExitTree();
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (!Enabled || Root?.Network == null || !Root.Network.IsServer) return;
		_memoryElapsed += delta;
		if (_memoryElapsed >= 30) CheckServerMemory();
		_sampleElapsed += delta;
		if (_sampleElapsed < SampleInterval) return;
		float interval = (float)Math.Min(_sampleElapsed, 1.0); _sampleElapsed = 0;
		HashSet<Player> active = [];
		foreach (Player player in Root.Players.GetChildrenOfClass<Player>()) { active.Add(player); CheckPlayer(player, interval); }
		foreach (Player player in new List<Player>(_states.Keys)) if (!active.Contains(player)) _states.Remove(player);
	}

	private void OnWorldInfoReady(BrickVerse.Schemas.API.APIPlaceInfo info)
	{
		Enabled = info.AntiCheatEnabled;
		if (!Enabled) { _states.Clear(); BV.PrintWarn("Internal anti-cheat is disabled by the game owner's universe settings."); }
	}

	private void CheckPlayer(Player player, float interval)
	{
		Vector3 position = player.Position; Vector3 velocity = player.Velocity;
		if (!_states.TryGetValue(player, out PlayerState? state))
		{
			ulong now = Time.GetTicksMsec();
			_states[player] = new PlayerState { Position = position, Velocity = velocity, LastSampleMsec = now, GraceUntilMsec = now + MovementWarmupMsec }; return;
		}
		if (state.EnforcementRequested) return;
		if (!IsFinite(position) || !IsFinite(velocity) || MaxAbs(position) > MaximumWorldCoordinate)
		{
			Flag(player, state, "invalid numeric/position state", 12); return;
		}
		bool grace = !player.IsReady || player.IsDead || player.Anchored || player.IsSitting || player.teleporting;
		ulong sampleTime = Time.GetTicksMsec();
		if (grace)
		{
			state.WasInGrace = true;
			state.GraceUntilMsec = sampleTime + MovementWarmupMsec;
			state.ConsecutiveSpeedSamples = 0;
			ResetMotionState(state, position, velocity);
			return;
		}
		if (state.WasInGrace)
		{
			state.WasInGrace = false;
			state.GraceUntilMsec = sampleTime + MovementWarmupMsec;
			ResetMotionState(state, position, velocity);
			return;
		}
		if (sampleTime < state.GraceUntilMsec) { ResetMotionState(state, position, velocity); return; }

		Vector3 displacement = position - state.Position;
		float horizontalDistance = new Vector2(displacement.X, displacement.Z).Length();
		float maximumSpeed = Math.Max(player.WalkSpeed, player.SprintSpeed);
		float externalAllowance = new Vector2(player.ExternalVelocity.X, player.ExternalVelocity.Z).Length();
		float distanceAllowance = (maximumSpeed * 2.15f + externalAllowance) * interval + 2.5f + Math.Min(player.NetworkPing, 500) * 0.008f;
		if (displacement.Length() > TeleportDistance)
		{
			Flag(player, state, "teleport", 5);
			state.ConsecutiveSpeedSamples = 0;
		}
		else if (horizontalDistance > distanceAllowance)
		{
			state.ConsecutiveSpeedSamples++;
			// Network replication can deliver one movement update as a burst. Only
			// score speed that remains impossible across a full second of samples.

			// Disabled for now, as it is too aggressive and can be triggered by network jitter.

			// if (state.ConsecutiveSpeedSamples >= 5 && state.ConsecutiveSpeedSamples % 5 == 0)
			// Flag(player, state, "sustained impossible horizontal speed", 2.5f);
		}
		else state.ConsecutiveSpeedSamples = 0;

		// Ground, wall and velocity state are authoritative only for a locally
		// simulated character. A dedicated server receives remote character
		// transforms in bursts and must not infer noclip/fly from its stale body.
		if (player.IsLocal)
		{
			Vector2 horizontalVelocity = new(velocity.X, velocity.Z); Vector2 oldHorizontalVelocity = new(state.Velocity.X, state.Velocity.Z);
			float acceleration = (horizontalVelocity - oldHorizontalVelocity).Length() / Math.Max(interval, 0.01f);
			if (acceleration > Math.Max(180f, maximumSpeed * 14f) && externalAllowance < 1f) Flag(player, state, "impossible acceleration", 1.25f);
			if (player.IsOnGround || player.IsClimbing) { state.AirborneSeconds = 0; state.HoverSeconds = 0; }
			else
			{
				state.AirborneSeconds += interval;
				if (Math.Abs(displacement.Y) < 0.08f && Math.Abs(velocity.Y) < 0.5f) state.HoverSeconds += interval; else state.HoverSeconds = Math.Max(0, state.HoverSeconds - interval);
				if (state.HoverSeconds > 1.8f) { Flag(player, state, "sustained hover/fly", 2); state.HoverSeconds = 0.8f; }
				if (state.AirborneSeconds > 2.5f && velocity.Y > Math.Max(player.JumpPower * 1.25f, 50f)) Flag(player, state, "impossible vertical velocity", 2);
			}
			if (player.CharBody3D.IsOnWall() && horizontalDistance > distanceAllowance * 0.8f) Flag(player, state, "collision bypass/noclip pattern", 0.75f);
		}

		state.Score = Math.Max(0, state.Score - interval * 0.12f);
		state.Position = position; state.Velocity = velocity; state.LastSampleMsec = Time.GetTicksMsec();
	}

	private void OnSuspiciousPeerActivity(int peerId, string reason, int severity)
	{
		Player? player = Root.Players.GetPlayerFromPeerID(peerId); if (player == null) return;
		if (!_states.TryGetValue(player, out PlayerState? state))
		{
			ulong now = Time.GetTicksMsec();
			_states[player] = state = new PlayerState { Position = player.Position, Velocity = player.Velocity, LastSampleMsec = now, GraceUntilMsec = now + MovementWarmupMsec };
		}
		Flag(player, state, "network:" + reason, Math.Clamp(severity, 1, 4));
	}

	private void CheckServerMemory()
	{
		_memoryElapsed = 0; long managed = GC.GetTotalMemory(false); long workingSet = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
		if (managed > ManagedMemoryWarningBytes || workingSet > WorkingSetWarningBytes || (_lastManagedMemory > 0 && managed - _lastManagedMemory > SuspiciousGrowthBytes))
			BV.PrintWarn("Internal integrity guard: abnormal server memory pressure managed=", managed, " workingSet=", workingSet, " growth=", managed - _lastManagedMemory);
		_lastManagedMemory = managed;
	}

	private void Flag(Player player, PlayerState state, string reason, float weight)
	{
		if (state.EnforcementRequested) return;
		state.Score += weight; BV.PrintWarn("Internal anti-cheat flag: ", player.Name, " reason=", reason, " score=", state.Score);
		if (state.Score >= EnforcementScore)
		{
			state.EnforcementRequested = true;
			player.Kick("You have been removed from the server for violating the game's anti-cheat policy. Please contact the game owner if you believe this is an error.");
		}
	}

	private static void ResetMotionState(PlayerState state, Vector3 position, Vector3 velocity) { state.Position = position; state.Velocity = velocity; state.AirborneSeconds = 0; state.HoverSeconds = 0; }
	private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
	private static float MaxAbs(Vector3 value) => Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
}
