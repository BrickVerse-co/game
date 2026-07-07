// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client;
using Polytoria.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Mobile.UI;

public partial class ViewTestPage : MobileViewBase
{
	private Button BeginButton = null!;
	private LineEdit AddresssField = null!;
	private DevLaunchOptions _launchOptions = new();
	private const string DevLaunchPath = "user://devlaunch";


	public override void _Ready()
	{
		BeginButton = GetNode<Button>("ConnectField/BeginButton");
		GetNode<Button>("RestartApp").Pressed += () =>
		{
			Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.MobileUI);
		};
		AddresssField = GetNode<LineEdit>("ConnectField/AddressField");
		GetNode<Label>("Version").Text = $"Running v{Globals.AppVersion}";

		if (FileAccess.FileExists(DevLaunchPath))
		{
			_launchOptions = JsonSerializer.Deserialize(FileAccess.GetFileAsString(DevLaunchPath), DevLaunchOptionsGenerationContext.Default.DevLaunchOptions)!;
		}

		AddresssField.Text = _launchOptions.ConnectAddress;
		BeginButton.Pressed += BeginPressed;
	}

	private void BeginPressed()
	{
		_launchOptions.ConnectAddress = AddresssField.Text;
		using FileAccess devlaunch = FileAccess.Open(DevLaunchPath, FileAccess.ModeFlags.Write);
		devlaunch.StoreString(JsonSerializer.Serialize(_launchOptions, DevLaunchOptionsGenerationContext.Default.DevLaunchOptions));
		devlaunch.Close();

		Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
		if (app is ClientEntry ce)
		{
			ClientEntry.ClientEntryData entryData = new()
			{
				ConnectAddress = _launchOptions.ConnectAddress
			};
			ce.Entry(entryData);
		}
	}

	[JsonSerializable(typeof(DevLaunchOptions))]
	internal partial class DevLaunchOptionsGenerationContext : JsonSerializerContext { }

	internal struct DevLaunchOptions
	{
		[JsonInclude]
		public string ConnectAddress = "";

		public DevLaunchOptions() { }
	}
}
