// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Schemas.API;
using Godot;

namespace BrickVerse.Creator.UI.Components;

public partial class PublishAsPlaceCardUI : Button
{
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Label _badgeLabel = null!;
	[Export] private Label _titleLabel = null!;
	[Export] private Label _metaLabel = null!;
	[Export] private Label _descriptionLabel = null!;

	public CreatorPlaceItem? Target { get; set; }
	public bool IsNewWorld { get; set; }
	public string TitleText { get; set; } = "";
	public string MetaText { get; set; } = "";
	public string DescriptionText { get; set; } = "";

	public override void _Ready()
	{
		base._Ready();
		RefreshCard();
	}

	public void RefreshCard()
	{
		_titleLabel.Text = string.IsNullOrWhiteSpace(TitleText)
			? (IsNewWorld ? "Create new experience" : "Untitled world")
			: TitleText;

		_metaLabel.Text = MetaText;
		_descriptionLabel.Text = DescriptionText;
		_badgeLabel.Text = IsNewWorld ? "NEW" : "OVR";

		if (IsNewWorld)
		{
			_thumbnailRect.Texture = GD.Load<Texture2D>(
				"res://assets/textures/creator/wizard/new-place/new_place.png"
			);
			return;
		}

		if (Target == null)
		{
			_thumbnailRect.Texture = GD.Load<Texture2D>("res://assets/textures/ui-icons/replace.svg");
			return;
		}

		_thumbnailRect.Texture = GD.Load<Texture2D>("res://assets/textures/ui-icons/replace.svg");
	}
}
