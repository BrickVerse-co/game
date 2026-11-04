// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Linq;
using BrickVerse.Attributes;

namespace BrickVerse.Datamodel;

/// <summary>A speaker line and its child DialogueChoice branches.</summary>
[Instantiable]
public sealed partial class DialogueLine : Instance
{
	[Editable, ScriptProperty] public string Speaker { get; set; } = "";
	[Editable, ScriptProperty] public string Text { get; set; } = "";
	[Editable, ScriptProperty] public string Portrait { get; set; } = "";
	[Editable, ScriptProperty, DefaultValue(false)] public bool AutoAdvance { get; set; }
	[Editable, ScriptProperty, DefaultValue(0d)] public double AutoAdvanceDelay { get; set; }
	[Editable, ScriptProperty] public DialogueLine? NextLine { get; set; }
	[ScriptMethod] public DialogueChoice[] GetChoices() => [.. GetChildren().OfType<DialogueChoice>().Where(c => c.Enabled).OrderBy(c => c.SortOrder)];
}
