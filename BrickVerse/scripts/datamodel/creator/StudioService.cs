// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.IO;
using System.Threading.Tasks;
using BrickVerse.Attributes;
using BrickVerse.Creator;
using BrickVerse.Creator.UI;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Datamodel.Creator;

[Static("StudioService"), ExplorerExclude, SaveIgnore]
public sealed partial class StudioService : Instance
{
	private static readonly string[] DefaultFilters =
	[
		"*.png, *.jpg, *.jpeg, *.webp, *.svg ; Images",
		"*.ogg, *.wav, *.mp3 ; Audio",
		"*.bvxw, *.bvworld, *.bvxm, *.bvmodel, *.model ; BrickVerse files",
		"*.lua, *.luau, *.json, *.txt ; Text files",
	];

	[ScriptProperty] public bool IsStudio => Root.SessionType == World.SessionTypeEnum.Creator;

	[ScriptMethod(Permissions = ScriptPermissionFlags.IORead)]
	public async Task<TemporaryFile?> PromptImportFile(string[]? filters = null)
	{
		if (!IsStudio || CreatorService.Interface == null)
			throw new InvalidOperationException("PromptImportFile is only available in a Creator studio session.");

		TaskCompletionSource<string?> selection = new(TaskCreationOptions.RunContinuationsAsynchronously);
		CreatorService.Interface.PromptFileSelect(
			new FileSelectPromptPayload
			{
				Title = "Import file",
				DialogMode = DisplayServer.FileDialogMode.OpenFile,
				Filters = filters is { Length: > 0 } ? filters : DefaultFilters,
			},
			paths => selection.TrySetResult(paths.Length > 0 ? paths[0] : null),
			() => selection.TrySetResult(null)
		);

		string? selectedPath = await selection.Task;
		if (string.IsNullOrWhiteSpace(selectedPath)) return null;
		(string temporaryPath, string temporaryId) = Root.IO.ImportTemporaryFile(selectedPath);
		FileInfo info = new(selectedPath);
		TemporaryFile result = Globals.LoadInstance<TemporaryFile>(Root);
		result.NameOverride = info.Name;
		result.TemporaryPath = temporaryPath;
		result.TemporaryId = temporaryId;
		result.FileName = info.Name;
		result.Extension = info.Extension.TrimStart('.').ToLowerInvariant();
		result.Size = info.Length;
		result.NetworkParent = Root.TemporaryContainer;
		return result;
	}
}
