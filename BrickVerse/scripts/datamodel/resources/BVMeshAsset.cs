// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Shared.AssetLoaders;

namespace BrickVerse.Datamodel.Resources;

[Instantiable]
public partial class BVMeshAsset : MeshAsset
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
		RegisterType<BVMeshAsset>();
	}

	public override void LoadResource()
	{
		if (string.IsNullOrWhiteSpace(AssetID)) return;

		AssetLoader.Singleton.GetResource(
			new() { Type = ResourceType.Mesh, ID = AssetID },
			InvokeResourceLoaded
		);
	}
}
