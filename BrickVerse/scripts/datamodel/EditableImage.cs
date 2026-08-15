// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using System;

namespace BrickVerse.Datamodel;

/// <summary>
/// A runtime-editable RGBA image backed by a Godot Image/ImageTexture.
///
/// A fixed-size image can be read/written as RGBA bytes and modified with
/// drawing primitives at runtime.
/// </summary>
[Instantiable]
public partial class EditableImage : UIImage
{
	private const int BytesPerPixel = 4;
	private const int DefaultWidth = 256;
	private const int DefaultHeight = 256;
	private const int MaxDimension = 2048;
	private const long MaxPixels = (long)MaxDimension * MaxDimension;

	private Godot.Image _editableImage = null!;
	private ImageTexture _editableTexture = null!;
	private Vector2I _size = new(DefaultWidth, DefaultHeight);

	/// <summary>
	/// Current canvas size in pixels.
	/// </summary>
	[Editable, ScriptProperty]
	public Vector2I Size
	{
		get => _size;
		set
		{
			Vector2I sanitized = SanitizeSize(value);
			if (_size == sanitized && _editableImage != null)
				return;

			_size = sanitized;
			RecreateCanvas(preservePixels: _editableImage != null);
			OnPropertyChanged();
		}
	}

	[ScriptMethod]
	public override void Init()
	{
		base.Init();
		RecreateCanvas(preservePixels: false);
	}

	/// <summary>
	/// Clears the entire image to transparent black.
	/// </summary>
	[ScriptMethod]
	public void Clear()
	{
		EnsureCanvas();
		_editableImage.Fill(new Color(0, 0, 0, 0));
		Commit();
	}

	/// <summary>
	/// Clears the entire image to a supplied color.
	/// </summary>
	[ScriptMethod]
	public void Clear(Color color)
	{
		EnsureCanvas();
		_editableImage.Fill(color);
		Commit();
	}

	/// <summary>
	/// Returns tightly-packed RGBA8 bytes for the requested region.
	/// Byte order is R, G, B, A for each pixel.
	/// </summary>
	[ScriptMethod]
	public byte[] ReadPixelsBuffer(Vector2I position, Vector2I size)
	{
		EnsureCanvas();
		ValidateRegion(position, size);

		byte[] bytes = new byte[checked(size.X * size.Y * BytesPerPixel)];
		int offset = 0;

		for (int y = 0; y < size.Y; y++)
		{
			for (int x = 0; x < size.X; x++)
			{
				Color color = _editableImage.GetPixel(position.X + x, position.Y + y);
				bytes[offset++] = ToByte(color.R);
				bytes[offset++] = ToByte(color.G);
				bytes[offset++] = ToByte(color.B);
				bytes[offset++] = ToByte(color.A);
			}
		}

		return bytes;
	}

	/// <summary>
	/// Writes tightly-packed RGBA8 bytes into the requested region.
	/// </summary>
	[ScriptMethod]
	public void WritePixelsBuffer(Vector2I position, Vector2I size, byte[] pixels)
	{
		EnsureCanvas();
		ValidateRegion(position, size);

		if (pixels == null)
			throw new ArgumentNullException(nameof(pixels));

		int expectedLength = checked(size.X * size.Y * BytesPerPixel);
		if (pixels.Length != expectedLength)
		{
			throw new ArgumentException(
				$"Pixel buffer must contain exactly {expectedLength} bytes for a {size.X}x{size.Y} RGBA8 region.",
				nameof(pixels)
			);
		}

		int offset = 0;
		for (int y = 0; y < size.Y; y++)
		{
			for (int x = 0; x < size.X; x++)
			{
				Color color = new(
					pixels[offset++] / 255.0f,
					pixels[offset++] / 255.0f,
					pixels[offset++] / 255.0f,
					pixels[offset++] / 255.0f
				);
				_editableImage.SetPixel(position.X + x, position.Y + y, color);
			}
		}

		Commit();
	}

	[ScriptMethod]
	public void DrawRectangle(Vector2 position, Vector2 size, Color color, float transparency = 0.0f)
	{
		EnsureCanvas();

		int left = Mathf.RoundToInt(position.X);
		int top = Mathf.RoundToInt(position.Y);
		int width = Mathf.Max(0, Mathf.RoundToInt(size.X));
		int height = Mathf.Max(0, Mathf.RoundToInt(size.Y));
		Color source = WithTransparency(color, transparency);

		for (int y = top; y < top + height; y++)
		{
			for (int x = left; x < left + width; x++)
				BlendPixel(x, y, source);
		}

		Commit();
	}

	[ScriptMethod]
	public void DrawCircle(Vector2 center, float radius, Color color, float transparency = 0.0f)
	{
		EnsureCanvas();
		if (radius < 0.0f)
			throw new ArgumentOutOfRangeException(nameof(radius), "Radius cannot be negative.");

		Color source = WithTransparency(color, transparency);
		int minX = Mathf.FloorToInt(center.X - radius);
		int maxX = Mathf.CeilToInt(center.X + radius);
		int minY = Mathf.FloorToInt(center.Y - radius);
		int maxY = Mathf.CeilToInt(center.Y + radius);
		float radiusSquared = radius * radius;

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				float dx = (x + 0.5f) - center.X;
				float dy = (y + 0.5f) - center.Y;
				if ((dx * dx) + (dy * dy) <= radiusSquared)
					BlendPixel(x, y, source);
			}
		}

		Commit();
	}

	[ScriptMethod]
	public void DrawLine(Vector2 p1, Vector2 p2, Color color, float transparency = 0.0f, float thickness = 1.0f)
	{
		EnsureCanvas();
		if (thickness <= 0.0f)
			return;

		Color source = WithTransparency(color, transparency);
		Vector2 delta = p2 - p1;
		int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(delta.X), Mathf.Abs(delta.Y))));
		float radius = Mathf.Max(0.5f, thickness * 0.5f);

		for (int i = 0; i <= steps; i++)
		{
			float t = i / (float)steps;
			Vector2 point = p1.Lerp(p2, t);
			StampCircle(point, radius, source);
		}

		Commit();
	}

	/// <summary>
	/// Draws another EditableImage at the specified top-left pixel position.
	/// Pixels are alpha composited over this image.
	/// </summary>
	[ScriptMethod]
	public void DrawImage(Vector2 position, EditableImage source)
	{
		if (source == null)
			throw new ArgumentNullException(nameof(source));

		EnsureCanvas();
		source.EnsureCanvas();

		int originX = Mathf.RoundToInt(position.X);
		int originY = Mathf.RoundToInt(position.Y);

		for (int y = 0; y < source._size.Y; y++)
		{
			for (int x = 0; x < source._size.X; x++)
			{
				Color pixel = source._editableImage.GetPixel(x, y);
				BlendPixel(originX + x, originY + y, pixel);
			}
		}

		Commit();
	}

	/// <summary>
	/// Returns a snapshot of this editable canvas as a Texture2D.
	/// </summary>
	[ScriptMethod]
	public Texture2D GetTexture()
	{
		EnsureCanvas();
		return _editableTexture;
	}

	private void RecreateCanvas(bool preservePixels)
	{
		Godot.Image? previous = preservePixels ? _editableImage : null;
		Vector2I previousSize = previous == null
			? Vector2I.Zero
			: new Vector2I(previous.GetWidth(), previous.GetHeight());

		_editableImage = Godot.Image.CreateEmpty(_size.X, _size.Y, false, Godot.Image.Format.Rgba8);
		_editableImage.Fill(new Color(0, 0, 0, 0));

		if (previous != null)
		{
			int copyWidth = Math.Min(previousSize.X, _size.X);
			int copyHeight = Math.Min(previousSize.Y, _size.Y);
			for (int y = 0; y < copyHeight; y++)
			{
				for (int x = 0; x < copyWidth; x++)
					_editableImage.SetPixel(x, y, previous.GetPixel(x, y));
			}
		}

		_editableTexture = ImageTexture.CreateFromImage(_editableImage);
		ApplyRuntimeTexture();
	}

	private void EnsureCanvas()
	{
		if (_editableImage == null || _editableTexture == null)
			RecreateCanvas(preservePixels: false);
	}

	private void Commit()
	{
		_editableTexture.Update(_editableImage);
		ApplyRuntimeTexture();
	}

	private void ApplyRuntimeTexture()
	{
		if (GDTextureRect == null)
			return;

		GDTextureRect.Material = null;
		GDTextureRect.Texture = _editableTexture;
		GDTextureRect.SelfModulate = Color;
	}

	private void StampCircle(Vector2 center, float radius, Color color)
	{
		int minX = Mathf.FloorToInt(center.X - radius);
		int maxX = Mathf.CeilToInt(center.X + radius);
		int minY = Mathf.FloorToInt(center.Y - radius);
		int maxY = Mathf.CeilToInt(center.Y + radius);
		float radiusSquared = radius * radius;

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				float dx = (x + 0.5f) - center.X;
				float dy = (y + 0.5f) - center.Y;
				if ((dx * dx) + (dy * dy) <= radiusSquared)
					BlendPixel(x, y, color);
			}
		}
	}

	private void BlendPixel(int x, int y, Color source)
	{
		if (x < 0 || y < 0 || x >= _size.X || y >= _size.Y || source.A <= 0.0f)
			return;

		Color destination = _editableImage.GetPixel(x, y);
		float outAlpha = source.A + destination.A * (1.0f - source.A);

		if (outAlpha <= 0.000001f)
		{
			_editableImage.SetPixel(x, y, new Color(0, 0, 0, 0));
			return;
		}

		float srcFactor = source.A;
		float dstFactor = destination.A * (1.0f - source.A);
		Color result = new(
			((source.R * srcFactor) + (destination.R * dstFactor)) / outAlpha,
			((source.G * srcFactor) + (destination.G * dstFactor)) / outAlpha,
			((source.B * srcFactor) + (destination.B * dstFactor)) / outAlpha,
			outAlpha
		);
		_editableImage.SetPixel(x, y, result);
	}

	private static Color WithTransparency(Color color, float transparency)
	{
		float opacity = 1.0f - Mathf.Clamp(transparency, 0.0f, 1.0f);
		return new Color(color.R, color.G, color.B, color.A * opacity);
	}

	private static byte ToByte(float value)
	{
		return (byte)Mathf.RoundToInt(Mathf.Clamp(value, 0.0f, 1.0f) * 255.0f);
	}

	private static Vector2I SanitizeSize(Vector2I size)
	{
		if (size.X <= 0 || size.Y <= 0)
			throw new ArgumentOutOfRangeException(nameof(size), "EditableImage dimensions must be greater than zero.");
		if (size.X > MaxDimension || size.Y > MaxDimension || (long)size.X * size.Y > MaxPixels)
			throw new ArgumentOutOfRangeException(nameof(size), $"EditableImage cannot exceed {MaxDimension}x{MaxDimension} pixels.");
		return size;
	}

	private void ValidateRegion(Vector2I position, Vector2I size)
	{
		if (size.X < 0 || size.Y < 0)
			throw new ArgumentOutOfRangeException(nameof(size), "Region size cannot be negative.");
		if (position.X < 0 || position.Y < 0 || position.X + size.X > _size.X || position.Y + size.Y > _size.Y)
			throw new ArgumentOutOfRangeException(nameof(position), "Pixel region must be fully inside the EditableImage bounds.");
	}
}
