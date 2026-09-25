using Godot;
using System;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class GamepadInputSimulator : Control
{
	public Vector2 LeftStick { get; private set; }
	public Vector2 RightStick { get; private set; }
	public bool Primary { get; private set; }
	public bool Secondary { get; private set; }
	public bool LeftTrigger { get; private set; }
	public bool RightTrigger { get; private set; }
	public event Action? Changed;
	private int _dragStick;
	private static readonly Color Body = new("202936"), Edge = new("526277"), Accent = new("0097ff");

	public GamepadInputSimulator()
	{
		CustomMinimumSize = new Vector2(520, 285); MouseFilter = MouseFilterEnum.Stop;
		TooltipText = "Drag either stick. Hold the face buttons or triggers to inject input.";
	}

	public void Reset()
	{
		LeftStick = RightStick = Vector2.Zero; Primary = Secondary = LeftTrigger = RightTrigger = false;
		Changed?.Invoke(); QueueRedraw();
	}

	public override void _GuiInput(InputEvent input)
	{
		Vector2 left = new(Size.X * .29f, 165), right = new(Size.X * .65f, 190);
		Vector2 a = new(Size.X - 82, 137), b = new(Size.X - 42, 99);
		if (input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
		{
			if (mouse.Pressed)
			{
				if (mouse.Position.DistanceTo(left) <= 45) _dragStick = 1;
				else if (mouse.Position.DistanceTo(right) <= 45) _dragStick = 2;
				else if (mouse.Position.DistanceTo(a) <= 20) Primary = true;
				else if (mouse.Position.DistanceTo(b) <= 20) Secondary = true;
				else if (new Rect2(70, 35, 105, 30).HasPoint(mouse.Position)) LeftTrigger = true;
				else if (new Rect2(Size.X - 175, 35, 105, 30).HasPoint(mouse.Position)) RightTrigger = true;
				UpdateStick(mouse.Position, left, right);
			}
			else
			{
				_dragStick = 0; Primary = Secondary = LeftTrigger = RightTrigger = false;
			}
			Changed?.Invoke(); QueueRedraw(); AcceptEvent();
		}
		else if (input is InputEventMouseMotion motion && _dragStick != 0)
		{
			UpdateStick(motion.Position, left, right); Changed?.Invoke(); QueueRedraw(); AcceptEvent();
		}
	}

	private void UpdateStick(Vector2 mouse, Vector2 left, Vector2 right)
	{
		if (_dragStick == 0) return;
		Vector2 delta = (mouse - (_dragStick == 1 ? left : right)) / 34f;
		if (delta.Length() > 1) delta = delta.Normalized();
		if (_dragStick == 1) LeftStick = delta; else RightStick = delta;
	}

	public override void _Draw()
	{
		Vector2 left = new(Size.X * .29f, 165), right = new(Size.X * .65f, 190);
		Vector2[] body = [new(55, 85), new(155, 62), new(Size.X - 155, 62), new(Size.X - 55, 85), new(Size.X - 18, 220), new(Size.X - 100, 255), new(Size.X - 170, 215), new(170, 215), new(100, 255), new(18, 220)];
		DrawColoredPolygon(body, Body); DrawPolyline(body, Edge, 2, true);
		DrawTrigger(new Rect2(70, 35, 105, 30), "LT", LeftTrigger); DrawTrigger(new Rect2(Size.X - 175, 35, 105, 30), "RT", RightTrigger);
		DrawStick(left, LeftStick, "MOVE"); DrawStick(right, RightStick, "LOOK");
		DrawButton(new(Size.X - 82, 137), "A", new("40c878"), Primary);
		DrawButton(new(Size.X - 42, 99), "B", new("ef5264"), Secondary);
		DrawString(ThemeDB.FallbackFont, new Vector2(18, 278), "Drag sticks • hold buttons/triggers • values reset when released", HorizontalAlignment.Left, -1, 12, new("93a4ba"));
	}

	private void DrawStick(Vector2 center, Vector2 value, string label)
	{
		DrawCircle(center, 46, new("111821")); DrawArc(center, 46, 0, Mathf.Tau, 40, Edge, 2);
		Vector2 knob = center + value * 34; DrawCircle(knob, 22, value.IsZeroApprox() ? new("344154") : Accent);
		DrawString(ThemeDB.FallbackFont, center + new Vector2(-23, 68), label, HorizontalAlignment.Center, 46, 11, new("aebbd0"));
	}
	private void DrawButton(Vector2 center, string text, Color color, bool down)
	{
		DrawCircle(center, 21, down ? Colors.White : color); DrawString(ThemeDB.FallbackFont, center + new Vector2(-8, 6), text, HorizontalAlignment.Center, 16, 14, down ? color : Colors.White);
	}
	private void DrawTrigger(Rect2 rect, string text, bool down)
	{
		DrawRect(rect, down ? Accent : new Color("303c4d")); DrawRect(rect, Edge, false, 2); DrawString(ThemeDB.FallbackFont, rect.Position + new Vector2(0, 21), text, HorizontalAlignment.Center, rect.Size.X, 12, Colors.White);
	}
}

public sealed partial class VRInputSimulator : Control
{
	public float HeadYawDegrees { get; private set; }
	public float HeadHeight { get; private set; } = 1.7f;
	public float HandSpread { get; private set; } = .45f;
	public event Action? Changed;
	private int _dragMode;
	private Vector2 _dragStart;
	private float _startYaw, _startHeight, _startSpread;

	public VRInputSimulator()
	{
		CustomMinimumSize = new Vector2(520, 315); MouseFilter = MouseFilterEnum.Stop;
		TooltipText = "Drag the headset to turn/change height. Drag either controller to change hand spacing.";
	}
	public void Reset() { HeadYawDegrees = 0; HeadHeight = 1.7f; HandSpread = .45f; Changed?.Invoke(); QueueRedraw(); }
	public override void _GuiInput(InputEvent input)
	{
		(Vector2 head, Vector2 left, Vector2 right) = Points();
		if (input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
		{
			if (mouse.Pressed)
			{
				_dragMode = new Rect2(head - new Vector2(72, 42), new Vector2(144, 84)).HasPoint(mouse.Position) ? 1
					: mouse.Position.DistanceTo(left) < 42 ? 2 : mouse.Position.DistanceTo(right) < 42 ? 3 : 0;
				_dragStart = mouse.Position; _startYaw = HeadYawDegrees; _startHeight = HeadHeight; _startSpread = HandSpread;
			}
			else _dragMode = 0;
			AcceptEvent();
		}
		else if (input is InputEventMouseMotion motion && _dragMode != 0)
		{
			Vector2 delta = motion.Position - _dragStart;
			if (_dragMode == 1) { HeadYawDegrees = Mathf.Clamp(_startYaw + delta.X * .8f, -180, 180); HeadHeight = Mathf.Clamp(_startHeight - delta.Y / 150f, .5f, 2.5f); }
			else HandSpread = Mathf.Clamp(_startSpread + delta.X / 250f * (_dragMode == 2 ? -1f : 1f), .1f, 1.2f);
			Changed?.Invoke(); QueueRedraw(); AcceptEvent();
		}
	}
	private (Vector2, Vector2, Vector2) Points()
	{
		Vector2 head = new(Size.X * .5f, Mathf.Remap(HeadHeight, .5f, 2.5f, 185, 70));
		float spread = Mathf.Remap(HandSpread, .1f, 1.2f, 70, Size.X * .42f);
		return (head, new(head.X - spread, 230), new(head.X + spread, 230));
	}
	public override void _Draw()
	{
		(Vector2 head, Vector2 left, Vector2 right) = Points();
		DrawLine(new(head.X, 35), new(head.X, 278), new Color("344154"), 1);
		DrawLine(head, left, new Color("526277"), 2); DrawLine(head, right, new Color("526277"), 2);
		Rect2 headset = new(head - new Vector2(72, 42), new Vector2(144, 84)); DrawRect(headset, new Color("202936")); DrawRect(headset, new Color("0097ff"), false, 2);
		DrawCircle(head + Vector2.FromAngle(Mathf.DegToRad(HeadYawDegrees)) * 27, 6, new Color("40c878"));
		DrawController(left, "L"); DrawController(right, "R");
		DrawString(ThemeDB.FallbackFont, new Vector2(12, 20), $"HEAD  {HeadYawDegrees:0}°  •  {HeadHeight:0.00}m", HorizontalAlignment.Left, -1, 13, new Color("b8c6d9"));
		DrawString(ThemeDB.FallbackFont, new Vector2(12, 302), $"Drag headset: yaw + height    •    Drag controllers: spacing {HandSpread:0.00}m", HorizontalAlignment.Left, -1, 12, new Color("93a4ba"));
	}
	private void DrawController(Vector2 center, string text)
	{
		DrawCircle(center, 30, new Color("202936")); DrawArc(center, 31, 0, Mathf.Tau, 32, new Color("9b6cff"), 2);
		DrawString(ThemeDB.FallbackFont, center + new Vector2(-8, 6), text, HorizontalAlignment.Center, 16, 14, Colors.White);
	}
}
