using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Shared.Settings;
using System.Linq;

namespace BrickVerse.Client.Settings.Appliers;

public sealed partial class AudioSettingsApplier : Node
{
	public override void _Ready()
	{
		ClientSettingsService.Instance.Changed += OnChanged;
		ApplyAll();
	}

	public override void _ExitTree()
	{
		ClientSettingsService.Instance?.Changed -= OnChanged;
		base._ExitTree();
	}

	private void OnChanged(SettingChangedEvent change)
	{
		switch (change.Key)
		{
			case ClientSettingKeys.General.MasterVolume:
				ApplyVolume();
				break;
			case ClientSettingKeys.Audio.OutputDevice:
				ApplyOutputDevice();
				break;
			case ClientSettingKeys.Audio.MicrophoneInputDevice:
				ApplyInputDevice();
				break;
			case ClientSettingKeys.Audio.MicrophoneVolume:
				ApplyMicrophoneVolume();
				break;
			case ClientSettingKeys.Audio.VoiceChatVolume:
				ApplyVoiceChatVolume();
				break;
		}
	}

	private static void ApplyAll()
	{
		ApplyVolume();
		ApplyOutputDevice();
		ApplyInputDevice();
		ApplyMicrophoneVolume();
		ApplyVoiceChatVolume();
	}

	private static void ApplyVolume()
	{
		float volume = ClientSettingsService.Instance.Get<float>(ClientSettingKeys.General.MasterVolume);
		AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(volume / 100f));
	}

	private static void ApplyOutputDevice()
	{
		string device = ClientSettingsService.Instance.Get<string>(ClientSettingKeys.Audio.OutputDevice);
		AudioServer.OutputDevice = AudioServer.GetOutputDeviceList().Contains(device) ? device : "Default";
	}

	private static void ApplyInputDevice()
	{
		string device = ClientSettingsService.Instance.Get<string>(ClientSettingKeys.Audio.MicrophoneInputDevice);
		AudioServer.InputDevice = AudioServer.GetInputDeviceList().Contains(device) ? device : "Default";
	}

	private static void ApplyMicrophoneVolume()
	{
		float volume = ClientSettingsService.Instance.Get<float>(ClientSettingKeys.Audio.MicrophoneVolume) / 100f;
		World.Current?.VoiceChat?.SetMicrophoneVolume(volume);
	}

	private static void ApplyVoiceChatVolume()
	{
		float volume = ClientSettingsService.Instance.Get<float>(ClientSettingKeys.Audio.VoiceChatVolume) / 100f;
		World.Current?.VoiceChat?.SetOutputVolume(volume);
	}
}
