// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Formats;
using System;
using System.Collections.Generic;
using static Polytoria.Datamodel.Creator.CreatorAddons;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class AddonPermRequestPopup : PopupWindowBase
{
	private static readonly Dictionary<AddonPermissionEnum, string> _permToText = new()
	{
		[AddonPermissionEnum.IORead] = "Read access to the project folder",
		[AddonPermissionEnum.IOWrite] = "Write access to the project folder",
	};
	[Export] private Label _titleLabel = null!;
	[Export] private Label _permissionLabel = null!;
	[Export] private Button _allowButton = null!;
	[Export] private Button _declineButton = null!;

	public AddonPermissionEnum[] RequestedPerms = [];
	public PackedFormat.AddonData AddonData;

	public event Action<bool>? Responded;

	public override void _Ready()
	{
		string permTxt = "";
		foreach (var item in RequestedPerms)
		{
			permTxt += "- " + _permToText[item] + "\n";
		}
		_permissionLabel.Text = permTxt;
		_titleLabel.Text = $"{AddonData.Metadata.Name} is asking for permission";

		_allowButton.Pressed += OnAllow;
		_declineButton.Pressed += OnDecline;
		CloseRequested += OnDecline;

		base._Ready();
	}

	public override void _ExitTree()
	{
		_allowButton.Pressed -= OnAllow;
		_declineButton.Pressed -= OnDecline;
		CloseRequested -= OnDecline;
		base._ExitTree();
	}

	private void OnAllow()
	{
		Responded?.Invoke(true);
		QueueFree();
	}

	private void OnDecline()
	{
		Responded?.Invoke(false);
		QueueFree();
	}
}
