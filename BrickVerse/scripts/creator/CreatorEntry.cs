// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Client.Settings.Appliers;
using BrickVerse.Client.WebAPI;
using BrickVerse.Creator.Managers;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.UI;
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

		// Create and show blocking auth overlay (must be early so nothing else works until login)
		var authOverlayScene = GD.Load<PackedScene>("res://scenes/creator/auth/auth_overlay.tscn");
		var authOverlay = authOverlayScene.Instantiate<Creator.UI.CreatorAuthOverlay>();
		AddChild(authOverlay);
		authOverlay.Name = "AuthOverlay";
		authOverlay.Visible = false; // will be shown by PolyDesktopAuthAPI.AskForAuthentication

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
			_ = ProjectManager.ImportLegacyWorld(legacyImportIn, legacyImportOut, new() { MainWorld = "main.poly", ProjectName = new DirectoryInfo(legacyImportOut).Name });
		}

		// Login creator with token
		if (launchToken != null)
		{
			await PolyCreatorAPI.LoginWithToken(launchToken);
		}
		else
		{
			// Start local dev server that receives brickverse:// tokens during editor testing
			DesktopAuthDevServer.StartIfEditor();

			// No CLI token: ensure desktop auth (browser / quick code) for Workshop
			PolyDesktopAuthAPI.UserAuthenticated += me => PT.Print("Creator authenticated via PolyDesktopAuthAPI as ", me.Username);
			PolyDesktopAuthAPI.AskForAuthentication += () => authOverlay.Visible = true;
			PolyDesktopAuthAPI.ShowQuickSignInCode += code => OS.Alert($"Quick Sign-In Code: {code}\n\nEnter it at brickverse.gg while signed in.", "Quick Sign-In");
			PolyDesktopAuthAPI.Setup();
		}
	}
}
