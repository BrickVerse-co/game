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
using BrickVerse.Datamodel.Resources;
using BrickVerse.Datamodel;
using DatamodelMesh = BrickVerse.Datamodel.Mesh;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BrickVerse.Creator;

public partial class CreatorEntry : Node
{
	public const int CreatorPort = 24220;
	private Task _authInitializationTask = Task.CompletedTask;
	private string? _pendingWorldId;
	private string? _pendingFilePath;
	private string? _pendingAssetId;
	private string? _pendingAssetType;
	private bool _pendingAssetImportInFlight;
	private const double SessionValidationIntervalSeconds = 10;
	private double _sessionValidationElapsed;
	private bool _sessionValidationInFlight;

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
		cmdargs.TryGetValue("world", out string? worldIdLegacy);
		cmdargs.TryGetValue("worldId", out string? worldId);
		if (worldId != null || worldIdLegacy != null)
		{
			BV.Print("Attempting to open world by id: ", worldId ?? worldIdLegacy);
			_pendingWorldId = worldId ?? worldIdLegacy;
		}

		// Open project or associated Creator file by path.
		cmdargs.TryGetValue("file", out string? associatedFilePath);
		cmdargs.TryGetValue("proj", out string? creatorFilePath);
		_pendingFilePath = associatedFilePath ?? creatorFilePath;
		if (!string.IsNullOrWhiteSpace(_pendingFilePath))
		{
			BV.Print("Attempting to open Creator file: ", _pendingFilePath);
		}

		cmdargs.TryGetValue("insertAssetId", out _pendingAssetId);
		cmdargs.TryGetValue("insertAssetType", out _pendingAssetType);
		if (!string.IsNullOrWhiteSpace(_pendingAssetId))
			BV.Print("Waiting to insert web asset ", _pendingAssetId, " into an open Creator world.");

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

		if (!string.IsNullOrWhiteSpace(_pendingFilePath))
		{
			string filePath = _pendingFilePath;
			_pendingFilePath = null;
			string extension = Path.GetExtension(filePath).ToLowerInvariant();
			if (extension == ".bvanim")
			{
				CreatorService.Interface.OpenAnimationEditor(filePath);
			}
			else if (extension is ".bvxm" or ".bvmodel" or ".model")
			{
				CreatorService.Interface.ImportModel(filePath);
			}
			else if (extension == ".bvaddon")
			{
				await AddonsManager.InstallAddonFile(filePath);
				CreatorService.Interface.PopupAlert(
					$"Installed {Path.GetFileName(filePath)}.",
					"Addon Installed"
				);
			}
			else
			{
				await CreatorService.Singleton.CreateNewSession(filePath);
			}
		}
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (!_pendingAssetImportInFlight && !string.IsNullOrWhiteSpace(_pendingAssetId) && World.Current != null)
			_ = InsertPendingAssetAsync();
		if (!CreatorAPI.IsUserAuthenticated || _sessionValidationInFlight)
			return;

		_sessionValidationElapsed += delta;
		if (_sessionValidationElapsed < SessionValidationIntervalSeconds)
			return;

		_sessionValidationElapsed = 0;
		_ = ValidateCreatorSession();
	}

	private async Task InsertPendingAssetAsync()
	{
		_pendingAssetImportInFlight = true;
		string assetId = _pendingAssetId!;
		string assetType = (_pendingAssetType ?? "PREFAB").ToUpperInvariant();
		try
		{
			await _authInitializationTask;
			World root = World.Current ?? throw new InvalidOperationException("Open a world before inserting this asset.");
			string name = "Asset" + assetId;
			Instance? inserted = assetType switch
			{
				"PREFAB" => await root.Insert.CreatorImportWebModel(assetId),
				"MESH" => CreateMeshAsset(root, assetId, name),
				"TEXTURE" => CreateTextureAsset(root, assetId, name),
				"SOUND" => CreateSoundAsset(root, assetId, name),
				"FONT" => CreateFontLabel(root, assetId, name),
				_ => throw new NotSupportedException($"{assetType} assets cannot be inserted into a world."),
			};

			if (inserted != null)
			{
				inserted.Parent = root.Environment;
				if (inserted is Dynamic dynamic)
					dynamic.Position = root.CreatorContext.Freelook.GetPlacementPosition();
				root.CreatorContext.Selections.SelectOnly(inserted);
				root.LinkedSession?.RescanFolder();
				CreatorService.Interface.StatusBar?.SetStatus($"Inserted asset {assetId}");
			}
		}
		catch (Exception error)
		{
			BV.PrintErr("Could not insert launched asset ", assetId, ": ", error);
			CreatorService.Interface.PopupAlert(error.Message, "Could not insert asset");
		}
		finally
		{
			_pendingAssetId = null;
			_pendingAssetType = null;
			_pendingAssetImportInFlight = false;
		}
	}

	private static DatamodelMesh CreateMeshAsset(World root, string assetId, string name)
	{
		DatamodelMesh mesh = root.New<DatamodelMesh>();
		mesh.Name = name;
		BVMeshAsset asset = root.New<BVMeshAsset>();
		asset.AssetID = assetId;
		mesh.Asset = asset;
		return mesh;
	}

	private static Image3D CreateTextureAsset(World root, string assetId, string name)
	{
		Image3D image = root.New<Image3D>();
		image.Name = name;
		BVImageAsset asset = root.New<BVImageAsset>();
		asset.ImageID = assetId;
		image.Image = asset;
		return image;
	}

	private static Sound CreateSoundAsset(World root, string assetId, string name)
	{
		Sound sound = root.New<Sound>();
		sound.Name = name;
		BVAudioAsset asset = root.New<BVAudioAsset>();
		asset.AudioID = assetId;
		sound.Audio = asset;
		return sound;
	}

	private static UILabel CreateFontLabel(World root, string assetId, string name)
	{
		BVFontAsset font = root.New<BVFontAsset>();
		font.FontID = assetId;
		UILabel label = root.New<UILabel>();
		label.Name = name;
		label.Text = name;
		label.FontAsset = font;
		return label;
	}

	private async Task ValidateCreatorSession()
	{
		_sessionValidationInFlight = true;
		try
		{
			await CreatorAPI.ValidateCurrentSessionAsync();
		}
		catch (Exception error)
		{
			// Network outages must not sign users out. The next interval retries.
			BV.PrintWarn("Creator session validation unavailable: ", error.Message);
		}
		finally
		{
			_sessionValidationInFlight = false;
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
		if (CreatorService.Interface != null)
			CreatorService.Interface.PopupAlert(reason, "Creator Session Ended");
	}

	private void OnClientAuthenticated(OpenIdUserInfoResponse me)
	{
		BV.Print("Authenticated as ", me.PreferredUsername ?? me.Sub);
	}
}
