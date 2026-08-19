using BrickVerse.Creator.Settings;
using BrickVerse.Datamodel;
using Godot;

namespace BrickVerse.Creator.Utils;

public static class CreatorBuildEffects
{
	public static void Emit(Dynamic? target)
	{
		if (target?.GDNode3D == null || !GodotObject.IsInstanceValid(target.GDNode3D)
			|| CreatorSettingsService.Instance == null
			|| !CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.BuildParticlesEnabled)) return;

		Aabb bounds = target.CalculateBounds();
		Vector3 size = bounds.Size.Abs();
		Vector3 extents = new(Mathf.Max(.2f, size.X * .56f), Mathf.Max(.15f, size.Y * .56f), Mathf.Max(.2f, size.Z * .56f));
		float largestAxis = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
		Color color = target is Part part ? part.Color : new Color(.15f, .62f, 1f);
		color = color.Lightened((float)GD.RandRange(.02, .12));
		ParticleProcessMaterial material = new()
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = extents,
			Direction = Vector3.Up,
			Spread = 58f,
			Gravity = new Vector3(0, -7.5f, 0),
			InitialVelocityMin = Mathf.Clamp(largestAxis * .35f, 2.2f, 7f),
			InitialVelocityMax = Mathf.Clamp(largestAxis * .65f, 4.2f, 11f),
			ScaleMin = .7f,
			ScaleMax = 1.25f,
			Color = color
		};
		BoxMesh brick = new() { Size = new Vector3(.16f, .10f, .22f) };
		GpuParticles3D particles = new()
		{
			Name = "CreatorLegoBuildBurst",
			Amount = Mathf.Clamp(8 + Mathf.RoundToInt(largestAxis * 2f), 12, 34),
			Lifetime = .65,
			OneShot = true,
			Explosiveness = .92f,
			ProcessMaterial = material,
			DrawPass1 = brick
		};
		target.GDNode3D.AddChild(particles, false, Node.InternalMode.Back);
		particles.TopLevel = true;
		particles.GlobalPosition = bounds.GetCenter();
		particles.Finished += particles.QueueFree;
		particles.Emitting = true;
	}
}
