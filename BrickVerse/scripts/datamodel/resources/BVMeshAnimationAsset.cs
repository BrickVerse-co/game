// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Shared.AssetLoaders;

namespace BrickVerse.Datamodel.Resources;

[Instantiable]
public partial class BVMeshAnimationAsset : MeshAnimationAsset
{
	private string _assetID = string.Empty;

	[Editable, ScriptProperty]
	public string AssetID
	{
		get => _assetID;
		set
		{
			_assetID = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	public static void RegisterAsset()
	{
		RegisterType<BVMeshAnimationAsset>();
	}

	public override void LoadResource()
	{
		AssetLoader.Singleton.GetResource(
			new() { Type = ResourceType.Animation, ID = AssetID },
			resource =>
			{
				if (resource is AnimationLibrary library)
					InvokeResourceLoaded((AnimationLibrary)library.DuplicateDeep());
			}
		);
	}
}
