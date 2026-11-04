// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using System;
using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>Built-in texture paint canvas with editable-mesh UV layout baking.</summary>
public sealed partial class TexturePaintWindow : PopupWindowBase
{
	private const int TextureSize = 1024;
	private readonly EditableMeshPart? _mesh;
	private Image _image = null!;
	private ImageTexture _texture = null!;
	private PaintCanvas _canvas = null!;
	private ColorPickerButton _color = null!;
	private SpinBox _brushSize = null!;

	private TexturePaintWindow(EditableMeshPart? mesh)
	{
		_mesh = mesh;
		Title = mesh == null ? "Texture Paint" : $"Texture Paint — {mesh.Name}";
		Size = new Vector2I(900, 760);
		MinSize = new Vector2I(640, 520);
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		Transient = true;
	}

	public static void Open()
	{
		EditableMeshPart? mesh = World.Current?.CreatorContext.Selections.GetSelected()
			.OfType<EditableMeshPart>().FirstOrDefault();
		CreatorService.Interface.PopupWindow(new TexturePaintWindow(mesh));
	}

	public override void _Ready()
	{
		_image = Image.CreateEmpty(TextureSize, TextureSize, false, Image.Format.Rgba8);
		_image.Fill(Colors.White);
		_texture = ImageTexture.CreateFromImage(_image);
		VBoxContainer page = new();
		page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(page);
		HBoxContainer toolbar = new() { CustomMinimumSize = new Vector2(0, 46) };
		page.AddChild(toolbar);
		_color = new ColorPickerButton { Color = new Color("2b7fff"), CustomMinimumSize = new Vector2(90, 34) };
		toolbar.AddChild(_color);
		_brushSize = new SpinBox { MinValue = 1, MaxValue = 256, Value = 24, Suffix = " px", CustomMinimumSize = new Vector2(110, 34) };
		toolbar.AddChild(_brushSize);
		AddButton(toolbar, "Clear", Clear);
		AddButton(toolbar, "Bake UV Layout", BakeUvLayout);
		AddButton(toolbar, "Export PNG", Export);
		toolbar.AddChild(new Label
		{
			Text = _mesh == null ? "Select an EditableMeshPart before opening to bake its UVs." : "Paint over the baked UV template, export, then assign as a PBR ColorMap.",
			VerticalAlignment = VerticalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Right,
		});
		_canvas = new PaintCanvas(_texture, Paint)
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		page.AddChild(_canvas);
		base._Ready();
	}

	private void Paint(Vector2 uv)
	{
		int centerX = Mathf.Clamp((int)(uv.X * TextureSize), 0, TextureSize - 1);
		int centerY = Mathf.Clamp((int)(uv.Y * TextureSize), 0, TextureSize - 1);
		int radius = (int)_brushSize.Value;
		for (int y = -radius; y <= radius; y++)
			for (int x = -radius; x <= radius; x++)
				if (x * x + y * y <= radius * radius)
				{
					int px = centerX + x, py = centerY + y;
					if ((uint)px < TextureSize && (uint)py < TextureSize)
						_image.SetPixel(px, py, _image.GetPixel(px, py).Lerp(_color.Color, _color.Color.A));
				}
		_texture.Update(_image);
	}

	private void Clear()
	{
		_image.Fill(Colors.White);
		_texture.Update(_image);
	}

	private void BakeUvLayout()
	{
		if (_mesh == null) return;
		EditableMesh geometry = _mesh.Geometry;
		foreach (int faceId in geometry.GetFaces())
		{
			Vector2[] points = geometry.GetFaceUVs(faceId)
				.Select(id => geometry.GetUV(id) * (TextureSize - 1)).ToArray();
			for (int corner = 0; corner < 3; corner++)
				DrawLine((Vector2I)points[corner], (Vector2I)points[(corner + 1) % 3], new Color(0, 0, 0, 0.75f));
		}
		_texture.Update(_image);
	}

	private void DrawLine(Vector2I from, Vector2I to, Color color)
	{
		int x = from.X, y = from.Y;
		int dx = Math.Abs(to.X - x), sx = x < to.X ? 1 : -1;
		int dy = -Math.Abs(to.Y - y), sy = y < to.Y ? 1 : -1;
		int error = dx + dy;
		while (true)
		{
			if ((uint)x < TextureSize && (uint)y < TextureSize) _image.SetPixel(x, y, color);
			if (x == to.X && y == to.Y) break;
			int twice = error * 2;
			if (twice >= dy) { error += dy; x += sx; }
			if (twice <= dx) { error += dx; y += sy; }
		}
	}

	private void Export()
	{
		CreatorService.Interface.PromptFileSelect(
			new FileSelectPromptPayload
			{
				Title = "Export painted texture",
				FileName = (_mesh?.Name ?? "painted-texture") + ".png",
				DialogMode = DisplayServer.FileDialogMode.SaveFile,
				Filters = ["*.png;PNG Image"],
			},
			paths =>
			{
				string path = paths[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? paths[0] : paths[0] + ".png";
				Error result = _image.SavePng(path);
				CreatorService.Interface.StatusBar?.SetStatus(result == Error.Ok ? "Exported painted texture to " + path : "Texture export failed: " + result);
			}
		);
	}

	private static void AddButton(Control parent, string text, Action action)
	{
		Button button = new() { Text = text, CustomMinimumSize = new Vector2(105, 34) };
		button.Pressed += action; parent.AddChild(button);
	}

	private sealed partial class PaintCanvas : TextureRect
	{
		private readonly Action<Vector2> _paint;
		private bool _painting;
		public PaintCanvas(Texture2D texture, Action<Vector2> paint)
		{
			Texture = texture; _paint = paint;
			ExpandMode = ExpandModeEnum.IgnoreSize;
			StretchMode = StretchModeEnum.KeepAspectCentered;
			MouseDefaultCursorShape = CursorShape.Cross;
		}
		public override void _GuiInput(InputEvent inputEvent)
		{
			if (inputEvent is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
			{
				_painting = button.Pressed;
				if (_painting) PaintAt(button.Position);
			}
			else if (_painting && inputEvent is InputEventMouseMotion motion) PaintAt(motion.Position);
		}
		private void PaintAt(Vector2 point)
		{
			float side = Mathf.Min(Size.X, Size.Y);
			Vector2 origin = (Size - Vector2.One * side) * 0.5f;
			Vector2 uv = (point - origin) / side;
			if (uv.X is >= 0 and <= 1 && uv.Y is >= 0 and <= 1) _paint(uv);
		}
	}
}
