// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using BrickVerse.Shared;
using BrickVerse.Shared.Settings;

namespace BrickVerse.Creator.Settings;

public sealed partial class CreatorSettingsService : SettingsServiceBase
{
	private const string SettingsPathConst = "user://creator/creator_settings.json";
	private const string RenderingMethodMigrationPath = "user://creator/rendering_method_migration";
	public static CreatorSettingsService Instance { get; private set; } = null!;

	private static readonly Dictionary<string, string> OldToNewKeyMap = new()
	{
		["Creator.OpenWebAfterPublish"] = CreatorSettingKeys.Creator.OpenWebAfterPublish,
		["Interface.UIScale"] = CreatorSettingKeys.Interface.UiScale,
		["Interface.ThemeMode"] = CreatorSettingKeys.Interface.ThemeMode,
		["Interface.MoveSnapEnabled"] = CreatorSettingKeys.Interface.MoveSnapEnabled,
		["Interface.MoveSnapStep"] = CreatorSettingKeys.Interface.MoveSnapStep,
		["Interface.RotateSnapEnabled"] = CreatorSettingKeys.Interface.RotateSnapEnabled,
		["Interface.RotateSnapStep"] = CreatorSettingKeys.Interface.RotateSnapStep,
		["Interface.SnapToPartEnabled"] = CreatorSettingKeys.Interface.SnapToPartEnabled,
		["Interface.DuplicateOnDragEnabled"] = CreatorSettingKeys.Interface.DuplicateOnDragEnabled,
		["Interface.TransformOrientation"] = CreatorSettingKeys.Interface.TransformOrientation,
		["Interface.SelectionPivotMode"] = CreatorSettingKeys.Interface.SelectionPivotMode,
		["Keybinds.ToolSelect"] = CreatorSettingKeys.Keybinds.ToolSelect,
		["Keybinds.ToolMove"] = CreatorSettingKeys.Keybinds.ToolMove,
		["Keybinds.ToolRotate"] = CreatorSettingKeys.Keybinds.ToolRotate,
		["Keybinds.ToolScale"] = CreatorSettingKeys.Keybinds.ToolScale,
		["Keybinds.RotateSelection"] = CreatorSettingKeys.Keybinds.RotateSelection,
		["Keybinds.TiltSelection"] = CreatorSettingKeys.Keybinds.TiltSelection,
		["Keybinds.ToggleTransformOrientation"] = CreatorSettingKeys.Keybinds.ToggleTransformOrientation,
		["Keybinds.TogglePivotMode"] = CreatorSettingKeys.Keybinds.TogglePivotMode,
		["Interface.UseFullscreen"] = SharedSettingKeys.Display.Fullscreen,
		["Backup.MaxBackupCount"] = CreatorSettingKeys.Backup.MaxBackupCount,
		["Backup.BackupInterval"] = CreatorSettingKeys.Backup.BackupInterval,
		["CodeEditor.PreferredEditor"] = CreatorSettingKeys.CodeEditor.PreferredEditor,
		["CodeEditor.IndentationMode"] = CreatorSettingKeys.CodeEditor.IndentationMode,
		["CodeEditor.IndentationSize"] = CreatorSettingKeys.CodeEditor.IndentationSize,
		["CodeEditor.ShowLineNumbers"] = CreatorSettingKeys.CodeEditor.ShowLineNumbers,
		["CodeEditor.HighlightCurrentLine"] = CreatorSettingKeys.CodeEditor.HighlightCurrentLine,
		["CodeEditor.WordWrap"] = CreatorSettingKeys.CodeEditor.WordWrap,
		["CodeEditor.ShowWhitespace"] = CreatorSettingKeys.CodeEditor.ShowWhitespace,
		["CodeEditor.MinimapEnabled"] = CreatorSettingKeys.CodeEditor.MinimapEnabled,
		["CodeEditor.CursorBlink"] = CreatorSettingKeys.CodeEditor.CursorBlink,
		["CodeEditor.CursorBlinkSpeed"] = CreatorSettingKeys.CodeEditor.CursorBlinkSpeed,
		["CodeEditor.CursorWidth"] = CreatorSettingKeys.CodeEditor.CursorWidth,
		["Graphics.VSync"] = SharedSettingKeys.Display.VSync,
		["Graphics.RenderingMethod"] = SharedSettingKeys.Graphics.RenderingMethod,
		["Popups.CloseModelWarning"] = CreatorSettingKeys.Popups.CloseModelWarning,
		["Popups.MoveFileConfirmation"] = CreatorSettingKeys.Popups.MoveFileConfirmation,
		["Popups.CloseTabWarning"] = CreatorSettingKeys.Popups.CloseTabWarning,
	};

	protected override string SettingsPath => SettingsPathConst;
	protected override IReadOnlyDictionary<string, SettingDef> Registry => CreatorSettingsRegistry.Definitions;

	public CreatorSettingsService()
	{
		Instance = this;
	}

	public void Init()
	{
		MigrateFromOldFormat();
		Load();
		MigrateRenderingMethod();
		ApplyDefaults();

		if (!FileAccess.FileExists(SettingsPathConst))
			GraphicsPresetManager.SelectPreset(this, GraphicsPreset.Medium);

		RenderingMethodOption renderingMethod = Get<RenderingMethodOption>(SharedSettingKeys.Graphics.RenderingMethod);
		RenderingDeviceSwitcher.Switch(renderingMethod);
	}

	private static void MigrateFromOldFormat()
	{
		const string oldFilePath = "user://creator/creator_settings";

		if (!FileAccess.FileExists(oldFilePath))
			return;

		BV.Print("Migrating creator settings from old format");

		try
		{
			string oldJson = FileAccess.GetFileAsString(oldFilePath);
			var oldData = JsonSerializer.Deserialize(oldJson, MigrationJSONGenerationContext.Default.DictionaryStringString);

			if (oldData == null || oldData.Count == 0)
				return;

			var newData = new Dictionary<string, object?>();

			if (oldData.TryGetValue("Graphics.PostProcessing", out string? postProcessingStr)
				&& bool.TryParse(postProcessingStr, out bool postProcessing))
			{
				newData[SharedSettingKeys.PostProcessing.Glow] = postProcessing;
				newData[SharedSettingKeys.PostProcessing.Ssao] = postProcessing;
			}

			if (oldData.TryGetValue("Graphics.PhotoMode", out string? photoModeStr)
				&& bool.TryParse(photoModeStr, out bool photoMode) && photoMode)
			{
				newData[SharedSettingKeys.PostProcessing.Ssr] = true;
				newData[SharedSettingKeys.PostProcessing.Sdfgi] = true;
				newData[SharedSettingKeys.PostProcessing.Ssil] = true;
			}

			foreach ((string oldKey, string oldValue) in oldData)
			{
				if (!OldToNewKeyMap.TryGetValue(oldKey, out string? newKey))
					continue;

				if (!CreatorSettingsRegistry.Definitions.TryGetValue(newKey, out var def))
					continue;

				newData[newKey] = SettingsFileUtility.ParseStringValue(oldValue, def);
			}

			if (newData.Count == 0)
				return;

			string newJson = JsonSerializer.Serialize(newData, MigrationJSONGenerationContext.Default.DictionaryStringString);
			using var newFile = FileAccess.Open(SettingsPathConst, FileAccess.ModeFlags.Write);
			newFile.StoreString(newJson);
			newFile.Close();

			DirAccess.RenameAbsolute(oldFilePath, "user://creator/creator_settings.old");

			BV.Print("Migrated creator settings from old format");
		}
		catch (Exception e)
		{
			BV.PrintErr($"Failed to migrate creator settings: {e}");
		}
	}

	private void MigrateRenderingMethod()
	{
		if (FileAccess.FileExists(RenderingMethodMigrationPath))
		{
			return;
		}

		try
		{
			if (_values.TryGetValue(SharedSettingKeys.Graphics.RenderingMethod, out object? rawValue))
			{
				RenderingMethodOption renderingMethod = (RenderingMethodOption)CreatorSettingsRegistry.Definitions[SharedSettingKeys.Graphics.RenderingMethod].ConvertToType(rawValue);
				BV.Print("Current rendering method: " + renderingMethod);
				if (renderingMethod == RenderingMethodOption.Standard)
				{
					Set(SharedSettingKeys.Graphics.RenderingMethod, RenderingMethodOption.Auto);
					BV.Print("Migrated rendering method setting to Auto");
				}
				else
				{
					BV.Print("No migration needed for rendering method");
				}
			}

			using var file = FileAccess.Open(RenderingMethodMigrationPath, FileAccess.ModeFlags.Write);
			file.StoreString(Globals.AppVersion);
		}
		catch (Exception e)
		{
			BV.PrintErr("Failed to migrate rendering method setting: " + e);
		}
	}

	protected override void OnAfterSet(string key, object normalizedValue)
	{
		GraphicsPresetManager.HandlePresetChange(this, key, normalizedValue);
	}
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class MigrationJSONGenerationContext : JsonSerializerContext { }
