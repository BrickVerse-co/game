// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Creator.Utils;
using BrickVerse.Formats;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class AnimationPublishPopup : PopupWindowBase
{
	private sealed record GuildChoice(string Id, string Name);
	private readonly BVAnimationClip _clip;
	private readonly List<GuildChoice> _guilds = [];
	private readonly List<long> _assetIds = [0];
	private LineEdit _name = null!;
	private TextEdit _description = null!;
	private Button _personal = null!;
	private Button _guild = null!;
	private OptionButton _guildChoice = null!;
	private OptionButton _publishTarget = null!;
	private Label _error = null!;
	private Button _publish = null!;

	public AnimationPublishPopup(BVAnimationClip clip)
	{
		_clip = clip;
		Title = "Publish Animation";
		Size = new Vector2I(600, 510);
		MinSize = new Vector2I(520, 460);
		Transient = true;
		Exclusive = true;
	}

	public override async void _Ready()
	{
		base._Ready();
		BuildUi();
		try
		{
			CreatorGuildItem[] guilds = await CreatorAPI.GetUserGuilds(limitToEditable: true);
			foreach (CreatorGuildItem guild in guilds)
			{
				_guilds.Add(new(guild.Id, guild.Name));
				_guildChoice.AddItem(guild.Name);
			}

			CreatorAssetItem[] assets = await CreatorAPI.GetCreatorAssets(PublishPopup.PublishTypeEnum.Animation);
			foreach (CreatorAssetItem asset in assets)
			{
				_assetIds.Add(asset.Id);
				_publishTarget.AddItem($"Update: {asset.Name}");
			}
		}
		catch (Exception ex)
		{
			BV.PrintErr("Animation publish options failed to load: ", ex.Message);
		}
		RefreshOwnership();
	}

	private void BuildUi()
	{
		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		AddChild(margin);
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		VBoxContainer root = new();
		root.AddThemeConstantOverride("separation", 10);
		margin.AddChild(root);
		Label heading = new() { Text = "Publish animation" };
		heading.AddThemeFontSizeOverride("font_size", 22);
		root.AddChild(heading);
		root.AddChild(new Label { Text = "Create a new animation asset or publish over one you own." });

		root.AddChild(new Label { Text = "Publish target" });
		_publishTarget = new OptionButton();
		_publishTarget.AddItem("Create new animation");
		_publishTarget.ItemSelected += _ => RefreshOwnership();
		root.AddChild(_publishTarget);

		root.AddChild(new Label { Text = "Name" });
		_name = new LineEdit { Text = _clip.Name, MaxLength = 100 };
		root.AddChild(_name);
		root.AddChild(new Label { Text = "Description" });
		_description = new TextEdit
		{
			PlaceholderText = "Describe how this animation should be used...",
			CustomMinimumSize = new Vector2(0, 90),
		};
		root.AddChild(_description);

		root.AddChild(new Label { Text = "Owner" });
		HBoxContainer ownerRow = new();
		ButtonGroup group = new();
		_personal = new()
		{
			Text = "Personal",
			ToggleMode = true,
			ButtonPressed = true,
			ButtonGroup = group,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_guild = new()
		{
			Text = "Guild",
			ToggleMode = true,
			ButtonGroup = group,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		ownerRow.AddChild(_personal);
		ownerRow.AddChild(_guild);
		root.AddChild(ownerRow);
		_guildChoice = new OptionButton { Visible = false };
		root.AddChild(_guildChoice);
		_personal.Pressed += RefreshOwnership;
		_guild.Pressed += RefreshOwnership;

		_error = new() { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_error.AddThemeColorOverride("font_color", new Color("ff6969"));
		root.AddChild(_error);
		Control spacer = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		root.AddChild(spacer);
		HBoxContainer footer = new();
		footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		Button cancel = new() { Text = "Cancel" };
		cancel.Pressed += QueueFree;
		footer.AddChild(cancel);
		_publish = new() { Text = "Publish Animation" };
		_publish.Pressed += Submit;
		footer.AddChild(_publish);
		root.AddChild(footer);
	}

	private void RefreshOwnership()
	{
		bool isUpdate = _publishTarget.Selected > 0;
		bool guild = _guild.ButtonPressed;
		_personal.Disabled = isUpdate;
		_guild.Disabled = isUpdate;
		_guildChoice.Visible = !isUpdate && guild;
		_guildChoice.Disabled = _guilds.Count == 0;
		if (!isUpdate && guild && _guilds.Count == 0)
			ShowError("You do not have animation upload permission in any guild.");
		else
			HideError();
	}

	private async void Submit()
	{
		string name = _name.Text.Trim();
		string description = _description.Text.Trim();
		if (name.Length == 0) { ShowError("Name is required."); return; }
		if (description.Length > 500) { ShowError("Description must be 500 characters or less."); return; }
		bool isUpdate = _publishTarget.Selected > 0;
		bool toGuild = !isUpdate && _guild.ButtonPressed;
		if (toGuild && (_guildChoice.Selected < 0 || _guildChoice.Selected >= _guilds.Count))
		{
			ShowError("Select a valid guild.");
			return;
		}

		SetBusy(true);
		try
		{
			long assetId = _assetIds.ElementAtOrDefault(_publishTarget.Selected);
			string ownerId = toGuild ? _guilds[_guildChoice.Selected].Id : CreatorAPI.UserID;
			await CreatorAPI.UploadAsset(
				BVAnimationFormat.Write(_clip),
				assetId,
				"ANIMATION",
				name + ".bvanim",
				name,
				description,
				ownerId,
				toGuild ? "GUILD" : "USER",
				BVAnimationFormat.MimeType
			);
			QueueFree();
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
			SetBusy(false);
		}
	}

	private void SetBusy(bool busy)
	{
		_publish.Disabled = busy;
		_name.Editable = !busy;
		_description.Editable = !busy;
		_publishTarget.Disabled = busy;
		_personal.Disabled = busy || _publishTarget.Selected > 0;
		_guild.Disabled = busy || _publishTarget.Selected > 0;
		_guildChoice.Disabled = busy || _guilds.Count == 0;
		_publish.Text = busy ? "Publishing..." : "Publish Animation";
	}

	private void ShowError(string message) { _error.Text = message; _error.Visible = true; }
	private void HideError() { _error.Text = ""; _error.Visible = false; }
}
