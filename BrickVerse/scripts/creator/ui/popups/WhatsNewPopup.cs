// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Creator.Settings;
using BrickVerse.Shared;
using Godot;
using Godot.Collections;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class WhatsNewPopup : PopupWindowBase
{
	private const string FeedUrl = "https://raw.githubusercontent.com/BrickVerse-co/game/main/BrickVerse/assets/creator/whats-new.json";
	private const string LocalFeedPath = "res://assets/creator/whats-new.json";
	private const string SeenIdPath = "user://creator/whats-new-seen.txt";
	private const string ScenePath = "res://scenes/creator/popups/whats_new.tscn";
	private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

	private Dictionary _feed = [];

	public WhatsNewPopup()
	{
		Title = "What’s New in BrickVerse Creator";
		MinSize = new Vector2I(620, 500);
		Size = new Vector2I(760, 640);
		Unresizable = false;
	}

	private void SetFeed(Dictionary feed) => _feed = feed;

	public static async void CheckForUpdates()
	{
		if (!CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.ShowWhatsNew)) return;
		await Task.Delay(1400);
		await ShowLatestAsync(false);
	}

	public static async void ShowLatest() => await ShowLatestAsync(true);

	private static async Task ShowLatestAsync(bool force)
	{
		try
		{
			Dictionary? feed = await LoadFeedAsync();
			if (feed == null) return;

			string id = ReadString(feed, "id");
			if (!force && id.Length > 0 && Godot.FileAccess.FileExists(SeenIdPath))
			{
				using Godot.FileAccess seen = Godot.FileAccess.Open(SeenIdPath, Godot.FileAccess.ModeFlags.Read);
				if (seen.GetAsText().Trim() == id) return;
			}

			WhatsNewPopup popup = GD.Load<PackedScene>(ScenePath).Instantiate<WhatsNewPopup>();
			popup.SetFeed(feed);
			CreatorService.Interface.PopupWindow(popup);
			if (id.Length > 0)
			{
				using Godot.FileAccess seen = Godot.FileAccess.Open(SeenIdPath, Godot.FileAccess.ModeFlags.Write);
				seen.StoreString(id);
			}
		}
		catch (Exception error)
		{
			BV.PrintErr("Could not display Creator What's New: ", error.Message);
		}
	}

	private static async Task<Dictionary?> LoadFeedAsync()
	{
		string json;
		try
		{
			json = await Http.GetStringAsync(FeedUrl);
		}
		catch (Exception error)
		{
			BV.Print("Creator What's New feed unavailable; using packaged release notes: ", error.Message);
			json = Godot.FileAccess.FileExists(LocalFeedPath)
				? Godot.FileAccess.GetFileAsString(LocalFeedPath)
				: string.Empty;
		}

		if (string.IsNullOrWhiteSpace(json)) return null;
		Variant parsed = Json.ParseString(json);
		return parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : null;
	}

	public override void _Ready()
	{
		MarginContainer margin = new() { Name = "ReleaseNotesLayout" };
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_bottom", 24);
		AddChild(margin);

		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 12);
		margin.AddChild(layout);

		Label eyebrow = new() { Text = ReadString(_feed, "date", "LATEST UPDATE").ToUpperInvariant() };
		eyebrow.AddThemeColorOverride("font_color", Color.FromHtml("#0097FF"));
		eyebrow.AddThemeFontSizeOverride("font_size", 13);
		layout.AddChild(eyebrow);

		Label title = new() { Text = ReadString(_feed, "title", "What’s new") };
		title.AddThemeFontSizeOverride("font_size", 27);
		layout.AddChild(title);

		Label subtitle = new() { Text = ReadString(_feed, "subtitle"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		subtitle.AddThemeFontSizeOverride("font_size", 16);
		layout.AddChild(subtitle);
		layout.AddChild(new HSeparator());

		ScrollContainer scroll = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		layout.AddChild(scroll);
		VBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 18);
		content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(content);

		if (_feed.TryGetValue("sections", out Variant sectionsValue) && sectionsValue.VariantType == Variant.Type.Array)
		{
			foreach (Variant sectionValue in sectionsValue.AsGodotArray())
			{
				if (sectionValue.VariantType != Variant.Type.Dictionary) continue;
				Dictionary section = sectionValue.AsGodotDictionary();
				PanelContainer cardPanel = new() { CustomMinimumSize = new Vector2(0, 116) };
				MarginContainer cardMargin = new();
				cardMargin.AddThemeConstantOverride("margin_left", 18);
				cardMargin.AddThemeConstantOverride("margin_top", 16);
				cardMargin.AddThemeConstantOverride("margin_right", 18);
				cardMargin.AddThemeConstantOverride("margin_bottom", 16);
				RichTextLabel card = new() { BbcodeEnabled = true, FitContent = true, CustomMinimumSize = new Vector2(0, 100) };
				card.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
				string heading = EscapeBbcode(ReadString(section, "title", "Update"));
				string body = EscapeBbcode(ReadString(section, "body"));
				string text = $"[font_size=19][b]{heading}[/b][/font_size]\n{body}";
				if (section.TryGetValue("items", out Variant itemsValue) && itemsValue.VariantType == Variant.Type.Array)
				{
					foreach (Variant item in itemsValue.AsGodotArray())
						text += $"\n[color=#0097FF]•[/color]  {EscapeBbcode(item.AsString())}";
				}
				card.Text = text;
				cardMargin.AddChild(card);
				cardPanel.AddChild(cardMargin);
				content.AddChild(cardPanel);
			}
		}

		HBoxContainer footer = new() { Alignment = BoxContainer.AlignmentMode.End, CustomMinimumSize = new Vector2(0, 48) };
		footer.AddThemeConstantOverride("separation", 8);
		layout.AddChild(footer);
		CheckBox doNotShow = new() { Text = "Don't show this again", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		doNotShow.Toggled += disabled =>
		{
			if (disabled)
				CreatorSettingsService.Instance.Set(CreatorSettingKeys.Creator.ShowWhatsNew, false);
		};
		footer.AddChild(doNotShow);
		if (_feed.TryGetValue("links", out Variant linksValue) && linksValue.VariantType == Variant.Type.Array)
		{
			foreach (Variant linkValue in linksValue.AsGodotArray())
			{
				if (linkValue.VariantType != Variant.Type.Dictionary) continue;
				Dictionary link = linkValue.AsGodotDictionary();
				string url = ReadString(link, "url");
				Button button = CreateActionButton(ReadString(link, "label", "Learn more"), false);
				button.Pressed += () => OS.ShellOpen(url);
				footer.AddChild(button);
			}
		}
		Button done = CreateActionButton("Got it", true);
		done.Pressed += () =>
		{
			if (doNotShow.ButtonPressed)
				CreatorSettingsService.Instance.Set(CreatorSettingKeys.Creator.ShowWhatsNew, false);
			QueueFree();
		};
		footer.AddChild(done);

		base._Ready();
	}

	private static string EscapeBbcode(string value) => value.Replace("[", "[lb]");

	private static Button CreateActionButton(string text, bool primary)
	{
		Button button = new() { Text = text, CustomMinimumSize = new Vector2(148, 40) };
		if (primary) button.AddThemeColorOverride("font_color", Colors.White);
		return button;
	}

	private static string ReadString(Dictionary dictionary, string key, string fallback = "") =>
		dictionary.TryGetValue(key, out Variant value) ? value.AsString() : fallback;
}
