using BrickVerse.Attributes;
using BrickVerse.Enums;
using BrickVerse.Scripting;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A developer-defined, per-user option displayed in the game's Settings menu.</summary>
[Instantiable]
public sealed partial class GameSetting : Instance
{
	private string _title = "Game Setting";
	private int _displayOrder;
	private float _minimum;
	private float _maximum = 100f;
	private float _step = 1f;

	[Editable, ScriptProperty] public string Key { get; set; } = "setting";
	[Editable, ScriptProperty] public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public string Description { get; set; } = "";
	[Editable, ScriptProperty] public string Category { get; set; } = "Game";
	[Editable, ScriptProperty] public int DisplayOrder { get => _displayOrder; set { _displayOrder = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public SettingTypeEnum SettingType { get; set; } = SettingTypeEnum.Boolean;
	[Editable, ScriptProperty] public bool Enabled { get; set; } = true;
	[Editable, ScriptProperty] public bool DefaultBoolean { get; set; }
	[Editable, ScriptProperty] public float DefaultNumber { get; set; }
	[Editable, ScriptProperty] public string DefaultText { get; set; } = "";
	[Editable, ScriptProperty] public string DefaultChoice { get; set; } = "";
	[Editable, ScriptProperty] public KeyCodeEnum DefaultKeybind { get; set; } = KeyCodeEnum.None;
	[Editable, ScriptProperty] public Color DefaultColor { get; set; } = Colors.White;
	[Editable, ScriptProperty] public string[] Choices { get; set; } = [];
	[Editable, ScriptProperty] public float Minimum { get => _minimum; set { _minimum = value; if (_maximum < value) _maximum = value; OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float Maximum { get => _maximum; set { _maximum = Mathf.Max(value, _minimum); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public float Step { get => _step; set { _step = Mathf.Max(0.001f, value); OnPropertyChanged(); } }
	[Editable, ScriptProperty] public int MaxTextLength { get; set; } = 128;

	[ScriptProperty] public BVSignal<object> Changed { get; private set; } = new();

	[ScriptMethod] public bool GetBoolean() => Root.GameSettings.GetBoolean(this);
	[ScriptMethod] public float GetNumber() => Root.GameSettings.GetNumber(this);
	[ScriptMethod] public string GetText() => Root.GameSettings.GetText(this);
	[ScriptMethod] public string GetChoice() => Root.GameSettings.GetChoice(this);
	[ScriptMethod] public KeyCodeEnum GetKeybind() => Root.GameSettings.GetKeybind(this);
	[ScriptMethod] public Color GetColor() => Root.GameSettings.GetColor(this);
	[ScriptMethod] public void SetBoolean(bool value) => Root.GameSettings.SetBoolean(this, value);
	[ScriptMethod] public void SetNumber(float value) => Root.GameSettings.SetNumber(this, value);
	[ScriptMethod] public void SetText(string value) => Root.GameSettings.SetText(this, value);
	[ScriptMethod] public void SetChoice(string value) => Root.GameSettings.SetChoice(this, value);
	[ScriptMethod] public void SetKeybind(KeyCodeEnum value) => Root.GameSettings.SetKeybind(this, value);
	[ScriptMethod] public void SetColor(Color value) => Root.GameSettings.SetColor(this, value);
	[ScriptMethod] public void Reset() => Root.GameSettings.Reset(this);

	internal void InvokeChanged(object value) => Changed.Invoke(value);

	public enum SettingTypeEnum { Boolean, Number, Text, Choice, Keybind, Color }
}
