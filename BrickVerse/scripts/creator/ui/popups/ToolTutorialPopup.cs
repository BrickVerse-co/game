// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System.Collections.Generic;
using BrickVerse.Datamodel.Creator;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class ToolTutorialPopup : PopupWindowBase
{
	private sealed record Guide(string Title, string Summary, string[] Steps, string Example, string Tip);
	private const string SeenPath = "user://creator/tool_guides.cfg";
	private static readonly Dictionary<int, Guide> Guides = new()
	{
		[0] = new("Data Store Explorer", "Inspect and edit data produced by local play tests without writing a temporary debug script.",
			["Search by store, key, or value.", "Select a row and choose Edit selected value.", "Enter valid JSON, then confirm to save.", "Use New key to create test data before running a save system."],
			"Example: create store PlayerData, key test-user, value { \"coins\": 250, \"level\": 4 }.",
			"Use development data only. Verify destructive changes before deleting a key."),
		[1] = new("Localization Manager", "Edit translated strings stored with the current project.",
			["Add a row for each locale and key.", "Keep the same key across every language.", "Edit the translated text directly in the table.", "Save, then test the locale in a client session."],
			"Example: en-US + menu.play + Play, then es-ES + menu.play + Jugar.",
			"Use dotted names such as inventory.empty to keep large projects organized."),
		[2] = new("Instance Icon Manager", "Give classes or individual instances recognizable Explorer icons.",
			["Select a class row to change every instance of that class.", "Select an Individual row to override one object only.", "Choose Override and select an image.", "Use Reset to return to the inherited icon."],
			"Example: assign a steering-wheel icon to VehicleController, then a warning icon to one damaged car.",
			"Simple, high-contrast SVG icons remain readable at Explorer sizes."),
		[3] = new("World Backups & Restore", "Create snapshots and compare the project against an earlier saved state.",
			["Choose Backup now before a risky edit.", "Select a snapshot to preview added, modified, and deleted files.", "Review the diff summary.", "Choose Rollback only after confirming the selected snapshot."],
			"Example: make a backup before replacing terrain or running a large scene migration.",
			"Restoring creates a safety backup first, but you should still reopen the project afterward."),
		[4] = new("Collision Groups Editor", "Control which physics groups can interact through a symmetric layer matrix.",
			["Assign objects to collision layers in Properties.", "Find that layer’s row in the matrix.", "Check a target column to enable collisions between both groups.", "Run a play test and verify contacts, triggers, and raycasts."],
			"Example: put Players on layer 1 and Projectiles on layer 2. Enable 1↔2, then disable Projectile↔Projectile to reduce unnecessary contacts.",
			"The number beside each layer shows how many physical objects currently belong to it."),
		[5] = new("Script Analysis & Activity", "Review Luau diagnostics and see every script currently present in the world.",
			["Choose Refresh after editing or adding scripts.", "Use Analysis to review errors, warnings, and hints.", "Double-click a diagnostic to open its file and line.", "Use Script Activity to audit script type, source size, and scene location."],
			"Example: double-click an ‘unknown global’ warning, fix the name in the editor, then Refresh to confirm it is gone.",
			"Start with errors, then warnings. Hints usually describe cleanup or maintainability improvements."),
	};

	public static void ShowFor(int tab, bool force = false)
	{
		if (!Guides.TryGetValue(tab, out Guide? guide)) return;
		ConfigFile config = new();
		config.Load(SeenPath);
		if (!force && config.GetValue("seen", tab.ToString(), false).AsBool()) return;
		config.SetValue("seen", tab.ToString(), true);
		config.Save(SeenPath);
		CreatorService.Interface.PopupWindow(new ToolTutorialPopup(guide));
	}

	private ToolTutorialPopup(Guide guide)
	{
		Title = guide.Title + " Guide";
		Size = new Vector2I(650, 540);
		MinSize = new Vector2I(520, 420);
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		Transient = true;
		Exclusive = false;
		Build(guide);
	}

	private void Build(Guide guide)
	{
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 24);
		VBoxContainer root = new(); root.AddThemeConstantOverride("separation", 14); margin.AddChild(root); AddChild(margin);
		Label eyebrow = new() { Text = "QUICK START", Modulate = new Color("63b3ff") }; eyebrow.AddThemeFontSizeOverride("font_size", 11); root.AddChild(eyebrow);
		Label title = new() { Text = guide.Title }; title.AddThemeFontSizeOverride("font_size", 25); root.AddChild(title);
		Label summary = Body(guide.Summary, "aeb8c8"); root.AddChild(summary);
		root.AddChild(new HSeparator());
		Label heading = new() { Text = "How to use it" }; heading.AddThemeFontSizeOverride("font_size", 16); root.AddChild(heading);
		for (int i = 0; i < guide.Steps.Length; i++) root.AddChild(Body($"{i + 1}.  {guide.Steps[i]}", "d8dee8"));
		PanelContainer example = Card("Example", guide.Example, new Color("172635")); root.AddChild(example);
		PanelContainer tip = Card("Tip", guide.Tip, new Color("24231a")); root.AddChild(tip);
		Control spacer = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; root.AddChild(spacer);
		HBoxContainer actions = new();
		CheckButton showAgain = new() { Text = "Show this guide next time", ButtonPressed = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		showAgain.Toggled += enabled => { if (enabled) { ConfigFile c = new(); c.Load(SeenPath); foreach ((int key, Guide value) in Guides) if (value == guide) c.SetValue("seen", key.ToString(), false); c.Save(SeenPath); } };
		actions.AddChild(showAgain);
		Button done = new() { Text = "Start using tool", CustomMinimumSize = new Vector2(140, 38) }; done.Pressed += QueueFree; actions.AddChild(done); root.AddChild(actions);
	}

	private static Label Body(string text, string color) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(color) };
	private static PanelContainer Card(string heading, string text, Color color)
	{
		PanelContainer panel = new(); panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = color, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 11, ContentMarginBottom = 11 });
		VBoxContainer box = new(); Label title = new() { Text = heading }; title.AddThemeFontSizeOverride("font_size", 14); box.AddChild(title); box.AddChild(Body(text, "b9c5d5")); panel.AddChild(box); return panel;
	}
}
