// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;
using System.Collections.Generic;

namespace BrickVerse.Datamodel.Services;

[Static("Assets")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class AssetsService : Instance
{
	public readonly Dictionary<string, FileLinkAsset> FileLinks = [];

	public override void PreDelete()
	{
		FileLinks.Clear();
		base.PreDelete();
	}

	[ScriptMethod, Obsolete("Use .New static instead")]
	public BaseAsset? NewAsset(string assetClassName)
	{
		NetworkedObject? obj = NewInternal(assetClassName, Root);
		if (obj is not BaseAsset)
		{
			obj?.Destroy();
			return null;
		}
		return (BaseAsset)obj;
	}

	[ScriptMethod, Obsolete("Use .New static instead")]
	public PTImageAsset? NewPTImage(string imgID)
	{
		PTImageAsset ptImg = New<PTImageAsset>();
		ptImg.ImageID = imgID;
		return ptImg;
	}

	[ScriptMethod, Obsolete("Use .New static instead")]
	public PTAudioAsset? NewPTAudio(string audioID)
	{
		PTAudioAsset ptAudio = New<PTAudioAsset>();
		ptAudio.AudioID = audioID;
		return ptAudio;
	}

	[ScriptMethod, Obsolete("Use .New static instead")]
	public PTMeshAsset? NewPTMesh(string assetID)
	{
		PTMeshAsset ptMesh = New<PTMeshAsset>();
		ptMesh.AssetID = assetID;
		return ptMesh;
	}

	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IORead)]
	public FileLinkAsset GetFileLinkByPath(string path)
	{
		if (FileLinks.TryGetValue(Root.IO.GetIDFromPath(path), out FileLinkAsset? link)) return link;
		FileLinkAsset fl = new()
		{
			Root = Root,
			LinkedID = Root.IO.GetIDFromPath(path),
		};
		return fl;
	}


	[ScriptMethod(Permissions = Scripting.ScriptPermissionFlags.IORead)]
	public FileLinkAsset GetFileLinkByID(string id)
	{
		if (FileLinks.TryGetValue(id, out FileLinkAsset? link)) return link;
		FileLinkAsset fl = new()
		{
			Root = Root,
			LinkedID = id,
		};
		return fl;
	}

	internal void RegisterFileLink(FileLinkAsset fl)
	{
		FileLinks.TryAdd(fl.LinkedID, fl);
	}

	internal void DeregisterFileLink(FileLinkAsset fl)
	{
		FileLinks.Remove(fl.LinkedID);
	}
}
