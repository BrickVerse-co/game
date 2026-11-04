// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Linq;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using Godot;

namespace BrickVerse.Creator.Utils;

/// <summary>Canonical website destinations used by Creator.</summary>
public static class CreatorWebLinks
{
	public const string Docs = "https://brickverse.gg/creator/docs";
	public const string Community = "https://brickverse.gg/forum";
	public const string Support = "https://brickverse.gg/help";

	/// <summary>
	/// Uses the stable class resolver rather than embedding the documentation category.
	/// The website redirects this URL to the current canonical article.
	/// </summary>
	public static string ClassDocs(string className) =>
		$"{Docs}/api/class/{Uri.EscapeDataString(className)}";

	public static string SearchDocs(string query) =>
		$"{Docs}?q={Uri.EscapeDataString(query.Trim())}";

	public static void OpenDocs() => Open(Docs);

	public static void OpenSelectionDocs()
	{
		Instance? selected = World.Current?.CreatorContext.Selections.SelectedInstances.LastOrDefault();
		Open(selected == null ? Docs : ClassDocs(selected.ClassName));
	}

	public static void OpenSearch()
	{
		Window window = new()
		{
			Title = "Search Creator Documentation",
			InitialPosition = Window.WindowInitialPosition.CenterMainWindowScreen,
			Size = new Vector2I(560, 180),
			MinSize = new Vector2I(440, 160),
			Transient = true,
			Exclusive = true,
		};
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_top", 18);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		window.AddChild(margin);
		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 10);
		margin.AddChild(layout);
		layout.AddChild(new Label { Text = "Search classes, properties, methods, events, and guides." });
		LineEdit input = new() { PlaceholderText = "Example: raycasting, PlayerAdded, UI layout…", ClearButtonEnabled = true };
		layout.AddChild(input);
		HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.End };
		Button cancel = new() { Text = "Cancel" };
		Button search = new() { Text = "Search Documentation", Disabled = true };
		actions.AddChild(cancel); actions.AddChild(search); layout.AddChild(actions);
		void Submit()
		{
			string query = input.Text.Trim();
			if (query.Length == 0) return;
			Open(SearchDocs(query));
			window.QueueFree();
		}
		input.TextChanged += value => search.Disabled = string.IsNullOrWhiteSpace(value);
		input.TextSubmitted += _ => Submit();
		search.Pressed += Submit;
		cancel.Pressed += window.QueueFree;
		window.CloseRequested += window.QueueFree;
		CreatorService.Interface.PopupWindow(window);
		input.CallDeferred(Control.MethodName.GrabFocus);
	}

	private static void Open(string url)
	{
		Error error = OS.ShellOpen(url);
		if (error != Error.Ok)
			CreatorService.Interface.PopupAlert($"Could not open {url}", "Browser unavailable");
	}
}
