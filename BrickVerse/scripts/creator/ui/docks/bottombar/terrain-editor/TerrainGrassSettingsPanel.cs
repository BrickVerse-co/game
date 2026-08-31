using BrickVerse.Datamodel;
using Godot;
using System;

namespace BrickVerse.Creator.UI;

public partial class TerrainGrassSettingsPanel : VBoxContainer
{
	public Func<bool, TerrainGrass?> ResolveGrass { private get; set; } = _ => null;

	public override void _Ready()
	{
		BindNumber("%Density", (g, v) => g.Density = v); BindNumber("%Height", (g, v) => g.BladeHeight = v);
		BindNumber("%Width", (g, v) => g.BladeWidth = v); BindNumber("%Inset", (g, v) => g.SurfaceOffset = v);
		BindNumber("%WindStrength", (g, v) => g.Root.Environment.WindStrength = v); BindNumber("%WindSpeed", (g, v) => g.Root.Environment.WindSpeed = v);
		BindNumber("%PaintDensity", (g, v) => g.PaintDensityScale = v); BindNumber("%PaintHeight", (g, v) => g.PaintHeightScale = v);
		BindNumber("%PaintWidth", (g, v) => g.PaintWidthScale = v);
		BindColor("%BaseColor", (g, v) => g.BaseColor = v); BindColor("%TipColor", (g, v) => g.TipColor = v); BindColor("%PaintColor", (g, v) => g.PaintColor = v);
		GetNode<CheckButton>("%Conform").Toggled += value => WithGrass(g => g.DeformToSurface = value);
	}

	public void RefreshFromLayer()
	{
		TerrainGrass? grass = ResolveGrass(false);
		SetNumber("%Density", grass?.Density ?? 1.2); SetNumber("%Height", grass?.BladeHeight ?? 1.4); SetNumber("%Width", grass?.BladeWidth ?? .13);
		SetNumber("%Inset", grass?.SurfaceOffset ?? -.1); SetNumber("%WindStrength", grass?.Root.Environment.WindStrength ?? .28); SetNumber("%WindSpeed", grass?.Root.Environment.WindSpeed ?? 1.5);
		SetNumber("%PaintDensity", grass?.PaintDensityScale ?? 1); SetNumber("%PaintHeight", grass?.PaintHeightScale ?? 1); SetNumber("%PaintWidth", grass?.PaintWidthScale ?? 1);
		SetColor("%BaseColor", grass?.BaseColor ?? new Color("327a32")); SetColor("%TipColor", grass?.TipColor ?? new Color("83c95b")); SetColor("%PaintColor", grass?.PaintColor ?? Colors.White);
		GetNode<CheckButton>("%Conform").SetPressedNoSignal(grass?.DeformToSurface ?? true);
	}

	private void BindNumber(string path, Action<TerrainGrass, float> apply) => GetNode<SpinBox>(path).ValueChanged += value => WithGrass(g => apply(g, (float)value));
	private void BindColor(string path, Action<TerrainGrass, Color> apply) => GetNode<ColorPickerButton>(path).ColorChanged += value => WithGrass(g => apply(g, value));
	private void WithGrass(Action<TerrainGrass> apply) { TerrainGrass? grass = ResolveGrass(true); if (grass != null) apply(grass); }
	private void SetNumber(string path, double value) => GetNode<SpinBox>(path).SetValueNoSignal(value);
	private void SetColor(string path, Color value) { ColorPickerButton input = GetNode<ColorPickerButton>(path); input.SetBlockSignals(true); input.Color = value; input.SetBlockSignals(false); }
}
