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
using System.Threading.Tasks;

namespace BrickVerse.Creator;

public partial class CreatorEntry : Node
{
	public const int CreatorPort = 24220;
	private Task _authInitializationTask = Task.CompletedTask;
	private string? _pendingWorldId;

	public override void _EnterTree()
	{
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();
		//BV.Print("CreatorEntry: Command line arguments: ", string.Join(", ", cmdargs));

		cmdargs.TryGetValue("token", out string? launchToken);

		//BV.Print("CreatorEntry: Launch token: ", launchToken ?? "(none)");

		CreatorAPI.AuthenticationFailed += OnClientAuthenticationFailed;
		CreatorAPI.UserAuthenticated += OnClientAuthenticated;

		_authInitializationTask = InitializeAuthAsync(launchToken);

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

		// Open project by world id cmd argument
		cmdargs.TryGetValue("world", out string? worldId);
		if (worldId != null)
		{
			BV.Print("Attempting to open world by id: ", worldId);
			_pendingWorldId = worldId;
		}

		// Open project by file path cmd argument
		cmdargs.TryGetValue("proj", out string? creatorFilePath);
		if (creatorFilePath != null)
		{
			BV.Print("Attempting to open project by file path: ", creatorFilePath);
			_ = CreatorService.Singleton.CreateNewSession(creatorFilePath);
		}

		// Import legacy world cmd arguments
		cmdargs.TryGetValue("liin", out string? legacyImportIn);
		cmdargs.TryGetValue("liout", out string? legacyImportOut);

		if (legacyImportIn != null && legacyImportOut != null)
		{
			BV.Print("Attempting to import legacy world from ", legacyImportIn, " to ", legacyImportOut);
			_ = ProjectManager.ImportLegacyWorld(legacyImportIn, legacyImportOut, new() { MainWorld = "main.bvxw", ProjectName = new DirectoryInfo(legacyImportOut).Name });
		}
	}

	public override async void _Ready()
	{
		base._Ready();

		try
		{
			await _authInitializationTask;
		}
		catch (Exception error)
		{
			BV.PrintErr("CreatorEntry: Auth initialization failed before startup open: ", error.Message);
		}

		if (!string.IsNullOrWhiteSpace(_pendingWorldId))
		{
			await CreatorService.Singleton.CreateNewSessionByWorldId(_pendingWorldId);
			_pendingWorldId = null;
		}
	}

	private static async Task InitializeAuthAsync(string? launchToken)
	{
		// Keep auth/network work off the startup path so creator UI can render immediately.
		if (launchToken != null)
		{
			try
			{
				await CreatorAPI.LoginWithToken(launchToken, true);
				return;
			}
			catch (Exception error)
			{
				BV.PrintErr("CreatorEntry: Launch token login failed, attempting saved session restore: ", error.Message);
			}
		}

		try
		{
			await CreatorAPI.SetupAuth();
		}
		catch (Exception error)
		{
			BV.PrintErr("CreatorEntry: Auth setup failed: ", error.Message);
		}
	}

	private void OnClientAuthenticationFailed(string reason)
	{
		BV.PrintErr("CreatorEntry: Client authentication failed: ", reason);
	}

	private void OnClientAuthenticated(OpenIdUserInfoResponse me)
	{
		BV.Print("Authenticated as ", me.PreferredUsername ?? me.Sub);
	}
}
