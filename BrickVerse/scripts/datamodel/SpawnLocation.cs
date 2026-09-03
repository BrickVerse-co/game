using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A spawn part with optional team filtering.</summary>
[Instantiable]
public sealed partial class SpawnLocation : Part
{
	private bool _neutral = true;
	private Team? _team;
	private Color _teamColor = Colors.White;
	private bool _legacyTeamColorConfigured;

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Neutral { get => _neutral; set { _neutral = value; OnPropertyChanged(); } }

	[Editable, ScriptProperty]
	public Team? Team { get => _team; set { _team = value; OnPropertyChanged(); } }

	// Retained for loading and scripting older worlds. New spawns should reference Team directly.
	[Editable(IsHidden = true), ScriptProperty, Attributes.Obsolete("Use Team instead")]
	public Color TeamColor
	{
		get => _teamColor;
		set
		{
			_teamColor = value;
			_legacyTeamColorConfigured = true;
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		base.Init();
		IsSpawn = true;
		Anchored = true;
		Size = new Vector3(6, 1, 6);
	}

	internal bool CanSpawn(Player player)
	{
		if (Neutral) return true;
		if (Team != null) return player.Team == Team;
		return _legacyTeamColorConfigured && player.Team?.Color == TeamColor;
	}
}
