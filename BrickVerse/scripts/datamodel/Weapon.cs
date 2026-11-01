// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using Godot;
using static BrickVerse.Datamodel.Environment;

namespace BrickVerse.Datamodel;

/// <summary>Server-authoritative hitscan weapon state that can be composed beneath a Tool or Model.</summary>
[Instantiable]
public partial class Weapon : Instance
{
	private ulong _lastShotMsec;
	private int _magazineSize = 30, _ammo = 30, _reserveAmmo = 90;

	[Editable, ScriptProperty, DefaultValue(25f)] public float Damage { get; set; } = 25f;
	[Editable, ScriptProperty, DefaultValue(500f)] public float Range { get; set; } = 500f;
	[Editable, ScriptProperty, DefaultValue(10f)] public float FireRate { get; set; } = 10f;
	[Editable, ScriptProperty, DefaultValue(30)] public int MagazineSize { get => _magazineSize; set { _magazineSize = Math.Max(1, value); _ammo = Math.Min(_ammo, _magazineSize); OnPropertyChanged(); } }
	[ScriptProperty, SyncVar] public int Ammo { get => _ammo; private set { _ammo = Math.Clamp(value, 0, MagazineSize); OnPropertyChanged(); } }
	[ScriptProperty, SyncVar] public int ReserveAmmo { get => _reserveAmmo; private set { _reserveAmmo = Math.Max(0, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public bool Automatic { get; set; }
	[ScriptProperty] public bool CanFire => Ammo > 0 && Time.GetTicksMsec() - _lastShotMsec >= ShotIntervalMsec;
	[ScriptProperty] public BVSignal<Vector3, Vector3, Instance?> Fired { get; private set; } = new();
	[ScriptProperty] public BVSignal<NPC, float> Damaged { get; private set; } = new();
	[ScriptProperty] public BVSignal Reloaded { get; private set; } = new();

	private ulong ShotIntervalMsec => (ulong)Math.Ceiling(1000.0 / Math.Max(0.01f, FireRate));

	[ScriptMethod]
	public RayResult? Fire(Vector3 origin, Vector3 direction, Instance[]? ignoreList = null)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Weapon.Fire must be validated and called by the server.");
		if (!origin.IsFinite() || !direction.IsFinite() || direction.IsZeroApprox()) throw new ArgumentException("Shot origin and direction must be finite and direction must be non-zero.");
		if (!CanFire) return null;
		_lastShotMsec = Time.GetTicksMsec(); Ammo--;
		RayResult? hit = Root.Environment.Raycast(origin, direction, Mathf.Max(0, Range), ignoreList);
		Vector3 end = hit?.Position ?? origin + direction.Normalized() * Mathf.Max(0, Range);
		Fired.Invoke(origin, end, hit?.Instance);
		NPC? target = hit?.Instance as NPC ?? hit?.Instance?.FindFirstAncestorWhichIsA(nameof(NPC)) as NPC;
		if (target != null && !target.IsDeleted)
		{
			float appliedDamage = Mathf.Max(0, Damage);
			target.TakeDamage(appliedDamage); Damaged.Invoke(target, appliedDamage);
		}
		return hit;
	}

	[ScriptMethod]
	public int Reload()
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Weapon.Reload must be called by the server.");
		int loaded = Math.Min(MagazineSize - Ammo, ReserveAmmo);
		if (loaded <= 0) return 0;
		Ammo += loaded; ReserveAmmo -= loaded; Reloaded.Invoke(); return loaded;
	}

	[ScriptMethod]
	public void SetAmmo(int ammo, int reserveAmmo)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Weapon.SetAmmo must be called by the server.");
		Ammo = ammo; ReserveAmmo = reserveAmmo;
	}
}
