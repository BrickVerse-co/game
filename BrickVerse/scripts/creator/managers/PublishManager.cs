// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.UI;
using BrickVerse.Creator.Utils;
using BrickVerse.Creator.Settings;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Formats;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using System.Threading.Tasks;

namespace BrickVerse.Creator.Managers;

public static class PublishManager
{
	public static async Task PublishModel(Instance target, long modelID = 0)
	{
		var loadOverlay = CreatorService.Interface.LoadOverlay;
		try
		{
			loadOverlay?.SetTitle("Publishing model...");
			loadOverlay?.Show();
			byte[] packed = await PackedFormat.PackModel(target, loadOverlay.CreateProgressReporter("Publishing model"));

			loadOverlay?.SetStatus("Uploading now...");

			CreatorPublishResponse publishRes = await CreatorAPI.UploadAsset(
				packed,
				modelID,
				"PREFAB",
				"model.bvxm",
				target.Name
			);

			if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
				OS.ShellOpen(publishRes.Link);
			CreatorService.Interface.StatusBar?.SetStatus("Model published");
		}
		catch (Exception ex)
		{
			BV.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message);
		}
		finally
		{
			loadOverlay?.Hide();
		}
	}

	public static async Task PublishAddon(ServerScript target, long addonID = 0)
	{
		var loadOverlay = CreatorService.Interface.LoadOverlay;
		try
		{
			loadOverlay?.SetTitle("Publishing addon...");
			loadOverlay?.SetStatus("Packing addon...");
			loadOverlay?.Show();

			ModuleScript? metaModule = target.FindChild<ModuleScript>("AddonMetadata");
			if (metaModule == null)
			{
				metaModule = new ModuleScript
				{
					Name = "AddonMetadata",
					Source = @"{
	""Name"": """ + target.Name + @""",
	""Version"": ""1.0.0"",
	""Description"": ""A new addon"",
	""Author"": ""Your Name""
}"
				};
				metaModule.Parent = target;
			}

			AddonsManager.AddonMetadata metadata = AddonsManager.AddonMetadata.FromJson(metaModule.Source);
			if (string.IsNullOrWhiteSpace(metadata.Name) || string.IsNullOrWhiteSpace(metadata.Version))
				throw new InvalidOperationException("Addon metadata must include a name and version.");

			byte[] packed = await PackedFormat.PackAddon(
				target,
				metadata,
				loadOverlay.CreateProgressReporter("Publishing addon")
			);

			loadOverlay?.SetStatus("Uploading now...");
			CreatorPublishResponse publishRes = await CreatorAPI.UploadAsset(
				packed,
				addonID,
				"PLUGIN",
				"addon.bvaddon",
				metadata.Name,
				metadata.Description
			);

			if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
				OS.ShellOpen(publishRes.Link);
			CreatorService.Interface.StatusBar?.SetStatus("Addon published");
		}
		catch (Exception ex)
		{
			BV.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message);
		}
		finally
		{
			loadOverlay?.Hide();
		}
	}


}
