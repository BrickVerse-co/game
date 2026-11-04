// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>A runtime dialogue graph built from DialogueLine and DialogueChoice instances.</summary>
[Instantiable]
public sealed partial class Dialogue : Instance
{
	[Editable, ScriptProperty] public DialogueLine? StartLine { get; set; }
	[ScriptProperty] public DialogueLine? CurrentLine { get; private set; }
	[ScriptProperty] public bool IsRunning => CurrentLine != null;
	[ScriptProperty] public BVSignal<DialogueLine> LineChanged { get; private set; } = new();
	[ScriptProperty] public BVSignal Ended { get; private set; } = new();
	[ScriptProperty] public BVSignal<DialogueChoice> ChoiceSelected { get; private set; } = new();
	[ScriptMethod] public bool Start() { DialogueLine? line = StartLine ?? GetChildren().OfType<DialogueLine>().FirstOrDefault(); if (line == null) return false; SetLine(line); return true; }
	[ScriptMethod] public bool Continue() { if (CurrentLine == null || CurrentLine.GetChoices().Length > 0) return false; SetLine(CurrentLine.NextLine); return true; }
	[ScriptMethod] public bool Choose(DialogueChoice choice) { if (CurrentLine == null || choice == null || choice.Parent != CurrentLine || !choice.Enabled) return false; choice.InvokeSelected(); ChoiceSelected.Invoke(choice); SetLine(choice.NextLine); return true; }
	[ScriptMethod] public void Stop() => SetLine(null);
	private void SetLine(DialogueLine? line) { bool wasRunning = IsRunning; CurrentLine = line; OnPropertyChanged(nameof(CurrentLine)); OnPropertyChanged(nameof(IsRunning)); if (line != null) LineChanged.Invoke(line); else if (wasRunning) Ended.Invoke(); }
#if CREATOR
	public override void CreatorInserted()
	{
		DialogueLine line = Root.New<DialogueLine>(); line.Name = "OpeningLine"; line.Text = "Hello!"; line.Parent = this; StartLine = line;
		base.CreatorInserted();
	}
#endif
}
