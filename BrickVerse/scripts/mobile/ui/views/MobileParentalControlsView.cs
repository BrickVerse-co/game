using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileParentalControlsView : MobileViewBase
{
	private readonly Dictionary<string, CheckButton> _toggles = [];
	private VBoxContainer _content = null!;
	private LineEdit _pin = null!;
	private Label _status = null!;
	private SpinBox _screenTime = null!;
	private SpinBox _spending = null!;
	private OptionButton _filter = null!;
	private OptionButton _visibility = null!;

	public override void _Ready() => Build();

	public override void ShowView(object? args) => _ = LoadState();

	private void Build()
	{
		var root = new VBoxContainer
		{
			Name = "Layout",
			OffsetLeft = 16,
			OffsetTop = 54,
			OffsetRight = -16,
			OffsetBottom = -16,
		};
		root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		root.AddThemeConstantOverride("separation", 12);
		AddChild(root);
		var header = new HBoxContainer();
		root.AddChild(header);
		var back = Button("←", 44);
		back.Pressed += () => MobileUI.Singleton.SwitchTo(MobileViewEnum.Dev, MobileViewEnum.Dev);
		header.AddChild(back);
		var title = new Label
		{
			Text = "Parental controls",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		title.AddThemeFontSizeOverride("font_size", 26);
		header.AddChild(title);
		_status = new Label
		{
			Text = "Checking parental protection…",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		_status.AddThemeColorOverride("font_color", Color.FromHtml("A6AFBB"));
		root.AddChild(_status);
		var unlock = new Panel();
		unlock.Name = "Unlock";
		unlock.AddThemeStyleboxOverride("panel", Box("14171C", 14));
		root.AddChild(unlock);
		var unlockRow = new HBoxContainer
		{
			OffsetLeft = 14,
			OffsetTop = 14,
			OffsetRight = -14,
			OffsetBottom = -14,
		};
		unlockRow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		unlockRow.AddThemeConstantOverride("separation", 10);
		unlock.AddChild(unlockRow);
		_pin = new LineEdit
		{
			PlaceholderText = "4-digit parental PIN",
			Secret = true,
			MaxLength = 4,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 48),
		};
		unlockRow.AddChild(_pin);
		var unlockButton = Button("Unlock", 96);
		unlockButton.Pressed += () => _ = Unlock();
		unlockRow.AddChild(unlockButton);
		unlock.CustomMinimumSize = new Vector2(0, 76);
		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		root.AddChild(scroll);
		_content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_content.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(_content);
		AddHeading("Screen time");
		_screenTime = AddNumber("Daily limit (minutes)", 0, 1440, 15);
		AddHeading("Communication");
		AddToggle("inGameChatEnabled", "In-game chat", "Allow free-text chat inside worlds");
		AddToggle("quickChatEnabled", "Curated quick chat", "Allow the safe phrase wheel");
		AddToggle(
			"onSiteChatEnabled",
			"Messages and site chat",
			"Allow social messaging and comments"
		);
		_filter = AddChoice("Chat filter", ["Friends only", "Same age group", "Unrestricted"]);
		AddHeading("Social and safety");
		AddToggle(
			"friendshipRequiresApproval",
			"Approve new friends",
			"A parent reviews incoming requests"
		);
		AddToggle(
			"groupApprovalRequired",
			"Approve guild memberships",
			"A parent reviews guild joins"
		);
		AddToggle(
			"childCanManageBlocks",
			"Manage block list",
			"Allow this account to block or unblock users"
		);
		AddToggle("accountLocked", "Lock account", "Prevent sign-in and play until unlocked");
		AddHeading("Spending");
		_spending = AddNumber("Daily Cubes limit", 0, 1000000, 10);
		AddToggle(
			"purchaseApprovalRequired",
			"Approve purchases",
			"Require parent approval before spending"
		);
		AddToggle("economyTradingEnabled", "Marketplace trading", "Allow player-to-player trading");
		AddHeading("Privacy");
		_visibility = AddChoice("Profile visibility", ["Everyone", "Friends only", "Private"]);
		var save = Button("Save parental controls", 0);
		save.CustomMinimumSize = new Vector2(0, 52);
		save.AddThemeStyleboxOverride("normal", Box("0097FF", 12));
		save.AddThemeStyleboxOverride("hover", Box("20A4FF", 12));
		save.Pressed += () => _ = Save();
		_content.AddChild(save);
		_content.Visible = false;
	}

	private async System.Threading.Tasks.Task LoadState()
	{
		try
		{
			using JsonDocument doc = await BVAPI.GetJson("/v3/auth/me/parental-controls");
			JsonElement root = doc.RootElement;
			bool unlocked = root.TryGetProperty("unlocked", out JsonElement u) && u.GetBoolean();
			_content.Visible = unlocked;
			GetNode<Control>("Layout/Unlock").Visible = !unlocked;
			if (!unlocked)
			{
				_status.Text = "Enter the parental PIN to manage protected settings.";
				return;
			}
			_status.Text = "Settings remain protected when you leave this screen.";
			if (root.TryGetProperty("controls", out JsonElement controls))
				Apply(controls);
		}
		catch (Exception ex)
		{
			_status.Text = Friendly(ex);
			BV.PrintErr(ex);
		}
	}

	private async System.Threading.Tasks.Task Unlock()
	{
		if (_pin.Text.Length != 4)
		{
			_status.Text = "Enter the four-digit parental PIN.";
			return;
		}
		try
		{
			using JsonDocument _ = await BVAPI.SendJson(
				HttpMethod.Post,
				"/v3/auth/me/verify-parental-pin",
				JsonSerializer.Serialize(new { pin = _pin.Text })
			);
			_pin.Text = "";
			await LoadState();
		}
		catch (Exception ex)
		{
			_status.Text = "That PIN was not accepted.";
			BV.PrintErr(ex);
		}
	}

	private async System.Threading.Tasks.Task Save()
	{
		var payload = new Dictionary<string, object?>
		{
			["screenTimeLimit"] = (int)_screenTime.Value,
			["spendingLimit"] = (int)_spending.Value,
			["chatFilterLevel"] = new[] { "FRIENDS_ONLY", "SAME_AGE_GROUP", "UNRESTRICTED" }[
				_filter.Selected
			],
			["visibility"] = new[] { "EVERYONE", "FRIENDS_ONLY", "PRIVATE" }[_visibility.Selected],
		};
		foreach ((string key, CheckButton toggle) in _toggles)
			payload[key] = toggle.ButtonPressed;
		try
		{
			using JsonDocument _ = await BVAPI.SendJson(
				HttpMethod.Put,
				"/v3/auth/me/parental-controls",
				JsonSerializer.Serialize(payload)
			);
			_status.Text = "Parental controls saved.";
		}
		catch (Exception ex)
		{
			_status.Text = "Unlock expired. Enter the PIN again to save.";
			_content.Visible = false;
			GetNode<Control>("Layout/Unlock").Visible = true;
			BV.PrintErr(ex);
		}
	}

	private void Apply(JsonElement c)
	{
		_screenTime.Value = Number(c, "screenTimeLimit");
		_spending.Value = Number(c, "spendingLimit");
		foreach ((string key, CheckButton toggle) in _toggles)
			if (
				c.TryGetProperty(key, out JsonElement value)
				&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
			)
				toggle.ButtonPressed = value.GetBoolean();
		_filter.Selected = Index(
			c,
			"chatFilterLevel",
			["FRIENDS_ONLY", "SAME_AGE_GROUP", "UNRESTRICTED"]
		);
		_visibility.Selected = Index(c, "visibility", ["EVERYONE", "FRIENDS_ONLY", "PRIVATE"]);
	}

	private void AddHeading(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 20);
		label.AddThemeColorOverride("font_color", Color.FromHtml("F5F7FA"));
		_content.AddChild(label);
	}

	private void AddToggle(string key, string title, string detail)
	{
		var panel = new Panel { CustomMinimumSize = new Vector2(0, 70) };
		panel.AddThemeStyleboxOverride("panel", Box("14171C", 12));
		_content.AddChild(panel);
		var row = new HBoxContainer
		{
			OffsetLeft = 14,
			OffsetTop = 10,
			OffsetRight = -14,
			OffsetBottom = -10,
		};
		row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		panel.AddChild(row);
		var copy = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		copy.AddChild(new Label { Text = title });
		var sub = new Label { Text = detail };
		sub.AddThemeColorOverride("font_color", Color.FromHtml("A6AFBB"));
		sub.AddThemeFontSizeOverride("font_size", 13);
		copy.AddChild(sub);
		row.AddChild(copy);
		var toggle = new CheckButton();
		row.AddChild(toggle);
		_toggles[key] = toggle;
	}

	private SpinBox AddNumber(string label, double min, double max, double step)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill });
		var input = new SpinBox
		{
			MinValue = min,
			MaxValue = max,
			Step = step,
			CustomMinimumSize = new Vector2(125, 44),
		};
		row.AddChild(input);
		_content.AddChild(row);
		return input;
	}

	private OptionButton AddChoice(string label, string[] options)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill });
		var choice = new OptionButton { CustomMinimumSize = new Vector2(165, 44) };
		foreach (string option in options)
			choice.AddItem(option);
		row.AddChild(choice);
		_content.AddChild(row);
		return choice;
	}

	private static Button Button(string text, float width)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(width, 44) };
		return button;
	}

	private static StyleBoxFlat Box(string color, int radius) =>
		new()
		{
			BgColor = Color.FromHtml(color),
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomLeft = radius,
			CornerRadiusBottomRight = radius,
		};

	private static double Number(JsonElement root, string key) =>
		root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.Number
			? value.GetDouble()
			: 0;

	private static int Index(JsonElement root, string key, string[] values)
	{
		string current = root.TryGetProperty(key, out JsonElement value)
			? value.GetString() ?? ""
			: "";
		int index = Array.IndexOf(values, current);
		return index < 0 ? 0 : index;
	}

	private static string Friendly(Exception ex) =>
		ex.Message.Contains("400")
			? "No parental PIN is set. A parent can add one in account Security settings."
		: ex.Message.Contains("404") ? "This account is not linked to a Parental Control Center."
		: "Parental controls could not be loaded.";
}
