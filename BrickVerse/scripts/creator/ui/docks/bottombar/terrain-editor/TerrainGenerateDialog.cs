using Godot;
using System;

namespace BrickVerse.Creator.UI;

public partial class TerrainGenerateDialog : ConfirmationDialog
{
	public Action<int, int, float, float, int, bool> GenerateRequested { private get; set; } = (_, _, _, _, _, _) => { };

	public override void _Ready()
	{
		Confirmed += () => GenerateRequested(
			GetNode<OptionButton>("%Preset").Selected,
			(int)GetNode<SpinBox>("%WorldSize").Value,
			(float)GetNode<SpinBox>("%MaximumHeight").Value,
			(float)GetNode<SpinBox>("%FeatureSize").Value,
			(int)GetNode<SpinBox>("%Seed").Value,
			GetNode<CheckButton>("%Replace").ButtonPressed);
	}

	public void Open()
	{
		GetNode<SpinBox>("%Seed").Value = Random.Shared.Next(1, int.MaxValue);
		PopupCentered();
	}
}
