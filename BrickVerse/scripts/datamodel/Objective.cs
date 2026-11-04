// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using BrickVerse.Attributes;
using BrickVerse.Networking;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

/// <summary>A trackable objective with progress, completion, failure, and reset behavior.</summary>
[Instantiable]
public sealed partial class Objective : Instance
{
	private double _target = 1, _progress;
	private string _description = "";
	[Editable, ScriptProperty] public string Description { get => _description; set { _description = value ?? ""; OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(1d)] public double Target { get => _target; set { _target = Math.Max(0, double.IsFinite(value) ? value : 1); SetProgress(_progress); OnPropertyChanged(); } }
	[Editable, ScriptProperty, DefaultValue(false)] public bool Optional { get; set; }
	[Editable, ScriptProperty, DefaultValue(false)] public bool Hidden { get; set; }
	[ScriptProperty, SyncVar] public double Progress { get => _progress; private set => SetProgress(value); }
	[ScriptProperty, SyncVar] public bool Failed { get; private set; }
	[ScriptProperty] public bool Completed => !Failed && _progress >= _target;
	[ScriptProperty] public double Fraction => _target <= 0 ? 1 : Math.Clamp(_progress / _target, 0, 1);
	[ScriptProperty] public BVSignal<double, double> ProgressChanged { get; private set; } = new();
	[ScriptProperty] public BVSignal CompletedSignal { get; private set; } = new();
	[ScriptProperty] public BVSignal FailedSignal { get; private set; } = new();
	[ScriptMethod] public void AddProgress(double amount = 1) { if (HasAuthority && !Failed && !Completed && amount > 0) Progress += amount; }
	[ScriptMethod] public void Complete() { if (HasAuthority && !Failed) Progress = _target; }
	[ScriptMethod] public void Fail() { if (!HasAuthority || Failed || Completed) return; Failed = true; OnPropertyChanged(nameof(Failed)); OnPropertyChanged(nameof(Completed)); FailedSignal.Invoke(); }
	[ScriptMethod] public void Reset() { if (!HasAuthority) return; Failed = false; SetProgress(0); OnPropertyChanged(nameof(Failed)); }
	private void SetProgress(double value) { double next = Math.Clamp(double.IsFinite(value) ? value : 0, 0, _target); if (next == _progress) return; bool wasComplete = Completed; double old = _progress; _progress = next; OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(Fraction)); OnPropertyChanged(nameof(Completed)); ProgressChanged.Invoke(old, next); if (!wasComplete && Completed) CompletedSignal.Invoke(); }
}
