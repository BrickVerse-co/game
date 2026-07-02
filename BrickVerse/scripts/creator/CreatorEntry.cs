// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Godot;
using BrickVerse.Client.Settings.Appliers;
using BrickVerse.Client.WebAPI;
using BrickVerse.Creator.Managers;
using BrickVerse.Creator.Settings;
using BrickVerse.Schemas.API;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Shared.Settings;
using System.Collections.Generic;
using System.IO;

namespace BrickVerse.Creator;

public partial class CreatorEntry : Node
{
	public const int CreatorPort = 24220;

	public async override void _EnterTree()
	{
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();
		cmdargs.TryGetValue("token", out string? launchToken);

		PT.Print("CreatorEntry: Launch token: ", launchToken ?? "(none)");

		CreatorAPI.AuthenticationFailed += OnClientAuthenticationFailed;

		// Prefer restoring a saved session. If the app was launched with a token,
		// use it and persist it so subsequent launches can restore without prompting.
		if (launchToken != null)
		{
			try
			{
				await CreatorAPI.LoginWithToken(launchToken, true);
			}
			catch (Exception error)
			{
				PT.PrintErr("CreatorEntry: Launch token login failed, attempting saved session restore: ", error.Message);
				await CreatorAPI.SetupAuth();
			}
		}
		else
		{
			await CreatorAPI.SetupAuth();
		}

		CreatorService creatorService = new();
		AddChild(creatorService);

		CreatorSettingsService creatorSettingsService = new()
		{
			Name = "CreatorSettingsService"
		};
		AddChild(creatorSettingsService, true, InternalMode.Front);
		creatorSettingsService.Init();

		AssetLoader.Singleton.MaxConcurrentRequests = creatorSettingsService.Get<int>(SharedSettingKeys.Advanced.AssetQueue);

		creatorSettingsService.AddChild(new GraphicsSettingsApplier { Name = GraphicsSettingsApplier.NodeName, Settings = creatorSettingsService }, true, InternalMode.Front);

		GetViewport().GuiEmbedSubwindows = true;

		// Open project
		cmdargs.TryGetValue("proj", out string? creatorFilePath);
		if (creatorFilePath != null)
		{
			_ = CreatorService.Singleton.CreateNewSession(creatorFilePath);
		}

		// Import legacy world cmd arguments
		cmdargs.TryGetValue("liin", out string? legacyImportIn);
		cmdargs.TryGetValue("liout", out string? legacyImportOut);

		if (legacyImportIn != null && legacyImportOut != null)
		{
			_ = ProjectManager.ImportLegacyWorld(legacyImportIn, legacyImportOut, new() { MainWorld = "main.bvxw", ProjectName = new DirectoryInfo(legacyImportOut).Name });
		}
	}

	private void OnClientAuthenticationFailed(string reason)
	{
		PT.PrintErr("CreatorEntry: Client authentication failed: ", reason);
	}

	private void OnClientAuthenticated(OpenIdUserInfoResponse me)
	{
		PT.Print("Authenticated as ", me.PreferredUsername ?? me.Sub);
	}
}
