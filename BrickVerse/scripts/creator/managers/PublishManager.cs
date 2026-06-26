// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.UI;
using BrickVerse.Creator.Utils;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.Managers;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Formats;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BrickVerse.Creator.Managers;

public static class PublishManager
{
	public static async Task PublishProject(string projectPath, int universeId = 0, int worldId = 0)
	{
		var loadOverlay = CreatorService.Interface.LoadOverlay;
		try
		{
			var metadata = PackedFormat.ReadProjectMetadata(File.ReadAllText(projectPath.PathJoin(Globals.ProjectMetaFileName)));
			var packed = await PackedFormat.PackProject(projectPath, loadOverlay.CreateProgressReporter("Publishing world"));

			loadOverlay?.SetStatus("Uploading now...");
			CreatorPublishResponse publishRes = await CreatorAPI.UploadWorld(packed, universeId, worldId);

			if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
				OS.ShellOpen(publishRes.Link);
			CreatorService.Interface.StatusBar?.SetStatus("World published");
			loadOverlay?.Hide();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message);
			loadOverlay?.Hide();
		}
	}

	public static async Task PublishModel(Instance target, int modelID = 0)
	{
		var loadOverlay = CreatorService.Interface.LoadOverlay;
		try
		{
			byte[] packed = await PackedFormat.PackModel(target, loadOverlay.CreateProgressReporter("Publishing model"));

			CreatorService.Interface.LoadOverlay?.SetStatus("Uploading now...");

			CreatorPublishResponse publishRes = await CreatorAPI.UploadModel(packed, modelID);
			CreatorService.Interface.LoadOverlay?.Hide();

			if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
				OS.ShellOpen(publishRes.Link);
			CreatorService.Interface.StatusBar?.SetStatus("Model published");
			loadOverlay?.Hide();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message);
			loadOverlay?.Hide();
		}
	}

	/*public static async Task PublishAddon(ServerScript target, int placeID = 0)
	{
		CreatorService.Interface.LoadOverlay?.SetTitle("Publishing addon...");
		CreatorService.Interface.LoadOverlay?.SetStatus("Packing addon...");
		CreatorService.Interface.LoadOverlay?.Show();

		// Check ServerScript has a ModuleScript as a child named "AddonMetadata"
		ModuleScript? metaModule = target.FindChild<ModuleScript>("AddonMetadata");
		if (metaModule == null)
		{
			// Create one 
			metaModule = new ModuleScript();
			metaModule.Name = "AddonMetadata";

			// Set the source code to a default template
			metaModule.Source = @"{
	""Name"": """ + target.Name + @""",
	""Version"": ""1.0.0"",
	""Description"": ""A new addon"",
	""Author"": ""Your Name"",
	}";

			// Add it as a child of the ServerScript
			metaModule.Parent = target;
		}

		// Extract the metadata from the ModuleScript as AddonMetadata from the source code
		AddonMetadata metadata = AddonMetadata.FromJson(metaModule.Source);

		byte[] packed = await PackedFormat.PackAddon(target, metadata);

		CreatorService.Interface.LoadOverlay?.SetStatus("Uploading now...");

		CreatorPublishResponse publishRes = await CreatorAPI.UploadAddon(packed, placeID);
		CreatorService.Interface.LoadOverlay?.Hide();

		if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
			OS.ShellOpen(publishRes.Link);
		CreatorService.Interface.StatusBar?.SetStatus("Addon published");
	}
*/

}
