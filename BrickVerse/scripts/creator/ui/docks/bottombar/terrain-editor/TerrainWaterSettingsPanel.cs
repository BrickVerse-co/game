using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using Godot;
using System;

namespace BrickVerse.Creator.UI;

public partial class TerrainWaterSettingsPanel : VBoxContainer
{
	public enum WaterEditMode { AddWater, DrainWater, SetOceanLevel, AddDryVolume, RemoveDryVolume }
	public WaterEditMode EditMode => (WaterEditMode)GetNode<OptionButton>("%EditMode").Selected;
	public Action EditModeChanged { private get; set; } = () => { };
	public Func<bool, TerrainWater?> ResolveWater { private get; set; } = _ => null;
	public Action<string> SetStatus { private get; set; } = _ => { };

	public override void _Ready()
	{
		GetNode<OptionButton>("%EditMode").ItemSelected += _ => EditModeChanged();
		GetNode<OptionButton>("%Preset").ItemSelected += ApplyPreset;
		GetNode<CheckButton>("%Enabled").Toggled += value => WithWater(w => w.Enabled = value);
		GetNode<CheckButton>("%OceanEnabled").Toggled += value => WithWater(w => w.OceanEnabled = value);
		BindNumber("%CellSize", (w, v) => w.CellSize = v);
		BindNumber("%Level", (w, v) => w.WaterLevel = v);
		BindNumber("%Width", (w, v) => w.Size = new Vector2(v, w.Size.Y));
		BindNumber("%Depth", (w, v) => w.Size = new Vector2(w.Size.X, v));
		BindNumber("%Shoreline", (w, v) => w.ShorelineWidth = v);
		BindNumber("%Opacity", (w, v) => w.Transparency = v);
		BindNumber("%Roughness", (w, v) => w.Roughness = v);
		BindNumber("%Refraction", (w, v) => w.RefractionStrength = v);
		BindNumber("%NormalStrength", (w, v) => w.NormalStrength = v);
		BindNumber("%TextureScale", (w, v) => w.TextureScale = v);
		BindNumber("%FoamAmount", (w, v) => w.FoamAmount = v);
		BindNumber("%WaveHeight", (w, v) => w.WaveHeight = v);
		BindNumber("%WaveLength", (w, v) => w.WaveLength = v);
		BindNumber("%WaveSpeed", (w, v) => w.WaveSpeed = v);
		BindNumber("%Steepness", (w, v) => w.WaveSteepness = v);
		BindNumber("%DirectionX", (w, v) => w.WaveDirection = new Vector2(v, w.WaveDirection.Y));
		BindNumber("%DirectionZ", (w, v) => w.WaveDirection = new Vector2(w.WaveDirection.X, v));
		BindColor("%Shallow", (w, v) => w.ShallowColor = v);
		BindColor("%Deep", (w, v) => w.DeepColor = v);
		BindColor("%Foam", (w, v) => w.FoamColor = v);
		GetNode<Button>("%SelectWater").Pressed += SelectWater;
		GetNode<Button>("%ClearExclusions").Pressed += ClearExclusions;
		GetNode<Button>("%ClearVoxels").Pressed += ClearVoxels;
		GetNode<Button>("%RemoveWater").Pressed += ConfirmRemoveWater;
		GetNode<ConfirmationDialog>("%RemoveConfirmation").Confirmed += RemoveWater;
	}

	public void RefreshFromLayer()
	{
		TerrainWater? water = ResolveWater(false);
		GetNode<CheckButton>("%Enabled").SetPressedNoSignal(water?.Enabled ?? true);
		GetNode<CheckButton>("%OceanEnabled").SetPressedNoSignal(water?.OceanEnabled ?? false);
		SetNumber("%CellSize", water?.CellSize ?? 4);
		SetNumber("%Level", water?.WaterLevel ?? 0); SetNumber("%Width", water?.Size.X ?? 2048);
		SetNumber("%Depth", water?.Size.Y ?? 2048); SetNumber("%Shoreline", water?.ShorelineWidth ?? 3.5f);
		SetNumber("%Opacity", water?.Transparency ?? .72f); SetNumber("%WaveHeight", water?.WaveHeight ?? .45f);
		SetNumber("%Roughness", water?.Roughness ?? .12f); SetNumber("%Refraction", water?.RefractionStrength ?? .035f);
		SetNumber("%NormalStrength", water?.NormalStrength ?? .7f); SetNumber("%TextureScale", water?.TextureScale ?? .035f); SetNumber("%FoamAmount", water?.FoamAmount ?? .65f);
		SetNumber("%WaveLength", water?.WaveLength ?? 18); SetNumber("%WaveSpeed", water?.WaveSpeed ?? 1.4f);
		SetNumber("%Steepness", water?.WaveSteepness ?? .3f); SetNumber("%DirectionX", water?.WaveDirection.X ?? 1);
		SetNumber("%DirectionZ", water?.WaveDirection.Y ?? .25f);
		SetColor("%Shallow", water?.ShallowColor ?? new Color("32a8c7"));
		SetColor("%Deep", water?.DeepColor ?? new Color("07577d"));
		SetColor("%Foam", water?.FoamColor ?? new Color("dffcff"));
		GetNode<Button>("%SelectWater").Text = water == null ? "Create water layer" : "Select water layer";
	}

	private void BindNumber(string path, Action<TerrainWater, float> apply) => GetNode<SpinBox>(path).ValueChanged += value => WithWater(w => apply(w, (float)value));
	private void ApplyPreset(long selected)
	{
		TerrainWater? water = ResolveWater(true); if (water == null || selected == 0) return;
		switch (selected)
		{
			case 1: water.SetWave(new(1,.2f),.08f,9,.45f,.08f); water.Roughness=.2f; water.NormalStrength=.25f; water.FoamAmount=.2f; break;
			case 2: water.SetWave(new(1,.25f),.45f,18,1.4f,.3f); water.Roughness=.12f; water.NormalStrength=.7f; water.FoamAmount=.65f; break;
			case 3: water.SetWave(new(.9f,.4f),1.8f,31,2.2f,.58f); water.Roughness=.16f; water.NormalStrength=1.15f; water.FoamAmount=1.05f; break;
			case 4: water.SetWave(new(.75f,.65f),4.5f,46,3.4f,.82f); water.Roughness=.22f; water.NormalStrength=1.8f; water.FoamAmount=1.5f; break;
		}
		RefreshFromLayer(); GetNode<OptionButton>("%Preset").Select(0); SetStatus("Water appearance preset applied.");
	}
	private void BindColor(string path, Action<TerrainWater, Color> apply) => GetNode<ColorPickerButton>(path).ColorChanged += value => WithWater(w => apply(w, value));
	private void WithWater(Action<TerrainWater> apply) { TerrainWater? water = ResolveWater(true); if (water != null) apply(water); }
	private void SetNumber(string path, double value) => GetNode<SpinBox>(path).SetValueNoSignal(value);
	private void SetColor(string path, Color value) { ColorPickerButton input = GetNode<ColorPickerButton>(path); input.SetBlockSignals(true); input.Color = value; input.SetBlockSignals(false); }
	private void SelectWater() { TerrainWater? water = ResolveWater(true); if (water == null) return; CreatorService.CurrentGame?.CreatorContext.Selections.SelectOnly(water); GetNode<Button>("%SelectWater").Text = "Select water layer"; SetStatus("Terrain water selected."); }
	private void ClearExclusions() { TerrainWater? water = ResolveWater(false); if (water == null) { SetStatus("No water layer exists."); return; } water.ClearExclusions(); SetStatus("All water exclusion volumes cleared."); }
	private void ClearVoxels() { TerrainWater? water = ResolveWater(false); if (water == null) { SetStatus("No water layer exists."); return; } water.ClearVoxelWater(); SetStatus("All voxel water cleared."); }
	private void ConfirmRemoveWater()
	{
		TerrainWater? water = ResolveWater(false); if (water == null) { SetStatus("No water layer exists."); return; }
		GetNode<ConfirmationDialog>("%RemoveConfirmation").PopupCentered();
	}
	private void RemoveWater() { TerrainWater? water = ResolveWater(false); if (water == null) return; water.Delete(); SetStatus("Terrain water removed."); GetNode<Button>("%SelectWater").Text = "Create water layer"; }
}
