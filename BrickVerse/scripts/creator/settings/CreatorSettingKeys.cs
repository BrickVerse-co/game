// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BrickVerse.Creator.Settings;

public static class CreatorSettingKeys
{
	public static class Creator
	{
		public const string OpenWebAfterPublish = "creator.open_web_after_publish";
		public const string DetailedRichPresence = "creator.detailed_rich_presence";
		public const string PromptForWorldProjectLocation = "creator.prompt_for_world_project_location";
		public const string ShowInteractiveTutorial = "creator.show_interactive_tutorial";
		public const string ShowWhatsNew = "creator.show_whats_new";
		public const string ShowUpdateNotifications = "creator.show_update_notifications";
		public const string PlayTestPresentation = "creator.play_test_presentation";
	}

	public static class Interface
	{
		public const string UiScale = "interface.ui_scale";
		public const string ThemeMode = "interface.theme_mode";
		public const string MoveSnapEnabled = "interface.move_snap_enabled";
		public const string MoveSnapStep = "interface.move_snap_step";
		public const string RotateSnapEnabled = "interface.rotate_snap_enabled";
		public const string RotateSnapStep = "interface.rotate_snap_step";
		public const string SnapToPartEnabled = "interface.snap_to_part_enabled";
		public const string DuplicateOnDragEnabled = "interface.duplicate_on_drag_enabled";
		public const string TransformOrientation = "interface.transform_orientation";
		public const string SelectionPivotMode = "interface.selection_pivot_mode";
	}

	public static class Keybinds
	{
		public const string ToolSelect = "keybinds.tool_select";
		public const string ToolMove = "keybinds.tool_move";
		public const string ToolRotate = "keybinds.tool_rotate";
		public const string ToolScale = "keybinds.tool_scale";
		public const string RotateSelection = "keybinds.rotate_selection";
		public const string TiltSelection = "keybinds.tilt_selection";
		public const string ToggleTransformOrientation = "keybinds.toggle_transform_orientation";
		public const string TogglePivotMode = "keybinds.toggle_pivot_mode";
	}

	public static class Backup
	{
		public const string MaxBackupCount = "backup.max_backup_count";
		public const string BackupInterval = "backup.backup_interval";
	}

	public static class CodeEditor
	{
		public const string PreferredEditor = "code_editor.preferred_editor";
		public const string IndentationMode = "code_editor.indentation_mode";
		public const string IndentationSize = "code_editor.indentation_size";
		public const string ShowLineNumbers = "code_editor.show_line_numbers";
		public const string HighlightCurrentLine = "code_editor.highlight_current_line";
		public const string WordWrap = "code_editor.word_wrap";
		public const string ShowWhitespace = "code_editor.show_whitespace";
		public const string MinimapEnabled = "code_editor.minimap_enabled";
		public const string CursorBlink = "code_editor.cursor_blink";
		public const string CursorBlinkSpeed = "code_editor.cursor_blink_speed";
		public const string CursorWidth = "code_editor.cursor_width";
		public const string ColorTheme = "code_editor.color_theme";
		public const string FontSize = "code_editor.font_size";
		public const string InlineSuggestions = "code_editor.inline_suggestions";
		public const string HoverDocumentation = "code_editor.hover_documentation";
		public const string FormatOnSave = "code_editor.format_on_save";
	}

	public static class Popups
	{
		public const string CloseModelWarning = "popups.close_model_warning";
		public const string MoveFileConfirmation = "popups.move_file_confirmation";
		public const string CloseTabWarning = "popups.close_tab_warning";
		public const string ExecutorConfirmation = "popups.executor_confirmation";
		public const string ExecutorSuccess = "popups.executor_success";
		public const string PrefabScriptWarning = "popups.prefab_script_warning";
	}
}
