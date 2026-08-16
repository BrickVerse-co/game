// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using System;
using System.Linq;

namespace BrickVerse.Creator.Properties;

/// <summary>Compact line-based editor for string-array DataModel properties.</summary>
public sealed partial class StringArrayProperty : VBoxContainer, IProperty<string[]>
{
	private TextEdit _editor = null!;
	private string[] _value = [];
	private bool _refreshing;

	public string[] Value { get => _value; set { _value = value ?? []; Refresh(); } }
	public Type PropertyType { get; set; } = typeof(string[]);
	public event Action<object?>? ValueChanged;
	public object? GetValue() => Value;
	public void SetValue(object? value) { if (value is string[] strings) Value = strings; }

	public override void _Ready()
	{
		_editor = GetNode<TextEdit>("Editor");
		_editor.TextChanged += OnTextChanged;
		Refresh();
		base._Ready();
	}

	public override void _ExitTree()
	{
		if (_editor != null) _editor.TextChanged -= OnTextChanged;
		base._ExitTree();
	}

	public void Refresh()
	{
		if (_editor == null) return;
		_refreshing = true; _editor.Text = string.Join('\n', _value); _refreshing = false;
	}

	private void OnTextChanged()
	{
		if (_refreshing) return;
		_value = _editor.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
		ValueChanged?.Invoke(_value);
	}
}
