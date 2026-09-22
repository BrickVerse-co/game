using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrickVerse.Attributes;
using BrickVerse.Formats;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>Resolves portable instance paths and writes through the same setters as the property inspector.</summary>
internal sealed class InstanceAnimationBinding : IDisposable
{
	private readonly List<(Instance Target, PropertyInfo Property, int Track)> _bindings = [];
	private readonly Animation _samples = new();

	public InstanceAnimationBinding(Instance root, BVAnimationClip clip)
	{
		_samples.Length = clip.Length;
		foreach (BVAnimationTrack track in clip.Tracks.Where(t => t.Channel == "instance"))
		{
			int split = track.Path.LastIndexOf(':');
			Instance target = Resolve(root, track.Path[..split]);
			PropertyInfo property = Properties(target).FirstOrDefault(p => p.Name == track.Path[(split + 1)..])
				?? throw new InvalidOperationException($"Animation property '{track.Path}' is not writable.");
			if (ValueType(property.PropertyType) != track.ValueType)
				throw new InvalidOperationException($"Animation property '{track.Path}' has changed type.");
			int index = _samples.AddTrack(Animation.TrackType.Value);
			_samples.TrackSetInterpolationLoopWrap(index, false);
			_samples.TrackSetInterpolationType(index, track.ValueType is "Bool" or "Int" or "String" ? Animation.InterpolationType.Nearest :
				Enum.TryParse(track.Interpolation, true, out Animation.InterpolationType interpolation) ? interpolation : Animation.InterpolationType.Linear);
			foreach (BVAnimationKey key in track.Keys)
				_samples.TrackInsertKey(index, key.Time, BVAnimationFormat.DecodeValue(track.ValueType, key.Value, key.Text), key.Transition);
			_bindings.Add((target, property, index));
		}
	}

	public void Apply(double time)
	{
		foreach (var binding in _bindings)
		{
			if (binding.Target.IsDeleted) continue;
			Variant value = _samples.ValueTrackInterpolate(binding.Track, (float)time);
			binding.Property.SetValue(binding.Target, ConvertValue(value, binding.Property.PropertyType));
		}
	}

	public void Dispose() { }

	internal static IEnumerable<PropertyInfo> Properties(Instance instance) => instance.GetEditableProperties()
		.Where(p => p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0 &&
			p.GetCustomAttribute<EditableAttribute>()?.IsHidden != true && ValueType(p.PropertyType) != null && p.Name is not "Name" and not "Archivable")
		.OrderBy(p => p.Name);

	internal static string? ValueType(Type type) => type == typeof(float) || type == typeof(double) ? "Float" :
		type == typeof(int) || type.IsEnum ? "Int" : type == typeof(bool) ? "Bool" : type == typeof(string) ? "String" :
		type == typeof(Vector2) ? "Vector2" : type == typeof(Vector3) ? "Vector3" : type == typeof(Color) ? "Color" : type == typeof(Quaternion) ? "Quaternion" : null;

	internal static Variant Read(Instance target, PropertyInfo property) => property.GetValue(target) switch
	{
		float value => Variant.From(value), double value => Variant.From(value), int value => Variant.From(value),
		Enum value => Variant.From(Convert.ToInt32(value)), bool value => Variant.From(value), string value => Variant.From(value),
		Vector2 value => Variant.From(value), Vector3 value => Variant.From(value), Color value => Variant.From(value), Quaternion value => Variant.From(value),
		_ => throw new InvalidOperationException($"Unsupported property '{property.Name}'.")
	};

	internal static object ConvertValue(Variant value, Type type) => type.IsEnum ? Enum.ToObject(type, value.AsInt32()) :
		type == typeof(int) ? value.AsInt32() : type == typeof(float) ? value.AsSingle() : type == typeof(double) ? value.AsDouble() :
		type == typeof(bool) ? value.AsBool() : type == typeof(string) ? value.AsString() : type == typeof(Vector2) ? value.AsVector2() :
		type == typeof(Vector3) ? value.AsVector3() : type == typeof(Color) ? value.AsColor() : value.AsQuaternion();

	internal static string Path(Instance root, Instance target)
	{
		List<string> parts = [];
		for (Instance? current = target; current != root; current = current.Parent)
		{
			if (current == null) throw new InvalidOperationException("Animation target is outside the selected hierarchy.");
			parts.Add(Uri.EscapeDataString(current.Name));
		}
		parts.Reverse();
		return parts.Count == 0 ? "." : string.Join('/', parts);
	}

	internal static Instance Resolve(Instance root, string path)
	{
		if (path == ".") return root;
		Instance current = root;
		foreach (string part in path.Split('/'))
		{
			string name = Uri.UnescapeDataString(part);
			Instance[] matches = current.GetChildren().Where(child => child.Name == name).ToArray();
			if (matches.Length != 1) throw new InvalidOperationException($"Animation target '{path}' is missing or ambiguous. Use unique child names.");
			current = matches[0];
		}
		return current;
	}
}
