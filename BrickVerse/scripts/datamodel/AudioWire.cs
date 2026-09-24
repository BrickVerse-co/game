// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Attributes;

namespace BrickVerse.Datamodel;

/// <summary>Connects a named output pin to a named input pin in the audio graph.</summary>
[Instantiable]
public sealed partial class AudioWire : Instance
{
	private Instance? _source;
	private Instance? _target;
	private string _sourceName = "Output";
	private string _targetName = "Input";
	private bool _lastConnected;

	[Editable, ScriptProperty]
	public Instance? SourceInstance
	{
		get => _source;
		set { if (_source == value) return; NotifyDisconnected(); _source = value; Refresh(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty]
	public string SourceName
	{
		get => _sourceName;
		set { string next = string.IsNullOrWhiteSpace(value) ? "Output" : value.Trim(); if (_sourceName == next) return; NotifyDisconnected(); _sourceName = next; Refresh(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty]
	public Instance? TargetInstance
	{
		get => _target;
		set { if (_target == value) return; NotifyDisconnected(); _target = value; Refresh(); OnPropertyChanged(); }
	}

	[Editable, ScriptProperty]
	public string TargetName
	{
		get => _targetName;
		set { string next = string.IsNullOrWhiteSpace(value) ? "Input" : value.Trim(); if (_targetName == next) return; NotifyDisconnected(); _targetName = next; Refresh(); OnPropertyChanged(); }
	}

	[ScriptProperty, SaveIgnore, CloneIgnore]
	public bool Connected => _source is IAudioWireNode source && _target is IAudioWireNode target && _source != _target && _source.Root == Root && _target.Root == Root && source.HasOutputPin(_sourceName) && target.HasInputPin(_targetName) && !AudioGraph.WouldCycle(this);

	public override void Ready() { Refresh(); base.Ready(); }

	public override void PreDelete()
	{
		NotifyDisconnected();
		base.PreDelete();
	}

	private void Refresh()
	{
		bool connected = Connected;
		if (connected && !_lastConnected) Notify(true);
		_lastConnected = connected;
		if (Root != null) AudioGraph.Rebuild(Root);
		OnPropertyChanged(nameof(Connected));
	}

	private void NotifyDisconnected()
	{
		if (_lastConnected) Notify(false);
		_lastConnected = false;
	}

	private void Notify(bool connected)
	{
		if (_source is IAudioWireNode source && _target is IAudioWireNode target)
		{
			source.EmitWiringChanged(connected, _sourceName, this, _target);
			target.EmitWiringChanged(connected, _targetName, this, _source);
		}
	}
}
