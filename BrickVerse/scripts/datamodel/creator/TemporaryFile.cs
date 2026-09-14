// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;

namespace BrickVerse.Datamodel.Creator;

[ExplorerExclude, SaveIgnore]
public sealed partial class TemporaryFile : Instance
{
	internal string TemporaryPath { get; set; } = string.Empty;
	internal string TemporaryId { get; set; } = string.Empty;

	[ScriptProperty] public string FileName { get; internal set; } = string.Empty;
	[ScriptProperty] public string Extension { get; internal set; } = string.Empty;
	[ScriptProperty] public long Size { get; internal set; }

	[ScriptMethod]
	public string GetTemporaryId() => TemporaryId;

	public override void PreDelete()
	{
		if (!string.IsNullOrWhiteSpace(TemporaryId))
			Root.IO.DeleteTemporaryFile(TemporaryPath, TemporaryId);
		base.PreDelete();
	}
}
