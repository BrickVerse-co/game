using Godot;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class DevicePreviewGuide : Control
{
	private Vector2I _target = Vector2I.Zero;
	public void SetTarget(Vector2I size) { _target = size; Visible = size.X > 0 && size.Y > 0; QueueRedraw(); }

	public override void _Draw()
	{
		if (_target.X <= 0 || _target.Y <= 0) return;
		float scale = Mathf.Min(Size.X / _target.X, Size.Y / _target.Y) * 0.94f;
		Vector2 previewSize = new(_target.X * scale, _target.Y * scale);
		Rect2 preview = new((Size - previewSize) * 0.5f, previewSize);
		Color shade = new(0.01f, 0.015f, 0.025f, 0.62f);
		DrawRect(new Rect2(0, 0, Size.X, preview.Position.Y), shade);
		DrawRect(new Rect2(0, preview.End.Y, Size.X, Size.Y - preview.End.Y), shade);
		DrawRect(new Rect2(0, preview.Position.Y, preview.Position.X, preview.Size.Y), shade);
		DrawRect(new Rect2(preview.End.X, preview.Position.Y, Size.X - preview.End.X, preview.Size.Y), shade);
		DrawRect(preview, new Color(0.2f, 0.62f, 1f, 0.95f), false, 2f);
		float safeInsetX = preview.Size.X * (_target.X < _target.Y ? 0.055f : 0.035f);
		float safeInsetY = preview.Size.Y * (_target.X < _target.Y ? 0.045f : 0.05f);
		Rect2 safeArea = preview.GrowIndividual(-safeInsetX, -safeInsetY, -safeInsetX, -safeInsetY);
		DrawRect(safeArea, new Color(0.35f, 0.9f, 0.62f, 0.75f), false, 1f);
		string breakpoint = _target.X <= 480 ? "Phone / compact" : _target.X <= 1100 ? "Tablet / medium" : "Desktop / wide";
		DrawString(ThemeDB.FallbackFont, preview.Position + new Vector2(8, -7), $"{_target.X} × {_target.Y}  •  {breakpoint}", HorizontalAlignment.Left, -1, 13, new Color(0.65f, 0.84f, 1f));
		DrawString(ThemeDB.FallbackFont, safeArea.Position + new Vector2(7, 16), "Safe area", HorizontalAlignment.Left, -1, 11, new Color(0.45f, 0.95f, 0.68f, 0.9f));
	}
}
