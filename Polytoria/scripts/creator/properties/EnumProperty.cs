// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using System;
using System.Reflection;

namespace Polytoria.Creator.Properties;

public sealed partial class EnumProperty : OptionButton, IProperty<int>
{
	private int _value;

	public int Value
	{
		get => _value;
		set
		{
			_value = value;
			Refresh();
		}
	}

	public Type PropertyType { get; set; } = null!;

	public event Action<object?>? ValueChanged;

	private PopupMenu _popup = null!;

	public object? GetValue()
	{
		return Value;
	}

	public void SetValue(object? value)
	{
		if (value == null) return;
		Value = _popup.GetItemIndex((int)value);
	}

	public void Refresh()
	{
		Selected = Value;
	}

	public override void _Ready()
	{
		CreatorEnumOptionsAttribute? options = PropertyType.GetCustomAttribute<CreatorEnumOptionsAttribute>();
		string[] enums = Enum.GetNames(PropertyType);

		_popup = GetPopup();

		if (options != null)
		{
			switch (options.SortOption)
			{
				case EnumSortOption.Alphabetical:
					Array.Sort(enums);
					break;
			}
		}

		foreach (string name in enums)
		{
			int id = (int)Enum.Parse(PropertyType, name);
			AddItem(name, id);
		}

		Refresh();

		_popup.IdPressed += value =>
		{
			ValueChanged?.Invoke((int)value);
		};
	}
}
