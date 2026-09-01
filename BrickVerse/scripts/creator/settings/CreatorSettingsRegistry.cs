// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Shared.Settings;
using System.Collections.Generic;

namespace BrickVerse.Creator.Settings;

public static class CreatorSettingsRegistry
{
	private const string DefaultSectionIcon = "res://assets/textures/ui-icons/settings.svg";

	public static readonly IReadOnlyList<SettingSectionDef> Sections =
	[
		new() { Key = "creator", Label = "Creator", Description = "Publishing, cloud projects, sounds, tutorials, and play testing.", IconPath = DefaultSectionIcon, SortOrder = 0 },
		new() { Key = "interface", Label = "Interface", Description = "Appearance, scaling, snapping, and transform behavior.", IconPath = DefaultSectionIcon, SortOrder = 1 },
		new() { Key = "keybinds", Label = "Keybinds", Description = "Customize shortcuts for tools and common editing actions.", IconPath = DefaultSectionIcon, SortOrder = 2 },
		new() { Key = "display", Label = "Display", IconPath = "res://assets/textures/ui-icons/camera.svg", SortOrder = 3 },
		new() { Key = "graphics", Label = "Graphics", IconPath = "res://assets/textures/ui-icons/mountain.svg", SortOrder = 4 },
		new() { Key = "post_processing", Label = "Post Processing", IconPath = "res://assets/textures/ui-icons/rocket.svg", SortOrder = 5 },
		new() { Key = "backup", Label = "Backup", Description = "Control automatic recovery snapshots and retention.", IconPath = DefaultSectionIcon, SortOrder = 6 },
		new() { Key = "code_editor", Label = "Code Editor", Description = "Editing, formatting, assistance, and display preferences.", IconPath = DefaultSectionIcon, SortOrder = 7 },
		new() { Key = "popups", Label = "Confirmations", Description = "Choose which safety prompts and completion messages appear.", IconPath = DefaultSectionIcon, SortOrder = 8 },
		new() { Key = "advanced", Label = "Advanced", IconPath = DefaultSectionIcon, SortOrder = 9 }
	];

	public static readonly IReadOnlyDictionary<string, SettingDef> Definitions = Build();

	private static Dictionary<string, SettingDef> Build()
	{
		var defs = new Dictionary<string, SettingDef>();
		SharedSettingsRegistry.AddSharedTo(defs);

		// Creator
		defs.Add(CreatorSettingKeys.Creator.SoundEffectsEnabled,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.SoundEffectsEnabled,
				SectionKey = "creator",
				Label = "Creator Sound Effects",
				Description = "Play feedback sounds for placing parts and other Creator actions.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Creator.BuildParticlesEnabled,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.BuildParticlesEnabled,
				SectionKey = "creator",
				Label = "Build Particles",
				Description = "Show a small brick-like particle burst when placing or duplicating objects.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Creator.SoundEffectsVolume,
			new SettingDef<float> { Key = CreatorSettingKeys.Creator.SoundEffectsVolume, SectionKey = "creator", Label = "Sound Effects Volume", Description = "Master volume for Creator feedback sounds.", ValueKind = SettingValueKind.Float, ControlKind = SettingControlKind.Slider, DefaultValue = 75f, MinValue = 0f, MaxValue = 100f, Step = 1f });
		defs.Add(CreatorSettingKeys.Creator.BuildSoundVolume,
			new SettingDef<float> { Key = CreatorSettingKeys.Creator.BuildSoundVolume, SectionKey = "creator", Label = "Building Sounds Volume", Description = "Volume for placing, transforming, deleting, undoing, and redoing.", ValueKind = SettingValueKind.Float, ControlKind = SettingControlKind.Slider, DefaultValue = 85f, MinValue = 0f, MaxValue = 100f, Step = 1f });
		defs.Add(CreatorSettingKeys.Creator.UiSoundVolume,
			new SettingDef<float> { Key = CreatorSettingKeys.Creator.UiSoundVolume, SectionKey = "creator", Label = "UI and Hover Volume", Description = "Volume for buttons, mouse hover feedback, and modal transitions.", ValueKind = SettingValueKind.Float, ControlKind = SettingControlKind.Slider, DefaultValue = 55f, MinValue = 0f, MaxValue = 100f, Step = 1f });

		defs.Add(CreatorSettingKeys.Creator.OpenWebAfterPublish,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.OpenWebAfterPublish,
				SectionKey = "creator",
				Label = "Open Web after Publish",
				Description = "Open the published item in a browser after publishing.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Creator.DetailedRichPresence,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.DetailedRichPresence,
				SectionKey = "creator",
				Label = "Detailed Rich Presence",
				Description = "Show the current script, world, or play-test activity in Discord Rich Presence.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false
			});

		defs.Add(CreatorSettingKeys.Creator.PromptForWorldProjectLocation,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.PromptForWorldProjectLocation,
				SectionKey = "creator",
				Label = "Choose Project Folder When Opening Worlds",
				Description = "Ask where to create a local project the first time a cloud world is opened. When disabled, Creator uses Documents/BrickVerseCreator/My Worlds automatically.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false
			});

		defs.Add(CreatorSettingKeys.Creator.ConfirmCloudDownload,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.ConfirmCloudDownload,
				SectionKey = "creator",
				Label = "Confirm Cloud Downloads",
				Description = "Ask before downloading a cloud-only world into a new local Creator project.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Creator.RefreshCloudProjectsOnOpen,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.RefreshCloudProjectsOnOpen,
				SectionKey = "creator",
				Label = "Refresh Cloud Projects When Opened",
				Description = "Fetch the newest personal and editable guild projects whenever the Cloud tab is opened.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Creator.ShowInteractiveTutorial,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.ShowInteractiveTutorial,
				SectionKey = "creator",
				Label = "Interactive Getting Started Tutorial",
				Description = "Show the optional, skippable Creator walkthrough for new installations.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Creator.ShowWhatsNew,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.ShowWhatsNew,
				SectionKey = "creator",
				Label = "Show What's New",
				Description = "Automatically show release highlights after Creator updates. They remain available from the Help menu.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Creator.ShowUpdateNotifications,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Creator.ShowUpdateNotifications,
				SectionKey = "creator",
				Label = "Creator Update Notifications",
				Description = "Notify you at startup when a newer Creator build is available.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Creator.PlayTestPresentation,
			new SettingDef<PlayTestPresentationEnum>
			{
				Key = CreatorSettingKeys.Creator.PlayTestPresentation,
				SectionKey = "creator",
				Label = "Play-test Window",
				Description = "Attach play-tests over the active world viewport, or launch them as independent resizable windows.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = PlayTestPresentationEnum.Attached,
				Options =
				[
					new() { Value = PlayTestPresentationEnum.Attached, Label = "Attached to Creator" },
					new() { Value = PlayTestPresentationEnum.Windowed, Label = "Separate Window" },
				]
			});

		// Interface
		defs.Add(CreatorSettingKeys.Interface.UiScale,
			new SettingDef<float>
			{
				Key = CreatorSettingKeys.Interface.UiScale,
				SectionKey = "interface",
				Label = "UI Scale",
				Description = "Scale of the creator interface.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 1.0f,
				MinValue = 0.5f,
				MaxValue = 5f,
				Step = 0.25f
			});

		defs.Add(CreatorSettingKeys.Interface.ThemeMode,
			new SettingDef<CreatorThemeModeEnum>
			{
				Key = CreatorSettingKeys.Interface.ThemeMode,
				SectionKey = "interface",
				Label = "Theme Mode",
				Description = "Switch between dark and light Creator themes.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = CreatorThemeModeEnum.Dark,
				Options =
				[
					new() { Value = CreatorThemeModeEnum.Dark, Label = "Dark" },
					new() { Value = CreatorThemeModeEnum.Light, Label = "Light" },
				]
			});

		defs.Add(CreatorSettingKeys.Interface.MoveSnapEnabled,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Interface.MoveSnapEnabled,
				SectionKey = "interface",
				Label = "Move Snap Enabled",
				Description = "Enable grid snapping while moving selections.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Interface.MoveSnapStep,
			new SettingDef<float>
			{
				Key = CreatorSettingKeys.Interface.MoveSnapStep,
				SectionKey = "interface",
				Label = "Move Snap Step",
				Description = "Grid size for move snapping.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 1f,
				MinValue = 0.1f,
				MaxValue = 16f,
				Step = 0.1f
			});

		defs.Add(CreatorSettingKeys.Interface.RotateSnapEnabled,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Interface.RotateSnapEnabled,
				SectionKey = "interface",
				Label = "Rotate Snap Enabled",
				Description = "Enable angular snapping while rotating selections.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Interface.RotateSnapStep,
			new SettingDef<float>
			{
				Key = CreatorSettingKeys.Interface.RotateSnapStep,
				SectionKey = "interface",
				Label = "Rotate Snap Step (Degrees)",
				Description = "Angle step for rotate snapping.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 45f,
				MinValue = 1f,
				MaxValue = 90f,
				Step = 1f
			});

		defs.Add(CreatorSettingKeys.Interface.SnapToPartEnabled,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Interface.SnapToPartEnabled,
				SectionKey = "interface",
				Label = "Snap To Part",
				Description = "Drag selections onto the hit part surface instead of floating on a plane.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Interface.DuplicateOnDragEnabled,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Interface.DuplicateOnDragEnabled,
				SectionKey = "interface",
				Label = "Duplicate On Ctrl+Drag",
				Description = "When enabled, holding Ctrl before dragging creates a duplicate and drags the copy.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Interface.TransformOrientation,
			new SettingDef<TransformOrientationEnum>
			{
				Key = CreatorSettingKeys.Interface.TransformOrientation,
				SectionKey = "interface",
				Label = "Transform Orientation",
				Description = "Choose whether transform gizmos align to global or local axes.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = TransformOrientationEnum.Global,
				Options =
				[
					new() { Value = TransformOrientationEnum.Global, Label = "Global" },
					new() { Value = TransformOrientationEnum.Local, Label = "Local" },
				]
			});

		defs.Add(CreatorSettingKeys.Interface.SelectionPivotMode,
			new SettingDef<SelectionPivotModeEnum>
			{
				Key = CreatorSettingKeys.Interface.SelectionPivotMode,
				SectionKey = "interface",
				Label = "Selection Pivot",
				Description = "Choose whether transforms pivot around the selection center or primary selected instance.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = SelectionPivotModeEnum.Center,
				Options =
				[
					new() { Value = SelectionPivotModeEnum.Center, Label = "Center" },
					new() { Value = SelectionPivotModeEnum.PrimarySelection, Label = "Primary Selection" },
				]
			});

		// Keybinds
		defs.Add(CreatorSettingKeys.Keybinds.ToolSelect,
			new SettingDef<string>
			{
				Key = CreatorSettingKeys.Keybinds.ToolSelect,
				SectionKey = "keybinds",
				Label = "Select Tool Key",
				Description = "Keyboard key for Select tool (for example: Key1, Q, F1).",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "Key1"
			});

		defs.Add(CreatorSettingKeys.Keybinds.ToolMove,
			new SettingDef<string>
			{
				Key = CreatorSettingKeys.Keybinds.ToolMove,
				SectionKey = "keybinds",
				Label = "Move Tool Key",
				Description = "Keyboard key for Move tool.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "Key2"
			});

		defs.Add(CreatorSettingKeys.Keybinds.ToolRotate,
			new SettingDef<string>
			{
				Key = CreatorSettingKeys.Keybinds.ToolRotate,
				SectionKey = "keybinds",
				Label = "Rotate Tool Key",
				Description = "Keyboard key for Rotate tool.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "Key3"
			});

		defs.Add(CreatorSettingKeys.Keybinds.ToolScale,
			new SettingDef<string>
			{
				Key = CreatorSettingKeys.Keybinds.ToolScale,
				SectionKey = "keybinds",
				Label = "Scale Tool Key",
				Description = "Keyboard key for Scale tool.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "Key4"
			});

		defs.Add(CreatorSettingKeys.Keybinds.RotateSelection,
			new SettingDef<string>
			{
				Key = CreatorSettingKeys.Keybinds.RotateSelection,
				SectionKey = "keybinds",
				Label = "Rotate Selection 90 Degrees",
				Description = "Shortcut key to rotate selected instances by 90 degrees.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "R"
			});

		defs.Add(CreatorSettingKeys.Keybinds.TiltSelection,
			new SettingDef<string>
			{
				Key = CreatorSettingKeys.Keybinds.TiltSelection,
				SectionKey = "keybinds",
				Label = "Tilt Selection 90 Degrees",
				Description = "Shortcut key to tilt selected instances by 90 degrees.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "T"
			});

		defs.Add(CreatorSettingKeys.Keybinds.ToggleTransformOrientation,
			new SettingDef<string>
			{
				Key = CreatorSettingKeys.Keybinds.ToggleTransformOrientation,
				SectionKey = "keybinds",
				Label = "Toggle Orientation Mode",
				Description = "Shortcut to toggle transform orientation between Global and Local.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "L"
			});

		defs.Add(CreatorSettingKeys.Keybinds.TogglePivotMode,
			new SettingDef<string>
			{
				Key = CreatorSettingKeys.Keybinds.TogglePivotMode,
				SectionKey = "keybinds",
				Label = "Toggle Pivot Mode",
				Description = "Shortcut to toggle pivot mode between Center and Primary Selection.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "P"
			});

		// Backup
		defs.Add(CreatorSettingKeys.Backup.MaxBackupCount,
			new SettingDef<int>
			{
				Key = CreatorSettingKeys.Backup.MaxBackupCount,
				SectionKey = "backup",
				Label = "Max Backup Count",
				Description = "Maximum number of backups to keep.",
				ValueKind = SettingValueKind.Int,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 10,
				MinValue = 1,
				MaxValue = 50,
				Step = 1
			});

		defs.Add(CreatorSettingKeys.Backup.BackupInterval,
			new SettingDef<float>
			{
				Key = CreatorSettingKeys.Backup.BackupInterval,
				SectionKey = "backup",
				Label = "Backup Interval (minutes)",
				Description = "How often to automatically back up worlds.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 4f,
				MinValue = 1f,
				MaxValue = 60f,
				Step = 1f
			});

		// Code Editor
		defs.Add(CreatorSettingKeys.CodeEditor.ColorTheme,
			new SettingDef<CodeEditorColorThemeEnum>
			{
				Key = CreatorSettingKeys.CodeEditor.ColorTheme,
				SectionKey = "code_editor",
				Label = "Color Theme",
				Description = "Choose the built-in editor palette for code backgrounds, text, comments, strings, keywords and symbols.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = CodeEditorColorThemeEnum.BrickVerse,
				Options =
				[
					new() { Value = CodeEditorColorThemeEnum.BrickVerse, Label = "BrickVerse Dark" },
					new() { Value = CodeEditorColorThemeEnum.VisualStudioDark, Label = "Visual Studio Dark" },
					new() { Value = CodeEditorColorThemeEnum.Dracula, Label = "Dracula" },
					new() { Value = CodeEditorColorThemeEnum.Light, Label = "Light" },
					new() { Value = CodeEditorColorThemeEnum.HighContrast, Label = "High Contrast" },
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.FontSize,
			new SettingDef<int>
			{
				Key = CreatorSettingKeys.CodeEditor.FontSize,
				SectionKey = "code_editor",
				Label = "Editor Font Size",
				Description = "Text size used by the built-in code editor.",
				ValueKind = SettingValueKind.Int,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 14,
				MinValue = 10,
				MaxValue = 28,
				Step = 1
			});

		defs.Add(CreatorSettingKeys.CodeEditor.PreferredEditor,
			new SettingDef<PreferredEditorEnum>
			{
				Key = CreatorSettingKeys.CodeEditor.PreferredEditor,
				SectionKey = "code_editor",
				Label = "Preferred Editor",
				Description = "Editor to use when opening scripts.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = PreferredEditorEnum.BuiltIn,
				Options =
				[
					new() { Value = PreferredEditorEnum.BuiltIn, Label = "Built-in" },
					new() { Value = PreferredEditorEnum.SystemDefault, Label = "System Default" },
					new() { Value = PreferredEditorEnum.VSCode, Label = "VS Code" },
					new() { Value = PreferredEditorEnum.Zed, Label = "Zed" },
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.InlineSuggestions,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.InlineSuggestions,
				SectionKey = "code_editor",
				Label = "Inline Suggestions",
				Description = "Show a local, type-aware completion after the caret. Press Tab to accept it.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.CodeEditor.HoverDocumentation,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.HoverDocumentation,
				SectionKey = "code_editor",
				Label = "Hover Documentation",
				Description = "Show Luau types and API documentation when hovering over globals, methods, and properties.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.CodeEditor.FormatOnSave,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.FormatOnSave,
				SectionKey = "code_editor",
				Label = "Format on Save",
				Description = "Format Luau documents with StyLua before saving.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false
			});

		defs.Add(CreatorSettingKeys.CodeEditor.AutoSave,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.AutoSave,
				SectionKey = "code_editor",
				Label = "Auto Save",
				Description = "Automatically save built-in code editor changes after you stop typing.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.CodeEditor.IndentationMode,
			new SettingDef<IndentationModeEnum>
			{
				Key = CreatorSettingKeys.CodeEditor.IndentationMode,
				SectionKey = "code_editor",
				Label = "Indentation Mode",
				Description = "Use tabs or spaces for indentation.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = IndentationModeEnum.Tabs,
				Options =
				[
					new() { Value = IndentationModeEnum.Tabs, Label = "Tabs" },
					new() { Value = IndentationModeEnum.Spaces, Label = "Spaces" },
				],
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.IndentationSize,
			new SettingDef<int>
			{
				Key = CreatorSettingKeys.CodeEditor.IndentationSize,
				SectionKey = "code_editor",
				Label = "Indentation Size (In Spaces)",
				Description = "Number of spaces per indentation level.",
				ValueKind = SettingValueKind.Int,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 2,
				MinValue = 1,
				MaxValue = 8,
				Step = 1,
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.ShowLineNumbers,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.ShowLineNumbers,
				SectionKey = "code_editor",
				Label = "Show Line Numbers",
				Description = "Display line numbers in the editor gutter.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true,
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.HighlightCurrentLine,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.HighlightCurrentLine,
				SectionKey = "code_editor",
				Label = "Highlight Current Line",
				Description = "Highlight the line containing the caret.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true,
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.WordWrap,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.WordWrap,
				SectionKey = "code_editor",
				Label = "Word Wrap",
				Description = "Wrap long lines to the visible viewport width.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false,
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.ShowWhitespace,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.ShowWhitespace,
				SectionKey = "code_editor",
				Label = "Render Whitespace",
				Description = "Show space and tab guides while editing code.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false,
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.MinimapEnabled,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.MinimapEnabled,
				SectionKey = "code_editor",
				Label = "Minimap",
				Description = "Show a minimap overview on the editor side.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false,
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.CursorBlink,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.CodeEditor.CursorBlink,
				SectionKey = "code_editor",
				Label = "Cursor Blink",
				Description = "Enable blinking text cursor.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true,
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.CursorBlinkSpeed,
			new SettingDef<float>
			{
				Key = CreatorSettingKeys.CodeEditor.CursorBlinkSpeed,
				SectionKey = "code_editor",
				Label = "Cursor Blink Interval",
				Description = "Time in seconds between cursor blink toggles.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 0.65f,
				MinValue = 0.2f,
				MaxValue = 1.5f,
				Step = 0.05f,
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		defs.Add(CreatorSettingKeys.CodeEditor.CursorWidth,
			new SettingDef<int>
			{
				Key = CreatorSettingKeys.CodeEditor.CursorWidth,
				SectionKey = "code_editor",
				Label = "Cursor Width",
				Description = "Thickness of the text cursor.",
				ValueKind = SettingValueKind.Int,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 2,
				MinValue = 1,
				MaxValue = 5,
				Step = 1,
				Conditions = [
					new SettingCondition<PreferredEditorEnum>() {
						Target = CreatorSettingKeys.CodeEditor.PreferredEditor,
						Predicate = x => x == PreferredEditorEnum.BuiltIn
					}
				]
			});

		// Popups
		defs.Add(CreatorSettingKeys.Popups.CloseModelWarning,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.CloseModelWarning,
				SectionKey = "popups",
				Label = "Close Model Warning",
				Description = "Show warning when closing an unsaved model.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Popups.MoveFileConfirmation,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.MoveFileConfirmation,
				SectionKey = "popups",
				Label = "Move File Confirmation",
				Description = "Show confirmation when moving files.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Popups.CloseTabWarning,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.CloseTabWarning,
				SectionKey = "popups",
				Label = "Close Tab Warning",
				Description = "Show warning when closing a modified tab.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Popups.ClosePlaceWarning,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.ClosePlaceWarning,
				SectionKey = "popups",
				Label = "Close Place Warning",
				Description = "Ask for confirmation when F4 or Close Place closes the active world.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Popups.ExecutorConfirmation,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.ExecutorConfirmation,
				SectionKey = "popups",
				Label = "Executor Confirmation",
				Description = "Ask for confirmation before running Luau from the Creator Output executor.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Popups.ExecutorSuccess,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.ExecutorSuccess,
				SectionKey = "popups",
				Label = "Executor Success Message",
				Description = "Show a completion popup after Luau finishes successfully.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(CreatorSettingKeys.Popups.PrefabScriptWarning,
			new SettingDef<bool>
			{
				Key = CreatorSettingKeys.Popups.PrefabScriptWarning,
				SectionKey = "popups",
				Label = "Prefab Script Warning",
				Description = "Warn before inserting a prefab or model that contains executable scripts.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		SettingDef.ValidateAll(defs.Values);
		return defs;
	}
}
