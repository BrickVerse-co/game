// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>A selectable branch from a DialogueLine to another line.</summary>
[Instantiable]
public sealed partial class DialogueChoice : Instance
{
	[Editable, ScriptProperty] public string Text { get; set; } = "Choice";
	[Editable, ScriptProperty] public DialogueLine? NextLine { get; set; }
	[Editable, ScriptProperty, DefaultValue(true)] public bool Enabled { get; set; } = true;
	[Editable, ScriptProperty, DefaultValue(0)] public int SortOrder { get; set; }
	[ScriptProperty] public BVSignal Selected { get; private set; } = new();
	internal void InvokeSelected() => Selected.Invoke();
}
