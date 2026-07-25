// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace BrickVerse.Creator.UI;

/// <summary>
/// UI metadata emitted for one completed Forge tool operation.
/// Kept in a standalone file so ForgeTab, ForgeChatClient, and ForgeTooling
/// share the same contract without depending on JSON source-generation output.
/// </summary>
public sealed class ForgeToolEvent
{
    public string ToolName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? InstancePath { get; set; }
    public string? Diff { get; set; }
    public bool CanRollback { get; set; }
}

/// <summary>Arguments accepted by the get_script_diff Forge tool.</summary>
public sealed class ForgeScriptDiffArgs
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}
