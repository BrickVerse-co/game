// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Linq;
using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>An ordered quest made from QuestStep children with explicit progression.</summary>
[Instantiable]
public sealed partial class Quest : Instance
{
	private int _currentStep = -1;
	[Editable, ScriptProperty] public string Description { get; set; } = "";
	[Editable, ScriptProperty, DefaultValue(false)] public bool Repeatable { get; set; }
	[ScriptProperty, SyncVar] public int CurrentStepIndex { get => _currentStep; private set { _currentStep = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentStep)); } }
	[ScriptProperty] public QuestStep? CurrentStep { get { QuestStep[] steps = GetSteps(); return _currentStep >= 0 && _currentStep < steps.Length ? steps[_currentStep] : null; } }
	[ScriptProperty, SyncVar] public bool IsActive { get; private set; }
	[ScriptProperty, SyncVar] public bool IsCompleted { get; private set; }
	[ScriptProperty] public BVSignal Started { get; private set; } = new();
	[ScriptProperty] public BVSignal<QuestStep> StepStarted { get; private set; } = new();
	[ScriptProperty] public BVSignal Completed { get; private set; } = new();
	[ScriptProperty] public BVSignal Failed { get; private set; } = new();
	[ScriptMethod] public QuestStep[] GetSteps() => [.. GetChildren().OfType<QuestStep>()];
	[ScriptMethod] public bool Start() { if (!HasAuthority || IsActive || (IsCompleted && !Repeatable) || GetSteps().Length == 0) return false; if (Repeatable) Reset(); IsActive = true; CurrentStepIndex = 0; OnPropertyChanged(nameof(IsActive)); Started.Invoke(); StepStarted.Invoke(CurrentStep!); return true; }
	[ScriptMethod] public bool Advance() { if (!HasAuthority || !IsActive || CurrentStep == null) return false; if (CurrentStep.Failed) { IsActive = false; OnPropertyChanged(nameof(IsActive)); Failed.Invoke(); return false; } if (!CurrentStep.Completed) return false; int next = CurrentStepIndex + 1; if (next >= GetSteps().Length) { IsActive = false; IsCompleted = true; OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(IsCompleted)); Completed.Invoke(); return true; } CurrentStepIndex = next; StepStarted.Invoke(CurrentStep!); return true; }
	[ScriptMethod] public void Reset() { if (!HasAuthority) return; foreach (QuestStep step in GetSteps()) step.Reset(); IsActive = false; IsCompleted = false; CurrentStepIndex = -1; OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(IsCompleted)); }
#if CREATOR
	public override void CreatorInserted()
	{
		QuestStep step = Root.New<QuestStep>(); step.Name = "FirstStep"; step.Parent = this;
		Objective objective = Root.New<Objective>(); objective.Name = "Objective"; objective.Parent = step;
		base.CreatorInserted();
	}
#endif
}
