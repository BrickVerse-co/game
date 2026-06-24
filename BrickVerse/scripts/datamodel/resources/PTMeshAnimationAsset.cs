// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Shared.AssetLoaders;

namespace BrickVerse.Datamodel.Resources;

[Instantiable]
public partial class PTMeshAnimationAsset : MeshAnimationAsset
{
	private string _assetID;

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
		RegisterType<PTMeshAnimationAsset>();
	}

	public override void LoadResource()
	{
		AssetLoader.Singleton.GetResource(
			new() { Type = ResourceType.Mesh, ID = AssetID },
			OnMeshResourceLoaded
		);
	}

	private void OnMeshResourceLoaded(Resource res)
	{
		if (res is PackedScene scene)
		{
			Node obj = scene.Instantiate<Node>();
			AnimationPlayer? animPlay = obj.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

			if (animPlay != null)
			{
				var libList = animPlay.GetAnimationLibraryList();
				if (libList.Count != 0)
				{
					AnimationLibrary lib = animPlay.GetAnimationLibrary(libList[0]);
					AnimationLibrary flib = (AnimationLibrary)lib.DuplicateDeep();
					InvokeResourceLoaded(flib);
				}
			}

			obj.Free();
			obj.Dispose();
		}
	}
}
