using Godot;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.Utils;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Datamodel.Creator;
using System;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class CreatorUpdatePopup : Window
{
	[Export] private Label _version = null!;
	[Export] private CheckBox _neverShow = null!;
	[Export] private Button _later = null!;
	[Export] private Button _update = null!;
	[Export] private Button _download = null!;

	private CreatorLatestBinaryResponse _release = null!;

	public static async void CheckAndShow()
	{
		if (!CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.ShowUpdateNotifications)) return;
		string configuredVersion = ProjectSettings.GetSetting("brickverse/build/version", "").AsString();
		string configuredChannel = ProjectSettings.GetSetting("brickverse/build/channel", "").AsString();
		if (OS.IsDebugBuild() && string.IsNullOrWhiteSpace(configuredVersion)) return;
		string branch = configuredChannel == "beta" ? "beta" : "main";
		try
		{
			CreatorLatestBinaryResponse? latest = await CreatorAPI.GetLatestCreatorBinary(Globals.ResolveCurrentPlatform(), branch);
			if (latest == null || string.IsNullOrWhiteSpace(latest.Version)) return;
			string installed = string.IsNullOrWhiteSpace(configuredVersion) ? Globals.AppVersion : configuredVersion;
			if (string.Equals(installed, latest.Version, StringComparison.OrdinalIgnoreCase)) return;
			CreatorUpdatePopup popup = GD.Load<PackedScene>("res://scenes/creator/popups/creator_update.tscn").Instantiate<CreatorUpdatePopup>();
			popup._release = latest;
			CreatorService.Interface.PopupWindow(popup);
		}
		catch (Exception error)
		{
			BV.PrintWarn("Could not check for Creator updates: ", error.Message);
		}
	}

	public override void _Ready()
	{
		_version.Text = $"Installed: {GetInstalledVersion()}\nAvailable: {_release.Version}"
			+ (_release.BuildNumber.HasValue ? $" (build {_release.BuildNumber})" : "");
		_later.Pressed += Close;
		_update.Pressed += () => { SavePreference(); OS.ShellOpen("brickverse://installer?product=creator"); Close(); };
		_download.Pressed += () => OS.ShellOpen("https://brickverse.gg/download");
		CloseRequested += Close;
	}

	private static string GetInstalledVersion()
	{
		string version = ProjectSettings.GetSetting("brickverse/build/version", "").AsString();
		return string.IsNullOrWhiteSpace(version) ? Globals.AppVersion : version;
	}

	private void Close()
	{
		SavePreference();
		QueueFree();
	}

	private void SavePreference()
	{
		if (_neverShow.ButtonPressed)
			CreatorSettingsService.Instance.Set(CreatorSettingKeys.Creator.ShowUpdateNotifications, false);
	}
}
