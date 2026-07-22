// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;

namespace BrickVerse.Datamodel;

[Instantiable]
public partial class Clothing : Instance
{
	public enum ClothingType
	{
		TShirt,
		Shirt,
		Pants,
	}

	private ImageAsset? _asset;
	private BrickversianModel? _target;
	private ClothingType _type = ClothingType.Shirt;

	internal Texture2D? ClothTexture;

	[Editable, ScriptProperty]
	public ClothingType Type
	{
		get => _type;
		set
		{
			if (_type == value)
				return;

			_type = value;
			NotifyCharacter();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageAsset? Image
	{
		get => _asset;
		set
		{
			if (_asset == value)
				return;

			if (_asset != null)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.UnlinkFrom(this);
			}

			_asset = value;
			ClothTexture = null;

			if (_asset != null)
			{
				_asset.LinkTo(this);
				_asset.ResourceLoaded += OnResourceLoaded;

				if (_asset.IsResourceLoaded && _asset.Resource != null)
					OnResourceLoaded(_asset.Resource);
				else
					_asset.QueueLoadResource();
			}

			NotifyCharacter();
			OnPropertyChanged();
		}
	}

	private void OnResourceLoaded(Resource resource)
	{
		if (resource is not Texture2D texture)
			return;

		ClothTexture = texture;
		NotifyCharacter();
	}

	private void NotifyCharacter()
	{
		_target?.QueueRenderCloth();
	}

	public override void EnterTree()
	{
		base.EnterTree();

		if (Parent is BrickversianModel character)
		{
			_target = character;
			NotifyCharacter();
		}
	}

	public override void PostIndexMove()
	{
		NotifyCharacter();
		base.PostIndexMove();
	}

	public override void ExitTree()
	{
		NotifyCharacter();
		_target = null;

		base.ExitTree();
	}
}