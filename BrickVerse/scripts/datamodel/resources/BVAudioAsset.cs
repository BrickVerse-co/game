// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Shared.AssetLoaders;

namespace BrickVerse.Datamodel.Resources;

[Instantiable]
public partial class BVAudioAsset : AudioAsset
{
	private string _audioID = "0";

	[Editable, ScriptProperty]
	public string AudioID
	{
		get => _audioID;
		set
		{
			_audioID = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	public static void RegisterAsset()
	{
		RegisterType<BVAudioAsset>();
	}

	public override void LoadResource()
	{
		if (AudioID == "0") return;
		AssetLoader.Singleton.GetResource(
			new() { Type = ResourceType.Sound, ID = AudioID },
			InvokeResourceLoaded
		);
	}
}
