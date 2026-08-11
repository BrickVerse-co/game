// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0.

using BrickVerse.Attributes;
using BrickVerse.Shared.AssetLoaders;

namespace BrickVerse.Datamodel.Resources;

[Instantiable]
public partial class BVFontAsset : FontAsset
{
	private string _fontID = string.Empty;

	[Editable, ScriptProperty]
	public string FontID
	{
		get => _fontID;
		set
		{
			_fontID = value;
			QueueLoadResource();
			OnPropertyChanged();
		}
	}

	public static void RegisterAsset() => RegisterType<BVFontAsset>();

	public override void LoadResource()
	{
		if (string.IsNullOrWhiteSpace(FontID)) return;
		AssetLoader.Singleton.GetResource(
			new() { Type = ResourceType.Font, ID = FontID },
			InvokeResourceLoaded
		);
	}
}
