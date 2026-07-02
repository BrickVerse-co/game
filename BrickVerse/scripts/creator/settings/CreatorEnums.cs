// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BrickVerse.Creator.Settings;

public enum PreferredEditorEnum
{
	BuiltIn,
	SystemDefault,
	VSCode,
	Zed
}

public enum IndentationModeEnum
{
	Tabs,
	Spaces
}

public enum CreatorThemeModeEnum
{
	Dark,
	Light
}

public enum TransformOrientationEnum
{
	Global,
	Local
}

public enum SelectionPivotModeEnum
{
	Center,
	PrimarySelection
}
