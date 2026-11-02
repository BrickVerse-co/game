using BrickVerse.Shared.Settings;
using Godot;

namespace BrickVerse.Client.UI;

public static class SettingFieldFactory
{
	public static Control Create(SettingDef def)
	{
		return def.ControlKind switch
		{
			SettingControlKind.Toggle => new ToggleSettingField { Definition = def },
			SettingControlKind.Slider => new SliderSettingField { Definition = def },
			SettingControlKind.Dropdown => new DropdownSettingField { Definition = def },
			_ => new Label { Text = "Unsupported setting type!" },
		};
	}
}
