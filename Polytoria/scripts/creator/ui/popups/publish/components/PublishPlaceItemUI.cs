// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared.AssetLoaders;

namespace Polytoria.Creator.UI.Components;

public partial class PublishPlaceItemUI : Button
{
	[Export] private TextureRect _iconRect = null!;
	[Export] private Label _placeNameLabel = null!;

	public CreatorPlaceItem Target;

	public override void _Ready()
	{
		_placeNameLabel.Text = Target.Name;

		WebAssetLoader.Singleton.GetResource(new() { URL = Target.IconUrl }, r =>
		{
			_iconRect.Texture = (Texture2D)r;
		});
	}
}
