// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Shared.AssetLoaders;
using System;

namespace BrickVerse.Datamodel.Resources;

[Instantiable]
public partial class BVImageAsset : ImageAsset
{
	private string _imageID = string.Empty;
	private ImageTypeEnum _imageType;

	[Editable, ScriptProperty]
	public string ImageID
	{
		get => _imageID;
		set
		{
			_imageID = value;
			QueueLoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageTypeEnum ImageType
	{
		get => _imageType;
		set
		{
			_imageType = value;
			QueueLoadResource();
			OnPropertyChanged();
		}
	}

	internal string? DirectImageURL { get; private set; }

	public static void RegisterAsset()
	{
		RegisterType<BVImageAsset>();
	}

	public override void LoadResource()
	{
		if (string.IsNullOrEmpty(ImageID)) { return; }
		ResourceType resourceType = ImageType switch
		{
			ImageTypeEnum.Asset => ResourceType.Texture,
			ImageTypeEnum.AssetThumbnail => ResourceType.Texture,
			ImageTypeEnum.WorldThumbnail => ResourceType.Texture,
			ImageTypeEnum.UserAvatar => ResourceType.UserBodyshot,
			ImageTypeEnum.UserAvatarHeadshot => ResourceType.UserHeadshot,
			ImageTypeEnum.GuildIcon => ResourceType.GuildIcon,
			ImageTypeEnum.GuildBanner => ResourceType.GuildBanner,
			_ => throw new NotImplementedException()
		};

		AssetLoader.Singleton.GetRawCache(
			new() { Type = resourceType, ID = ImageID },
			OnResourceLoaded
		);
	}

	private void OnResourceLoaded(CacheItem cacheItem)
	{
		DirectImageURL = cacheItem.DirectURL;
		InvokeResourceLoaded(cacheItem.Resource);
	}
}

[ScriptEnum]
public enum ImageTypeEnum
{
	Asset,
	AssetThumbnail,
	WorldThumbnail,
	UserAvatar,
	UserAvatarHeadshot,
	GuildIcon,
	GuildBanner
}
