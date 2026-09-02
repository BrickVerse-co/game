// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System.Collections.Generic;

namespace BrickVerse.Datamodel;

[Instantiable]
public partial class Team : Instance
{
	private string _displayName = "";
	private Color _color = new(1, 0, 0);
	private bool _isDefault;

	[ScriptProperty]
	public BVSignal<Player> PlayerJoined { get; private set; } = new();

	[ScriptProperty]
	public BVSignal<Player> PlayerLeft { get; private set; } = new();

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool IsDefault
	{
		get => _isDefault;
		set
		{
			_isDefault = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue("")]
	public string DisplayName
	{
		get => _displayName;
		set
		{
			_displayName = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color Color
	{
		get => _color;
		set
		{
			_color = value;
			OnPropertyChanged();
		}
	}

	[ScriptMethod]
	public string GetDisplayName()
	{
		return _displayName == string.Empty ? Name : _displayName;
	}

	[ScriptMethod]
	public Player[] GetPlayers()
	{
		List<Player> plr = [];
		foreach (var item in Root.Players.GetPlayers())
		{
			if (item.Team == this)
			{
				plr.Add(item);
			}
		}
		return [.. plr];
	}

	internal void InvokePlayerJoined(Player player) => PlayerJoined.Invoke(player);

	internal void InvokePlayerLeft(Player player) => PlayerLeft.Invoke(player);
}
