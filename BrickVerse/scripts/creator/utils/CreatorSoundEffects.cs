using BrickVerse.Creator.Settings;
using BrickVerse.Datamodel.Creator;
using Godot;
using System;
using System.Collections.Generic;

namespace BrickVerse.Creator.Utils;

/// <summary>Short, procedural editor feedback cues. Streams are generated once and cached.</summary>
public static class CreatorSoundEffects
{
	public enum Effect { Place, Duplicate, Move, Rotate, Scale, Delete, Undo, Redo, Success, Error, UiClick, UiHover, ModalOpen, ModalClose }
	private readonly record struct Tone(float StartHz, float EndHz, float Seconds, float Gain, Waveform Wave = Waveform.Sine);
	private enum Waveform { Sine, Triangle, Square, Noise }
	private const int SampleRate = 24000;
	private const int MaximumVoices = 6;
	private static readonly Dictionary<Effect, AudioStreamWav> Streams = [];
	private static readonly Dictionary<Effect, ulong> LastPlayedAt = [];
	private static int _activeVoices;

	public static void PlayPlace() => Play(Effect.Place);
	public static void PlayDuplicate() => Play(Effect.Duplicate);
	public static void PlayMove() => Play(Effect.Move);
	public static void PlayRotate() => Play(Effect.Rotate);
	public static void PlayScale() => Play(Effect.Scale);
	public static void PlayDelete() => Play(Effect.Delete);
	public static void PlayUndo() => Play(Effect.Undo);
	public static void PlayRedo() => Play(Effect.Redo);
	public static void PlaySuccess() => Play(Effect.Success);
	public static void PlayError() => Play(Effect.Error);
	public static void PlayUiClick() => Play(Effect.UiClick);
	public static void PlayUiHover() => Play(Effect.UiHover);
	public static void PlayModalOpen() => Play(Effect.ModalOpen);
	public static void PlayModalClose() => Play(Effect.ModalClose);

	public static void Play(Effect effect)
	{
		if (!IsEnabled() || _activeVoices >= MaximumVoices) return;
		ulong now = Time.GetTicksMsec();
		ulong cooldown = effect switch { Effect.Rotate => 18UL, Effect.UiHover => 45UL, Effect.Move or Effect.Scale => 55UL, _ => 35UL };
		if (LastPlayedAt.TryGetValue(effect, out ulong last) && now - last < cooldown) return;
		LastPlayedAt[effect] = now;
		if (!Streams.TryGetValue(effect, out AudioStreamWav? stream)) Streams[effect] = stream = BuildStream(GetTones(effect));
		float master = CreatorSettingsService.Instance.Get<float>(CreatorSettingKeys.Creator.SoundEffectsVolume) / 100f;
		bool uiEffect = effect is Effect.UiClick or Effect.UiHover or Effect.ModalOpen or Effect.ModalClose;
		float category = CreatorSettingsService.Instance.Get<float>(uiEffect ? CreatorSettingKeys.Creator.UiSoundVolume : CreatorSettingKeys.Creator.BuildSoundVolume) / 100f;
		float volumeScale = master * category;
		if (volumeScale <= .0001f) return;
		AudioStreamPlayer player = new()
		{
			Name = $"Creator{effect}Sfx", Stream = stream, VolumeDb = GetVolume(effect) + Mathf.LinearToDb(volumeScale),
			PitchScale = (float)GD.RandRange(0.975, 1.025)
		};
		_activeVoices++;
		CreatorService.Interface.AddChild(player);
		player.Finished += () => { _activeVoices = Math.Max(0, _activeVoices - 1); player.QueueFree(); };
		player.Play();
	}

	private static bool IsEnabled() => CreatorSettingsService.Instance != null
		&& CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.SoundEffectsEnabled)
		&& CreatorService.Interface != null && GodotObject.IsInstanceValid(CreatorService.Interface);

	private static float GetVolume(Effect effect) => effect switch
	{
		Effect.Error => -13f,
		Effect.UiHover => -24f,
		Effect.UiClick => -20f,
		Effect.ModalOpen or Effect.ModalClose => -18f,
		Effect.Move or Effect.Rotate or Effect.Scale => -17f,
		_ => -14f
	};

	private static Tone[] GetTones(Effect effect) => effect switch
	{
		Effect.Place => [new(720, 510, .055f, .72f, Waveform.Triangle), new(510, 390, .045f, .46f)],
		Effect.Duplicate => [new(480, 650, .045f, .48f), new(650, 900, .06f, .68f, Waveform.Triangle)],
		Effect.Move => [new(300, 470, .07f, .42f, Waveform.Triangle)],
		Effect.Rotate => [new(620, 390, .045f, .36f), new(390, 610, .055f, .42f)],
		Effect.Scale => [new(330, 760, .09f, .44f, Waveform.Triangle)],
		Effect.Delete => [new(320, 120, .10f, .58f, Waveform.Square), new(1800, 300, .035f, .18f, Waveform.Noise)],
		Effect.Undo => [new(680, 430, .08f, .46f), new(430, 520, .04f, .30f)],
		Effect.Redo => [new(430, 680, .08f, .46f), new(680, 820, .04f, .30f)],
		Effect.Success => [new(520, 660, .055f, .46f), new(660, 880, .075f, .60f, Waveform.Triangle)],
		Effect.Error => [new(190, 150, .09f, .50f, Waveform.Square), new(165, 125, .11f, .44f, Waveform.Square)],
		Effect.UiClick => [new(760, 610, .028f, .34f, Waveform.Triangle)],
		Effect.UiHover => [new(1050, 1180, .018f, .18f)],
		Effect.ModalOpen => [new(360, 560, .055f, .32f), new(560, 760, .065f, .40f, Waveform.Triangle)],
		Effect.ModalClose => [new(720, 490, .055f, .32f), new(490, 340, .06f, .30f, Waveform.Triangle)],
		_ => [new(440, 440, .06f, .4f)]
	};

	private static AudioStreamWav BuildStream(Tone[] tones)
	{
		int gapSamples = (int)(SampleRate * .008f);
		int sampleCount = gapSamples * Math.Max(0, tones.Length - 1);
		foreach (Tone tone in tones) sampleCount += Math.Max(1, (int)(tone.Seconds * SampleRate));
		byte[] pcm = new byte[sampleCount * 2];
		int cursor = 0; uint noiseState = 0x9E3779B9u;
		foreach (Tone tone in tones)
		{
			int count = Math.Max(1, (int)(tone.Seconds * SampleRate)); double phase = 0;
			for (int i = 0; i < count; i++, cursor++)
			{
				float progress = i / (float)Math.Max(1, count - 1);
				phase += Math.Tau * Mathf.Lerp(tone.StartHz, tone.EndHz, progress) / SampleRate;
				float oscillator = tone.Wave switch
				{
					Waveform.Triangle => (float)(2.0 / Math.PI * Math.Asin(Math.Sin(phase))),
					Waveform.Square => Math.Sin(phase) >= 0 ? .72f : -.72f,
					Waveform.Noise => NextNoise(ref noiseState),
					_ => (float)Math.Sin(phase)
				};
				float envelope = Mathf.Min(1f, progress / .08f) * Mathf.Pow(1f - progress, 1.7f);
				short sample = (short)Mathf.Clamp(oscillator * envelope * tone.Gain * short.MaxValue, short.MinValue, short.MaxValue);
				pcm[cursor * 2] = (byte)(sample & 0xff); pcm[cursor * 2 + 1] = (byte)((sample >> 8) & 0xff);
			}
			cursor += gapSamples;
		}
		return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = SampleRate, Stereo = false, Data = pcm };
	}

	private static float NextNoise(ref uint state)
	{
		state ^= state << 13; state ^= state >> 17; state ^= state << 5;
		return state / (float)uint.MaxValue * 2f - 1f;
	}
}
