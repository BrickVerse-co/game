// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System;
using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>Node-inspired PBR material workspace backed by SurfaceAppearance.</summary>
public sealed partial class MaterialEditorWindow : PopupWindowBase
{
	private static MaterialEditorWindow? _open;
	private readonly SurfaceAppearance _material;
	private ColorPickerButton _color = null!;
	private HSlider _roughness = null!;
	private HSlider _metalness = null!;
	private OptionButton _alpha = null!;
	private Label _readout = null!;

	private MaterialEditorWindow(SurfaceAppearance material)
	{
		_material = material;
		Title = $"PBR Material — {material.Parent?.Name ?? material.Name}";
		Size = new Vector2I(940, 600);
		MinSize = new Vector2I(700, 440);
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		Transient = true;
	}

	public static void Open()
	{
		World? world = World.Current;
		Instance? target = world?.CreatorContext.Selections.GetSelected().FirstOrDefault();
		if (world == null || target is not (Part or BrickVerse.Datamodel.Mesh or Model or Pawn))
		{
			CreatorService.Interface.StatusBar?.SetStatus("Select a Part, Mesh, Model, or Pawn to edit its PBR material");
			return;
		}
		SurfaceAppearance? material = target.Children.OfType<SurfaceAppearance>().FirstOrDefault();
		if (material == null)
		{
			material = world.New<SurfaceAppearance>(target);
			material.Name = "PBRMaterial";
		}
		if (_open != null && IsInstanceValid(_open))
			_open.QueueFree();
		_open = new MaterialEditorWindow(material);
		CreatorService.Interface.PopupWindow(_open);
	}

	public override void _Ready()
	{
		ColorRect background = new() { Color = new Color("11141b") };
		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(background);
		VBoxContainer page = new();
		page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 18);
		AddChild(page);

		HBoxContainer presets = new() { CustomMinimumSize = new Vector2(0, 42) };
		page.AddChild(presets);
		presets.AddChild(new Label { Text = "Material library  ", VerticalAlignment = VerticalAlignment.Center });
		AddPreset(presets, "Plastic", Colors.White, 0.65f, 0f);
		AddPreset(presets, "Matte", new Color("aeb5c2"), 0.95f, 0f);
		AddPreset(presets, "Metal", new Color("b9c2cf"), 0.22f, 1f);
		AddPreset(presets, "Gold", new Color("d8a83e"), 0.2f, 1f);
		AddPreset(presets, "Rubber", new Color("24262b"), 0.88f, 0f);
		AddPreset(presets, "Glass", new Color(0.82f, 0.92f, 1f, 0.35f), 0.08f, 0f, true);

		HBoxContainer graph = new()
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		graph.AddThemeConstantOverride("separation", 42);
		page.AddChild(graph);
		VBoxContainer textureNode = NodeCard("Texture inputs", "Color • Normal • Roughness • Metalness");
		textureNode.AddChild(new Label
		{
			Text = "Assign texture assets through the Properties panel.\nMaps feed directly into the PBR surface node.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Modulate = new Color("9aa6b7"),
		});
		graph.AddChild(textureNode);

		VBoxContainer surfaceNode = NodeCard("PBR surface", "Physically based shading");
		_color = new ColorPickerButton { Color = _material.Color, CustomMinimumSize = new Vector2(220, 38) };
		_color.ColorChanged += color => { _material.Color = color; Refresh(); };
		surfaceNode.AddChild(Labeled("Base color", _color));
		_roughness = Slider(_material.Roughness);
		_roughness.ValueChanged += value => { _material.Roughness = (float)value; Refresh(); };
		surfaceNode.AddChild(Labeled("Roughness", _roughness));
		_metalness = Slider(_material.Metalness);
		_metalness.ValueChanged += value => { _material.Metalness = (float)value; Refresh(); };
		surfaceNode.AddChild(Labeled("Metalness", _metalness));
		_alpha = new OptionButton();
		_alpha.AddItem("Overlay / Opaque");
		_alpha.AddItem("Transparency");
		_alpha.Selected = (int)_material.AlphaMode;
		_alpha.ItemSelected += index => _material.AlphaMode = (SurfaceAppearance.AlphaModeEnum)index;
		surfaceNode.AddChild(Labeled("Alpha mode", _alpha));
		graph.AddChild(surfaceNode);

		VBoxContainer outputNode = NodeCard("Material output", "Part and mesh renderer");
		_readout = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		outputNode.AddChild(_readout);
		CheckButton enabled = new() { Text = "Enabled", ButtonPressed = _material.Enabled };
		enabled.Toggled += value => _material.Enabled = value;
		outputNode.AddChild(enabled);
		graph.AddChild(outputNode);
		Refresh();
		base._Ready();
	}

	public override void _ExitTree()
	{
		if (_open == this) _open = null;
		base._ExitTree();
	}

	private void AddPreset(Control parent, string name, Color color, float roughness, float metalness, bool transparent = false)
	{
		Button button = new() { Text = name, CustomMinimumSize = new Vector2(82, 34) };
		button.Pressed += () =>
		{
			_material.Color = color;
			_material.Roughness = roughness;
			_material.Metalness = metalness;
			_material.AlphaMode = transparent ? SurfaceAppearance.AlphaModeEnum.Transparency : SurfaceAppearance.AlphaModeEnum.Overlay;
			_color.Color = color;
			_roughness.Value = roughness;
			_metalness.Value = metalness;
			_alpha.Selected = (int)_material.AlphaMode;
			Refresh();
		};
		parent.AddChild(button);
	}

	private static VBoxContainer NodeCard(string title, string subtitle)
	{
		VBoxContainer card = new()
		{
			CustomMinimumSize = new Vector2(245, 330),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		Panel panel = new();
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		card.AddChild(panel);
		Label heading = new() { Text = title };
		heading.AddThemeFontSizeOverride("font_size", 19);
		card.AddChild(heading);
		card.AddChild(new Label { Text = subtitle, Modulate = new Color("8190a5") });
		card.AddChild(new HSeparator());
		return card;
	}

	private static Control Labeled(string text, Control input)
	{
		VBoxContainer row = new();
		row.AddChild(new Label { Text = text });
		row.AddChild(input);
		return row;
	}

	private static HSlider Slider(float value) => new()
	{
		MinValue = 0,
		MaxValue = 1,
		Step = 0.01,
		Value = value,
		CustomMinimumSize = new Vector2(220, 28),
	};

	private void Refresh()
	{
		if (_readout == null) return;
		_readout.Text = $"Roughness  {_material.Roughness:0.00}\nMetalness  {_material.Metalness:0.00}\nAlpha  {_material.Color.A:0.00}\n\nChanges update every target using this SurfaceAppearance immediately.";
	}
}
