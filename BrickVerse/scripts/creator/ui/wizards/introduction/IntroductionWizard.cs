// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Godot;
using BrickVerse.Creator.UI.Splashes;
using BrickVerse.Datamodel;

namespace BrickVerse.Creator.UI.Wizards;

/// <summary>
/// Interactive, resize-safe tour of the actual Creator interface. The four
/// dimmer panels leave the highlighted control usable while the tour is open.
/// </summary>
public partial class IntroductionWizard : Control
{
	private sealed record TutorialStep(string Title, string Body, string? TargetPath, string Tip);

	private static readonly TutorialStep[] Steps =
	[
		new(
			"Welcome to BrickVerse Creator",
			"This short interactive tour uses the real Creator interface. You can click highlighted controls, resize docks, and move through the tour at your own pace.",
			null,
			"Press Right Arrow for Next, Left Arrow for Back, or Escape to skip."
		),
		new(
			"Your 3D viewport",
			"This is where you build and inspect the world. Hold right mouse and move to look around, use WASD to fly, and scroll to change camera speed. Select objects directly in the viewport or through Explorer.",
			"Splitter/Center/Tabs/Container",
			"Try moving the camera after opening a world. Camera movement never changes animation keyframes."
		),
		new(
			"Explorer: the world hierarchy",
			"Explorer shows every instance in your world. Expand parents to understand hierarchy, drag instances to reparent them, right-click for actions, and double-click scripts to edit them.",
			"Splitter/Right/RightTabs/Explorer/Split/Explorer",
			"Use Explorer when an object is hard to click in the 3D viewport."
		),
		new(
			"Properties: edit the selection",
			"Properties updates for whatever you select. Change position, appearance, behavior, physics, assets, and script-facing values here. Changes are undoable and Team Create changes replicate to collaborators.",
			"Splitter/Right/RightTabs/Explorer/Split/Properties",
			"Drag the divider between Explorer and Properties to give either panel more room."
		),
		new(
			"Build and transform tools",
			"The ribbon contains Select, Move, Rotate, Scale, snapping, paint, materials, brushes, and Insert. Gizmos operate on the current selection; snapping keeps precise builds aligned.",
			"Ribbon/Buttons",
			"Use 1-4 keys and R/T to switch common transform tools quickly."
		),
		new(
			"Creator tools",
			"These shortcuts open Forge AI, Terrain, Animator, Toolbox, and Input Manager. Forge can help create project files, Terrain sculpts materials and height, and Animator edits rig keyframes in 3D.",
			"Ribbon/Buttons/QuickActions",
			"Hover any tool for a short description before opening it."
		),
		new(
			"Toolbox and project files",
			"Use the left dock to browse marketplace models and your project files. Insert public models from Toolbox, or switch to Files to create and organize scripts and other project assets.",
			"Splitter/Left",
			"Only insert assets you trust—models can contain scripts."
		),
		new(
			"Output and debugging",
			"Output shows prints, warnings, errors, and their script paths. Search and filter noisy logs, inspect runtime output while play-testing, and use the executor for quick Luau experiments.",
			"Splitter/Center/BottomTabs",
			"If the panel is too short, drag its divider upward."
		),
		new(
			"Play-test and collaborate",
			"Play launches your world from the normal spawn; Play Here starts near the Creator camera. Choose a player count for multiplayer tests. Collaborate and Session manage Team Create and collaborator presence.",
			"Menu/Layout/Margin/Layout/PlayOptions",
			"The Stop button appears while a test is active and closes its debug windows."
		),
		new(
			"You are ready to create",
			"Start with a small world, test often, watch Output for issues, and publish to save the current project state to the cloud. You can replay this tour from the Creator Home screen at any time.",
			null,
			"Everything here is also available through menus and configurable shortcuts."
		),
	];

	private const float SpotlightPadding = 8f;
	public static IntroductionWizard Singleton { get; private set; } = null!;

	// Kept for scene compatibility with the old slideshow scene.
	[Export] private IntroPage _firstPage = null!;
	[Export] private TextureRect _bannerImg = null!;
	[Export] private TabContainer _tabs = null!;

	private readonly ColorRect[] _dimmers = new ColorRect[4];
	private Panel _spotlight = null!;
	private PanelContainer _card = null!;
	private Label _eyebrow = null!;
	private Label _title = null!;
	private Label _body = null!;
	private Label _tip = null!;
	private Label _progress = null!;
	private Button _back = null!;
	private Button _next = null!;
	private Tween? _cardTween;
	private int _stepIndex;
	private float _focusAnimation = 1f;
	private Control? _target;

	public IntroductionWizard() => Singleton = this;

	public override void _Ready()
	{
		GetNode<Control>("Panel").Hide();
		SelfModulate = Colors.White;
		MouseFilter = MouseFilterEnum.Ignore;
		BuildGuidedOverlay();
		SetProcess(true);
		base._Ready();
	}

	private void BuildGuidedOverlay()
	{
		for (int i = 0; i < _dimmers.Length; i++)
		{
			_dimmers[i] = new ColorRect
			{
				Color = new Color(0.008f, 0.014f, 0.024f, 0.82f),
				MouseFilter = MouseFilterEnum.Stop,
				ZIndex = 1,
			};
			AddChild(_dimmers[i]);
		}

		_spotlight = new Panel { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 2 };
		StyleBoxFlat spotlightStyle = new()
		{
			BgColor = new Color(0, 0.59f, 1, 0.035f),
			BorderColor = new Color(0, 0.59f, 1, 1),
			BorderWidthLeft = 3,
			BorderWidthTop = 3,
			BorderWidthRight = 3,
			BorderWidthBottom = 3,
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10,
			ShadowColor = new Color(0, 0.59f, 1, 0.35f),
			ShadowSize = 10,
		};
		_spotlight.AddThemeStyleboxOverride("panel", spotlightStyle);
		AddChild(_spotlight);

		_card = new PanelContainer
		{
			CustomMinimumSize = new Vector2(410, 0),
			MouseFilter = MouseFilterEnum.Stop,
			ZIndex = 3,
			PivotOffset = new Vector2(205, 130),
		};
		StyleBoxFlat cardStyle = new()
		{
			ContentMarginLeft = 24,
			ContentMarginTop = 22,
			ContentMarginRight = 24,
			ContentMarginBottom = 20,
			BgColor = new Color(0.035f, 0.047f, 0.066f, 0.985f),
			BorderColor = new Color(0.12f, 0.19f, 0.28f, 1),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 14,
			CornerRadiusTopRight = 14,
			CornerRadiusBottomLeft = 14,
			CornerRadiusBottomRight = 14,
			ShadowColor = new Color(0, 0, 0, 0.55f),
			ShadowSize = 16,
		};
		_card.AddThemeStyleboxOverride("panel", cardStyle);
		AddChild(_card);

		VBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 10);
		_card.AddChild(content);

		_eyebrow = MakeLabel(12, new Color(0.18f, 0.7f, 1), true);
		_title = MakeLabel(25, Colors.White, true);
		_body = MakeLabel(15, new Color(0.78f, 0.83f, 0.9f), false);
		_tip = MakeLabel(13, new Color(0.46f, 0.7f, 0.91f), false);
		_body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_tip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_eyebrow);
		content.AddChild(_title);
		content.AddChild(_body);

		PanelContainer tipPanel = new();
		StyleBoxFlat tipStyle = new()
		{
			ContentMarginLeft = 12,
			ContentMarginTop = 10,
			ContentMarginRight = 12,
			ContentMarginBottom = 10,
			BgColor = new Color(0, 0.36f, 0.64f, 0.16f),
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
		};
		tipPanel.AddThemeStyleboxOverride("panel", tipStyle);
		tipPanel.AddChild(_tip);
		content.AddChild(tipPanel);

		HBoxContainer footer = new();
		footer.AddThemeConstantOverride("separation", 8);
		content.AddChild(footer);
		_progress = MakeLabel(12, new Color(0.5f, 0.57f, 0.68f), false);
		_progress.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_progress.VerticalAlignment = VerticalAlignment.Center;
		footer.AddChild(_progress);
		Button skip = MakeButton("Skip tour", false);
		_back = MakeButton("Back", false);
		_next = MakeButton("Next", true);
		skip.Pressed += Close;
		_back.Pressed += Prev;
		_next.Pressed += Next;
		footer.AddChild(skip);
		footer.AddChild(_back);
		footer.AddChild(_next);
	}

	private static Label MakeLabel(int size, Color color, bool uppercase)
	{
		Label label = new() { MouseFilter = MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		if (uppercase) label.Text = label.Text.ToUpperInvariant();
		return label;
	}

	private static Button MakeButton(string text, bool primary)
	{
		Button button = new() { Text = text, CustomMinimumSize = new Vector2(82, 38) };
		StyleBoxFlat normal = new()
		{
			ContentMarginLeft = 14,
			ContentMarginRight = 14,
			BgColor = primary ? new Color(0, 0.59f, 1) : new Color(0.08f, 0.1f, 0.14f),
			CornerRadiusTopLeft = 7,
			CornerRadiusTopRight = 7,
			CornerRadiusBottomLeft = 7,
			CornerRadiusBottomRight = 7,
		};
		button.AddThemeStyleboxOverride("normal", normal);
		return button;
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (!Visible) return;
		_focusAnimation = Mathf.Min(1, _focusAnimation + (float)delta * 4.5f);
		LayoutSpotlight();
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (!Visible || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
		switch (key.Keycode)
		{
			case Key.Escape: Close(); break;
			case Key.Left: Prev(); break;
			case Key.Right:
			case Key.Enter: Next(); break;
			default: return;
		}
		GetViewport().SetInputAsHandled();
	}

	public void Open()
	{
		_stepIndex = 0;
		Visible = true;
		ShowStep();
	}

	public void Close()
	{
		Visible = false;
		using FileAccess f = FileAccess.Open(CreatorInterface.IntroRanFile, FileAccess.ModeFlags.Write);
		f.StoreString("1");
		if (World.Current == null) StartupSplash.Singleton.Open();
	}

	public void Next()
	{
		if (_stepIndex >= Steps.Length - 1) { Close(); return; }
		_stepIndex++;
		ShowStep();
	}

	public void Prev()
	{
		if (_stepIndex == 0) return;
		_stepIndex--;
		ShowStep();
	}

	private void ShowStep()
	{
		TutorialStep step = Steps[_stepIndex];
		_eyebrow.Text = $"INTERACTIVE TOUR  •  {_stepIndex + 1} OF {Steps.Length}";
		_title.Text = step.Title;
		_body.Text = step.Body;
		_tip.Text = "TIP  •  " + step.Tip;
		_progress.Text = BuildProgressText();
		_back.Disabled = _stepIndex == 0;
		_next.Text = _stepIndex == Steps.Length - 1 ? "Finish" : "Next";
		_target = step.TargetPath == null ? null : GetParent().GetNodeOrNull<Control>(step.TargetPath);
		_focusAnimation = 0;
		AnimateCard();
		LayoutSpotlight();
		_next.GrabFocus();
	}

	private string BuildProgressText()
	{
		string progress = "";
		for (int i = 0; i < Steps.Length; i++) progress += i == _stepIndex ? "● " : "○ ";
		return progress.TrimEnd();
	}

	private void AnimateCard()
	{
		_cardTween?.Kill();
		_card.Scale = new Vector2(0.94f, 0.94f);
		_card.Modulate = new Color(1, 1, 1, 0);
		_cardTween = CreateTween().SetParallel(true).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		_cardTween.TweenProperty(_card, "scale", Vector2.One, 0.22);
		_cardTween.TweenProperty(_card, "modulate", Colors.White, 0.18);
	}

	private void LayoutSpotlight()
	{
		Vector2 viewportSize = Size;
		Rect2 focus;
		bool hasTarget = _target != null && IsInstanceValid(_target) && _target.IsVisibleInTree();
		if (hasTarget)
		{
			Rect2 targetRect = _target!.GetGlobalRect();
			Vector2 localOrigin = GetGlobalRect().Position;
			focus = new Rect2(targetRect.Position - localOrigin, targetRect.Size).Grow(SpotlightPadding);
			Rect2 expanded = focus.Grow(34);
			focus = new Rect2(
				expanded.Position.Lerp(focus.Position, _focusAnimation),
				expanded.Size.Lerp(focus.Size, _focusAnimation)
			);
		}
		else
		{
			focus = new Rect2(viewportSize * 0.5f, Vector2.Zero);
		}

		_spotlight.Visible = hasTarget;
		_spotlight.Position = focus.Position;
		_spotlight.Size = focus.Size;
		LayoutDimmers(viewportSize, hasTarget ? focus : new Rect2());
		LayoutCard(viewportSize, focus, hasTarget);
	}

	private void LayoutDimmers(Vector2 viewportSize, Rect2 hole)
	{
		if (hole.Size == Vector2.Zero)
		{
			SetRect(_dimmers[0], Vector2.Zero, viewportSize);
			for (int i = 1; i < 4; i++) SetRect(_dimmers[i], Vector2.Zero, Vector2.Zero);
			return;
		}

		float left = Mathf.Clamp(hole.Position.X, 0, viewportSize.X);
		float top = Mathf.Clamp(hole.Position.Y, 0, viewportSize.Y);
		float right = Mathf.Clamp(hole.End.X, 0, viewportSize.X);
		float bottom = Mathf.Clamp(hole.End.Y, 0, viewportSize.Y);
		SetRect(_dimmers[0], Vector2.Zero, new Vector2(viewportSize.X, top));
		SetRect(_dimmers[1], new Vector2(0, top), new Vector2(left, bottom - top));
		SetRect(_dimmers[2], new Vector2(right, top), new Vector2(viewportSize.X - right, bottom - top));
		SetRect(_dimmers[3], new Vector2(0, bottom), new Vector2(viewportSize.X, viewportSize.Y - bottom));
	}

	private void LayoutCard(Vector2 viewportSize, Rect2 focus, bool hasTarget)
	{
		Vector2 cardSize = _card.Size;
		if (cardSize.X < 100) cardSize = new Vector2(410, 280);
		const float margin = 22;
		Vector2 position;

		if (!hasTarget)
		{
			position = (viewportSize - cardSize) * 0.5f;
		}
		else if (viewportSize.X - focus.End.X >= cardSize.X + margin)
		{
			position = new Vector2(focus.End.X + margin, focus.GetCenter().Y - cardSize.Y * 0.5f);
		}
		else if (focus.Position.X >= cardSize.X + margin)
		{
			position = new Vector2(focus.Position.X - cardSize.X - margin, focus.GetCenter().Y - cardSize.Y * 0.5f);
		}
		else if (viewportSize.Y - focus.End.Y >= cardSize.Y + margin)
		{
			position = new Vector2(focus.GetCenter().X - cardSize.X * 0.5f, focus.End.Y + margin);
		}
		else
		{
			position = new Vector2(focus.GetCenter().X - cardSize.X * 0.5f, focus.Position.Y - cardSize.Y - margin);
		}

		_card.Position = new Vector2(
			Mathf.Clamp(position.X, margin, Mathf.Max(margin, viewportSize.X - cardSize.X - margin)),
			Mathf.Clamp(position.Y, margin, Mathf.Max(margin, viewportSize.Y - cardSize.Y - margin))
		);
	}

	private static void SetRect(Control control, Vector2 position, Vector2 size)
	{
		control.Position = position;
		control.Size = new Vector2(Mathf.Max(0, size.X), Mathf.Max(0, size.Y));
	}

	// Legacy IntroPage calls; retained so old serialized pages remain harmless.
	public void ShowImage(string image) { }
}
