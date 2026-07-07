// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Team : Instance
{
	private string _displayName = "";
	private Color _color = new(1, 0, 0);

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
}
