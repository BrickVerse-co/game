// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;

namespace BrickVerse.Creator.UI.Popups;

public static class CreatorBetaFeatures
{
	private const string ConfigPath = "user://creator_beta_features.cfg";
	public static bool IsEnabled(string flag)
	{
		ConfigFile config = new();
		config.Load(ConfigPath);
		return config.GetValue("features", flag, false).AsBool();
	}

	public static void SetEnabled(string flag, bool enabled)
	{
		ConfigFile config = new();
		config.Load(ConfigPath);
		config.SetValue("features", flag, enabled);
		config.Save(ConfigPath);
	}
}

public sealed partial class BetaFeaturesPopup : Window
{
	private static readonly (string Flag, string Name, string Description, bool Restart)[] Features =
	[
		("ForgeInlineActions", "Forge inline actions", "Allow Forge to surface experimental editor-aware actions and richer generated API context.", false),
		("SmartStreaming", "Smart streaming preview", "Preview adaptive world streaming and occlusion decisions during play tests.", true),
		("AnimationIK", "Animator IK controls", "Enable experimental inverse-kinematics authoring controls in Animator.", true),
		("TeamCreatePresenceV2", "Team Create presence v2", "Try reduced-bandwidth collaborator cameras and presence rendering.", true),
	];

	public BetaFeaturesPopup()
	{
		Title = "Beta Features";
		Size = new Vector2I(680, 520);
		MinSize = new Vector2I(560, 420);
		Transient = true;
		Exclusive = false;
	}

	public override void _Ready()
	{
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 14);
		AddChild(margin);
		VBoxContainer layout = new();
		margin.AddChild(layout);
		layout.AddChild(new Label { Text = "Creator Beta Features", ThemeTypeVariation = "HeaderLarge" });
		layout.AddChild(new Label { Text = "Opt in to experimental tools. Beta features may change or require restarting Creator.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
		layout.AddChild(new HSeparator());
		ScrollContainer scroll = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		layout.AddChild(scroll);
		VBoxContainer list = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(list);
		foreach ((string flag, string name, string description, bool restart) in Features)
		{
			PanelContainer panel = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			MarginContainer cardMargin = new();
			cardMargin.AddThemeConstantOverride("margin_left", 12);
			cardMargin.AddThemeConstantOverride("margin_top", 10);
			cardMargin.AddThemeConstantOverride("margin_right", 12);
			cardMargin.AddThemeConstantOverride("margin_bottom", 10);
			panel.AddChild(cardMargin);
			HBoxContainer row = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			cardMargin.AddChild(row);
			VBoxContainer copy = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			copy.AddChild(new Label { Text = name });
			copy.AddChild(new Label { Text = description + (restart ? "  Restart required." : ""), AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("9aa8ba") });
			row.AddChild(copy);
			CheckButton toggle = new() { ButtonPressed = CreatorBetaFeatures.IsEnabled(flag), TooltipText = "Enroll or unenroll" };
			toggle.Toggled += enabled => CreatorBetaFeatures.SetEnabled(flag, enabled);
			row.AddChild(toggle);
			list.AddChild(panel);
		}
		Button done = new() { Text = "Done", CustomMinimumSize = new Vector2(100, 36), SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
		done.Pressed += QueueFree;
		layout.AddChild(done);
		CloseRequested += QueueFree;
	}
}
