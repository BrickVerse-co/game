// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Resources;

namespace BrickVerse.Creator.Properties;

public sealed partial class BVImageAssetSubview : Control, IPropertySubview
{
	public NetworkedObject TargetObject { get; set; } = null!;
	private BVImageAsset _baseAsset = null!;
	private TextureRect _rect = null!;

	public override void _Ready()
	{
		_baseAsset = (BVImageAsset)TargetObject;
		_rect = GetNode<TextureRect>("Alpha/Texture");

		if (_baseAsset.Resource != null)
		{
			OnResourceLoaded(_baseAsset.Resource);
		}
		_baseAsset.ResourceLoaded += OnResourceLoaded;
	}

	private void OnResourceLoaded(Resource resource)
	{
		_rect.Texture = (Texture2D)resource;
	}
}
