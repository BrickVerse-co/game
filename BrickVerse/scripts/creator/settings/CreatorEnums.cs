// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;

namespace BrickVerse.Creator.Settings;

[ScriptEnum("PreferredEditor", IsCreatorOnly = true)]
public enum PreferredEditorEnum
{
	BuiltIn,
	SystemDefault,
	VSCode,
	Zed
}

[ScriptEnum("IndentationMode", IsCreatorOnly = true)]
public enum IndentationModeEnum
{
	Tabs,
	Spaces
}

[ScriptEnum("CreatorThemeMode", IsCreatorOnly = true)]
public enum CreatorThemeModeEnum
{
	Dark,
	Light
}

[ScriptEnum("TransformOrientation", IsCreatorOnly = true)]
public enum TransformOrientationEnum
{
	Global,
	Local
}

[ScriptEnum("SelectionPivotMode", IsCreatorOnly = true)]
public enum SelectionPivotModeEnum
{
	Center,
	PrimarySelection
}
