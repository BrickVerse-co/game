// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using static Polytoria.Formats.PolyFormat;

namespace Polytoria.Creator;

public partial class CreatorClipboard : Node
{
	private const string OldClipboardPrefix = "POLYTORIA_CLIPBOARD:";
	public CreatorService Service = null!;
	private PolyRootData[]? _clipboard = null;
	public object? PropertyClipboard { get; set; }

	public async Task SetClipboard(Instance[] i)
	{
		List<PolyRootData> datas = [];
		foreach (Instance item in i)
		{
			datas.Add(SaveModel(item));
		}
		_clipboard = [.. datas];
	}

	public async Task<Instance[]?> GetClipboard()
	{
		if (World.Current == null) return null;
		if (_clipboard == null) return null;
		List<Instance> instances = [];
		foreach (PolyRootData item in _clipboard)
		{
			Instance? i = LoadModelFromRootData(World.Current, item, globalLoadContext: new() { AssignModelRoot = false });
			if (i == null) continue;
			foreach (var item1 in i.GetChildren())
			{
				PT.Print("Copied instance child: ", item1.Name);
			}
			instances.Add(i);
		}
		return [.. instances];
	}

	public async Task SetClipboardToSelected()
	{
		if (World.Current == null) return;
		await SetClipboard([.. World.Current.CreatorContext.Selections.SelectedInstances]);
		string oldClipboard = DisplayServer.ClipboardGet();

		if (oldClipboard.StartsWith(OldClipboardPrefix))
		{
			DisplayServer.ClipboardSet("");
		}
	}

	public async Task PasteClipboard(bool pasteInto = false)
	{
		if (World.Current == null) return;
		World root = World.Current;
		CreatorSelections selections = root.CreatorContext.Selections;

		// Load old creator base64 clipboard
		string oldClipboard = DisplayServer.ClipboardGet();

		if (oldClipboard.StartsWith(OldClipboardPrefix))
		{
			string oldXML = DecompressLegacyString(oldClipboard.Replace(OldClipboardPrefix, ""));

			Instance? i = await DatamodelLoader.LoadModelBytes(root, oldXML.ToUtf8Buffer(), root.Environment);

			if (i != null)
			{
				selections.SelectOnly(i);
				i.DetachModel();
			}

			return;
		}

		List<Instance> news = [];

		Instance[]? instances = await GetClipboard();
		if (instances != null)
		{
			Instance parentTo;

			if (selections.SelectedInstances.Count != 0)
			{
				if (pasteInto)
				{
					parentTo = selections.SelectedInstances[0];
				}
				else
				{
					parentTo = selections.SelectedInstances[0].Parent ?? root.Environment;
				}
			}
			else
			{
				parentTo = root.Environment;
			}

			foreach (Instance item in instances)
			{
				Instance i = (Instance)item.Clone(parentTo);
				news.Add(i);
				item.Delete();
			}

			selections.DeselectAll();
			foreach (Instance item in news)
			{
				selections.Select(item);
			}
		}
	}

	private static string DecompressLegacyString(string compressedText)
	{
		try
		{
			byte[] compressedBytes = Convert.FromBase64String(compressedText);

			using MemoryStream memoryStream = new(compressedBytes);
			using GZipStream gzipStream = new(memoryStream, CompressionMode.Decompress);
			using MemoryStream resultStream = new();
			gzipStream.CopyTo(resultStream);
			return Encoding.UTF8.GetString(resultStream.ToArray());
		}
		catch (Exception ex)
		{
			PT.PrintErr($"Error decompressing clipboard data: {ex}");
			throw;
		}
	}
}
