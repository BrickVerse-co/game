// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrickVerse.Formats;

/// <summary>
/// BrickVerse's portable skeletal-animation format. Tracks retain Godot NodePaths
/// so animations can target any rig; the format deliberately does not prescribe
/// Mixamo or Brickversian bone names.
/// </summary>
public static class BVAnimationFormat
{
	public const int CurrentVersion = 1;
	public const string MimeType = "application/vnd.brickverse.animation+json";

	public static BVAnimationClip Read(byte[] data)
	{
		BVAnimationClip? clip = JsonSerializer.Deserialize(
			data,
			BVAnimationJsonContext.Default.BVAnimationClip
		);
		if (clip == null)
			throw new InvalidOperationException("Animation file is empty.");

		Validate(clip);
		return clip;
	}

	public static byte[] Write(BVAnimationClip clip)
	{
		Validate(clip);
		return JsonSerializer.SerializeToUtf8Bytes(
			clip,
			BVAnimationJsonContext.Default.BVAnimationClip
		);
	}

	public static BVAnimationClip FromAnimation(string name, Animation animation)
	{
		BVAnimationClip clip = new()
		{
			Name = string.IsNullOrWhiteSpace(name) ? "Animation" : name,
			Length = (float)animation.Length,
			LoopMode = animation.LoopMode.ToString(),
		};

		for (int trackIndex = 0; trackIndex < animation.GetTrackCount(); trackIndex++)
		{
			Animation.TrackType type = animation.TrackGetType(trackIndex);
			if (
				type is not Animation.TrackType.Position3D
				and not Animation.TrackType.Rotation3D
				and not Animation.TrackType.Scale3D
			)
				continue;

			BVAnimationTrack track = new()
			{
				Path = animation.TrackGetPath(trackIndex).ToString(),
				Channel = type switch
				{
					Animation.TrackType.Position3D => "position",
					Animation.TrackType.Rotation3D => "rotation",
					_ => "scale",
				},
				Interpolation = animation.TrackGetInterpolationType(trackIndex).ToString(),
			};

			for (int keyIndex = 0; keyIndex < animation.TrackGetKeyCount(trackIndex); keyIndex++)
			{
				Variant value = animation.TrackGetKeyValue(trackIndex, keyIndex);
				float[] components = type switch
				{
					Animation.TrackType.Rotation3D => QuaternionComponents(value.AsQuaternion()),
					_ => VectorComponents(value.AsVector3()),
				};
				track.Keys.Add(
					new BVAnimationKey
					{
						Time = animation.TrackGetKeyTime(trackIndex, keyIndex),
						Transition = animation.TrackGetKeyTransition(trackIndex, keyIndex),
						Value = components,
					}
				);
			}
			clip.Tracks.Add(track);
		}

		Validate(clip);
		return clip;
	}

	public static Animation ToAnimation(BVAnimationClip clip)
	{
		Validate(clip);
		Animation animation = new()
		{
			Length = clip.Length,
			LoopMode = Enum.TryParse(
				clip.LoopMode,
				true,
				out Animation.LoopModeEnum loopMode
			)
				? loopMode
				: Animation.LoopModeEnum.None,
		};

		foreach (BVAnimationTrack track in clip.Tracks)
		{
			Animation.TrackType type = track.Channel switch
			{
				"position" => Animation.TrackType.Position3D,
				"rotation" => Animation.TrackType.Rotation3D,
				"scale" => Animation.TrackType.Scale3D,
				_ => throw new InvalidOperationException(
					$"Unsupported animation channel '{track.Channel}'."
				),
			};
			int index = animation.AddTrack(type);
			animation.TrackSetPath(index, new NodePath(track.Path));
			if (
				Enum.TryParse(
					track.Interpolation,
					true,
					out Animation.InterpolationType interpolation
				)
			)
				animation.TrackSetInterpolationType(index, interpolation);

			foreach (BVAnimationKey key in track.Keys)
			{
				Variant value =
					type == Animation.TrackType.Rotation3D
						? new Quaternion(key.Value[0], key.Value[1], key.Value[2], key.Value[3])
						: new Vector3(key.Value[0], key.Value[1], key.Value[2]);
				animation.TrackInsertKey(index, key.Time, value, key.Transition);
			}
		}
		return animation;
	}

	public static AnimationLibrary ToLibrary(BVAnimationClip clip)
	{
		AnimationLibrary library = new();
		library.AddAnimation(clip.Name, ToAnimation(clip));
		return library;
	}

	public static void Validate(BVAnimationClip clip)
	{
		if (clip.Format != "BVAnimation")
			throw new InvalidOperationException("Not a BrickVerse animation file.");
		if (clip.Version != CurrentVersion)
			throw new InvalidOperationException($"Unsupported animation version {clip.Version}.");
		if (string.IsNullOrWhiteSpace(clip.Name) || clip.Name.Length > 100)
			throw new InvalidOperationException("Animation name must be between 1 and 100 characters.");
		if (!float.IsFinite(clip.Length) || clip.Length <= 0 || clip.Length > 3600)
			throw new InvalidOperationException("Animation length must be between 0 and 3600 seconds.");
		if (clip.Tracks.Count == 0 || clip.Tracks.Count > 512)
			throw new InvalidOperationException("Animation must contain between 1 and 512 tracks.");

		int totalKeys = 0;
		foreach (BVAnimationTrack track in clip.Tracks)
		{
			if (string.IsNullOrWhiteSpace(track.Path) || track.Path.Length > 500)
				throw new InvalidOperationException("Animation track has an invalid target path.");
			int componentCount = track.Channel == "rotation" ? 4 : 3;
			if (track.Channel is not "position" and not "rotation" and not "scale")
				throw new InvalidOperationException($"Invalid animation channel '{track.Channel}'.");
			if (track.Keys.Count == 0)
				throw new InvalidOperationException($"Animation track '{track.Path}' has no keys.");

			double previousTime = -1;
			foreach (BVAnimationKey key in track.Keys)
			{
				if (
					!double.IsFinite(key.Time)
					|| key.Time < 0
					|| key.Time > clip.Length + 0.001
					|| key.Time < previousTime
				)
					throw new InvalidOperationException(
						$"Animation track '{track.Path}' has invalid key timing."
					);
				if (key.Value.Length != componentCount)
					throw new InvalidOperationException(
						$"Animation channel '{track.Channel}' has the wrong component count."
					);
				foreach (float component in key.Value)
					if (!float.IsFinite(component))
						throw new InvalidOperationException("Animation key contains a non-finite value.");
				previousTime = key.Time;
				totalKeys++;
			}
		}
		if (totalKeys > 100_000)
			throw new InvalidOperationException("Animation contains too many keyframes.");
	}

	private static float[] VectorComponents(Vector3 value) => [value.X, value.Y, value.Z];
	private static float[] QuaternionComponents(Quaternion value) =>
		[value.X, value.Y, value.Z, value.W];
}

public sealed class BVAnimationClip
{
	public string Format { get; set; } = "BVAnimation";
	public int Version { get; set; } = BVAnimationFormat.CurrentVersion;
	public string Name { get; set; } = "Animation";
	public float Length { get; set; } = 1;
	public string LoopMode { get; set; } = "None";
	public List<BVAnimationTrack> Tracks { get; set; } = [];
}

public sealed class BVAnimationTrack
{
	public string Path { get; set; } = "";
	public string Channel { get; set; } = "rotation";
	public string Interpolation { get; set; } = "Linear";
	public List<BVAnimationKey> Keys { get; set; } = [];
}

public sealed class BVAnimationKey
{
	public double Time { get; set; }
	public float Transition { get; set; } = 1;
	public float[] Value { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(BVAnimationClip))]
internal partial class BVAnimationJsonContext : JsonSerializerContext;
