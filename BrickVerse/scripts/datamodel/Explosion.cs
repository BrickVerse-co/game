// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Datamodel;

[Instantiable]
public partial class Explosion : Dynamic
{
	private const float ExplosionParticleTimeSec = 10f;

	private GpuParticles3D _particle = null!;
	private float _radius = 10;
	private float _force = 5000;
	private bool _affectAnchored = false;
	private float _damage = 100000;
	private bool _affectWelds;
	private bool _useEffects = true;
	private float _blastPressure = 500000f;
	private float _destroyJointRadiusPercent = 1f;
	private bool _visible = true;
	private float _effectScale = 1f;
	private float _soundVolume = 1f;
	private float _soundPitch = 1f;
	private bool _damageEnabled = true;
	private bool _physicsEnabled = true;
	private bool _destroyJoints = true;
	private bool _autoDelete = true;
	private float _lifetime = ExplosionParticleTimeSec;
	private bool _hasExploded;

	[Editable, ScriptProperty]
	public float Radius
	{
		get => _radius;
		set
		{
			_radius = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Force
	{
		get => _force;
		set
		{
			_force = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool AffectAnchored
	{
		get => _affectAnchored;
		set
		{
			_affectAnchored = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Damage
	{
		get => _damage;
		set
		{
			_damage = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool AffectWelds
	{
		get => _affectWelds;
		set
		{
			_affectWelds = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool UseEffects
	{
		get => _useEffects;
		set
		{
			_useEffects = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float BlastPressure
	{
		get => _blastPressure;
		set
		{
			_blastPressure = Mathf.Max(0, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float DestroyJointRadiusPercent
	{
		get => _destroyJointRadiusPercent;
		set
		{
			_destroyJointRadiusPercent = Mathf.Clamp(value, 0, 1);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool Visible
	{
		get => _visible;
		set
		{
			_visible = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float EffectScale
	{
		get => _effectScale;
		set
		{
			_effectScale = Mathf.Max(0, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float SoundVolume
	{
		get => _soundVolume;
		set
		{
			_soundVolume = Mathf.Clamp(value, 0, 2);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float SoundPitch
	{
		get => _soundPitch;
		set
		{
			_soundPitch = Mathf.Max(0.001f, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool DamageEnabled
	{
		get => _damageEnabled;
		set
		{
			_damageEnabled = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool PhysicsEnabled
	{
		get => _physicsEnabled;
		set
		{
			_physicsEnabled = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool DestroyJoints
	{
		get => _destroyJoints;
		set
		{
			_destroyJoints = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool AutoDelete
	{
		get => _autoDelete;
		set
		{
			_autoDelete = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Lifetime
	{
		get => _lifetime;
		set
		{
			_lifetime = Mathf.Max(0, value);
			OnPropertyChanged();
		}
	}

	[ScriptProperty]
	public bool HasExploded => _hasExploded;

	[ScriptProperty]
	public BVFunction? AffectPredicate { get; set; }

	[ScriptProperty]
	public BVSignal<Instance> Hit { get; private set; } = new();

	[ScriptProperty]
	public BVSignal Exploded { get; private set; } = new();

	[ScriptMethod]
	public void Explode() => TryIgnite();

	[ScriptProperty]
	public BVSignal<Instance> Touched { get; private set; } = new();

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene(ClassName)!;
	}

	public override void Init()
	{
		base.Init();
		_particle = GDNode.GetNode<GpuParticles3D>("Particles");
		_particle.Visible = false;
	}

	public override void Ready()
	{
		TryIgnite();
		base.Ready();
	}

	public override void EnterTree()
	{
		TryIgnite();
		base.EnterTree();
	}

	private async void TryIgnite()
	{
		if (!IsNetworkReady || IsHidden || _hasExploded)
			return;

		_hasExploded = true;
		Exploded.Invoke();

		Sound? s = null;
		if (_useEffects)
		{
			_particle.Scale = Vector3.One * (_radius / 15f) * _effectScale;
			_particle.Visible = _visible;
			_particle.Emitting = _visible;

			if (!Root.Network.IsServer)
			{
				BuiltInAudioAsset audio = New<BuiltInAudioAsset>();
				audio.AudioPreset = BuiltInAudioAsset.BuiltInAudioPresetEnum.Explosion;

				s = New<Sound>();
				s.Audio = audio;
				s.PlayInWorld = true;
				s.Volume = _soundVolume;
				s.Pitch = _soundPitch;
				s.Parent = this;
				s.LocalPosition = Vector3.Zero;
			}
		}

		Instance[] overlaps = Root.Environment.OverlapSphere(Position, Radius);

		foreach (Instance item in overlaps)
		{
			Touched.Invoke(item);
			Hit.Invoke(item);

			if (AffectPredicate != null)
			{
				object?[] res = await AffectPredicate.Call(item);
				if (!(res.Length == 1 && res[0] is bool b && b))
				{
					continue;
				}
			}

			if (_physicsEnabled && item is Entity e && !item.IsDescendantOfClass("Accessory"))
			{
				if (e.Anchored && !AffectAnchored && AffectPredicate == null)
					continue;

				RigidBody3D body = e.GDRigidBody;
				Vector3 direction = body.GlobalTransform.Origin - GetGlobalTransform().Origin;
				float distance = direction.Length();
				bool unanchor = true;

				direction = direction.Normalized();

				if (
					(e.Size.X > Radius * 1.3 || e.Size.Y > Radius * 1.3 || e.Size.Z > Radius * 1.3)
					&& AffectPredicate == null
				)
				{
					unanchor = false;
				}

				if (unanchor)
				{
					e.Anchored = false;
				}

				float forceMagnitude =
					(Force + BlastPressure * 0.001f) * Mathf.Max(0, 1 - (distance / Radius));
				Vector3 force = direction * forceMagnitude / 100;

				body.ApplyCentralImpulse(force);

				if (
					_affectWelds
					&& _destroyJoints
					&& distance <= Radius * _destroyJointRadiusPercent
				)
				{
					foreach (Weld w in Weld.GetWeldsFor(e))
					{
						if (w.Enabled)
							w.Break();
					}
				}
			}
			else if (item is Player plr)
			{
				if (plr.IsDead)
					continue;

				if (_damageEnabled)
					plr.TakeDamage(Damage);
				if (_physicsEnabled)
					AddPlrExplosionForce(plr);
			}
		}

		// Play sound on next frame, needed to be loaded
		if (s != null)
		{
			Callable.From(s.Play).CallDeferred();
		}

		if (_autoDelete)
		{
			await Globals.Singleton.WaitAsync(_lifetime);
			Delete();
		}
	}

	private void AddPlrExplosionForce(Player player)
	{
		float force = Force * 0.02f;
		Vector3 dir = player.GetGlobalTransform().Origin - GetGlobalTransform().Origin;
		float wearoff = 1 - (dir.Length() / (Radius * 2f));
		wearoff = Mathf.Max(Mathf.Clamp(wearoff, 0, 1), 0.1f);
		Vector3 f = dir.Normalized() * force;
		f.X *= 1.5f;
		f.Z *= 1.5f;

		player.CharacterVelocity = f * wearoff;
	}
}
