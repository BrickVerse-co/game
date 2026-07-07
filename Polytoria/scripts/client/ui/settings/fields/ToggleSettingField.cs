using Godot;
using Polytoria.Client.Settings;
using Polytoria.Shared.Settings;

namespace Polytoria.Client.UI;

public sealed partial class ToggleSettingField : CheckButton
{
	public SettingDef Definition = null!;
	private System.Action<SettingChangedEvent>? _changedHandler;

	public override void _Ready()
	{
		Flat = true;
		Text = string.Empty;
		MouseDefaultCursorShape = CursorShape.PointingHand;

		AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
		AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
		AddThemeStyleboxOverride("hover_pressed", new StyleBoxEmpty());
		AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());

		ButtonPressed = ClientSettingsService.Instance.Get<bool>(Definition.Key);

		Toggled += (value) =>
		{
			ClientSettingsService.Instance.Set(Definition.Key, value);
		};

		_changedHandler = e =>
		{
			if (e.Key == Definition.Key)
			{
				SetPressedNoSignal((bool)e.NewValue!);
			}
		};
		ClientSettingsService.Instance.Changed += _changedHandler;

		base._Ready();
	}

	public override void _ExitTree()
	{
		if (_changedHandler != null && ClientSettingsService.Instance != null)
		{
			ClientSettingsService.Instance.Changed -= _changedHandler;
			_changedHandler = null;
		}

		base._ExitTree();
	}
}
