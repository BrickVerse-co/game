using BrickVerse.Attributes;
using Godot;

namespace BrickVerse.Datamodel;

/// <summary>A Roblox-compatible spawn part with optional team filtering.</summary>
[Instantiable]
public sealed partial class SpawnLocation : Part
{
	private bool _neutral = true;
	private Color _teamColor = Colors.White;

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool Neutral { get => _neutral; set { _neutral = value; OnPropertyChanged(); } }

	[Editable, ScriptProperty]
	public Color TeamColor { get => _teamColor; set { _teamColor = value; OnPropertyChanged(); } }

	public override void Init()
	{
		base.Init();
		IsSpawn = true;
		Anchored = true;
		Size = new Vector3(6, 1, 6);
	}

	internal bool CanSpawn(Player player) => Neutral || player.Team?.Color == TeamColor;
}
