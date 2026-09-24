// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;

namespace BrickVerse.Creator.UI;

/// <summary>Reusable shimmer-free loading placeholder for Creator lists and panels.</summary>
public sealed partial class EditorLoadingSkeleton : VBoxContainer
{
	public EditorLoadingSkeleton(int rows = 6)
	{
		MouseFilter = MouseFilterEnum.Ignore;
		AddThemeConstantOverride("separation", 8);
		for (int i = 0; i < rows; i++)
		{
			Panel row = new() { CustomMinimumSize = new Vector2(0, i == 0 ? 38 : 30), Modulate = new Color(1, 1, 1, 0.72f - i * 0.055f) };
			row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
			{
				BgColor = new Color("252934"),
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6,
			});
			AddChild(row);
		}
	}
}
