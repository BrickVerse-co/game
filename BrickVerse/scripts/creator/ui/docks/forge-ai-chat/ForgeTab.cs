// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Creator.UI.TextEditor;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;

namespace BrickVerse.Creator.UI;

public partial class ForgeTab : VBoxContainer
{
	private ForgeProviderSettings _settings = new();
	private readonly ForgeChatClient _chatClient = new();
	private readonly List<ForgeChatMessage> _history = [];
	private readonly List<ForgeConversation> _conversations = [];
	private ForgeConversation? _activeConversation;
	private bool _isBusy;

	private VBoxContainer _messages = null!;
	private VBoxContainer _historyContainer = null!;
	private Control _welcome = null!;
	private Control _suggestions = null!;
	private Control _providerNotice = null!;
	private ScrollContainer _chatScroll = null!;
	private Button _newChatButton = null!;
	private Button _settingsButton = null!;
	private Button _sendButton = null!;
	private TextEdit _promptEditor = null!;
	private OptionButton _composerProvider = null!;
	private MenuButton _contextMenu = null!;
	private MenuButton _historyMenu = null!;
	private ProgressBar _progress = null!;
	private Label _activityLabel = null!;
	private PanelContainer? _activityCard;
	private readonly List<Button> _rollbackButtons = [];
	private Tween? _activityPulse;

	private Window _providerWindow = null!;
	private OptionButton _providerOption = null!;
	private LineEdit _endpointEdit = null!;
	private LineEdit _apiKeyEdit = null!;
	private LineEdit _modelEdit = null!;
	private CheckBox _storeKeyCheck = null!;
	private Button _testButton = null!;
	private Button _saveButton = null!;

	public World? Root { get; private set; }

	public override void _Ready()
	{
		_messages = GetNode<VBoxContainer>("ChatScroll/ChatMargin/Messages");
		_chatScroll = GetNode<ScrollContainer>("ChatScroll");
		_newChatButton = GetNode<Button>("Header/Row/NewChat");
		_settingsButton = GetNode<Button>("Header/Row/Settings");
		_sendButton = GetNode<Button>("Composer/Layout/Actions/Send");
		_promptEditor = GetNode<TextEdit>("Composer/Layout/Prompt");
		_composerProvider = GetNode<OptionButton>("Composer/Layout/ContextRow/Model");
		_contextMenu = GetNode<MenuButton>("Composer/Layout/ContextRow/Context");
		BuildEnhancedHeader();
		PopulateModelPicker();

		_providerWindow = GetNode<Window>("ProviderSettings");
		_providerOption = GetNode<OptionButton>("ProviderSettings/Margin/Layout/Provider");
		_endpointEdit = GetNode<LineEdit>("ProviderSettings/Margin/Layout/Endpoint");
		_apiKeyEdit = GetNode<LineEdit>("ProviderSettings/Margin/Layout/ApiKey");
		_modelEdit = GetNode<LineEdit>("ProviderSettings/Margin/Layout/Model");
		_storeKeyCheck = GetNode<CheckBox>("ProviderSettings/Margin/Layout/StoreKey");
		_testButton = GetNode<Button>("ProviderSettings/Margin/Layout/Buttons/Test");
		_saveButton = GetNode<Button>("ProviderSettings/Margin/Layout/Buttons/Save");

		_welcome = GetNode<Control>("ChatScroll/ChatMargin/Messages/Welcome");
		_suggestions = GetNode<Control>("ChatScroll/ChatMargin/Messages/Suggestions");
		_providerNotice = GetNode<Control>("ChatScroll/ChatMargin/Messages/ProviderNotice");

		_historyContainer = new VBoxContainer();
		_historyContainer.Name = "History";
		_historyContainer.AddThemeConstantOverride("separation", 12);
		_messages.AddChild(_historyContainer);

		_settings = ForgeProviderSettingsStore.Load();
		_conversations.AddRange(ForgeConversationStore.Load().OrderByDescending(static chat => chat.UpdatedAt));
		Root = World.Current;

		_wireSuggestionButtons();
		WireEvents();
		ApplySettingsToUi(_settings);
		UpdateComposerProviderSelection();
		RefreshHistoryMenu();
		UpdateSendState();
		UpdateEmptyState();

		base._Ready();
	}

	public static string GetConsoleSnippet(int maxChars = 2000)
	{
		RichTextLabel? richLabel = DebugConsole.Singleton?.GetNodeOrNull<RichTextLabel>("VBoxContainer/RichTextLabel");
		if (richLabel == null || string.IsNullOrWhiteSpace(richLabel.Text))
		{
			return string.Empty;
		}

		string text = richLabel.Text.Trim();
		return text.Length <= maxChars ? text : text[^maxChars..];
	}

	private void WireEvents()
	{
		_newChatButton.Pressed += ResetConversation;
		_settingsButton.Pressed += OpenSettings;
		_sendButton.Pressed += async () => await SendCurrentPromptAsync();
		_promptEditor.TextChanged += UpdateSendState;
		_promptEditor.GuiInput += OnPromptGuiInput;

		_providerWindow.CloseRequested += _providerWindow.Hide;
		_providerOption.ItemSelected += OnSettingsProviderChanged;
		_testButton.Pressed += async () => await TestConnectionAsync();
		_saveButton.Pressed += SaveSettings;
		_composerProvider.ItemSelected += OnComposerProviderChanged;
		_historyMenu.GetPopup().IdPressed += OnHistorySelected;

		PopupMenu contextPopup = _contextMenu.GetPopup();
		contextPopup.HideOnCheckableItemSelection = false;
		contextPopup.IdPressed += OnContextPopupIdPressed;
	}

	private void _wireSuggestionButtons()
	{
		GetNode<Button>("ChatScroll/ChatMargin/Messages/Suggestions/Script").Pressed += async () => await SendSuggestionAsync("Create a day and night cycle");
		GetNode<Button>("ChatScroll/ChatMargin/Messages/Suggestions/Explain").Pressed += async () => await SendSuggestionAsync("Explain the selected script");
		GetNode<Button>("ChatScroll/ChatMargin/Messages/Suggestions/Fix").Pressed += async () => await SendSuggestionAsync("Fix errors from the Console");
	}

	private void OnContextPopupIdPressed(long id)
	{
		PopupMenu popup = _contextMenu.GetPopup();
		int idx = popup.GetItemIndex((int)id);
		if (idx >= 0)
		{
			popup.SetItemChecked(idx, !popup.IsItemChecked(idx));
		}
	}

	private async Task SendSuggestionAsync(string text)
	{
		_promptEditor.Text = text;
		await SendCurrentPromptAsync();
	}

	private async void OnPromptGuiInput(InputEvent @event)
	{
		if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
		{
			return;
		}

		if ((keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter) && !keyEvent.ShiftPressed)
		{
			AcceptEvent();
			await SendCurrentPromptAsync();
		}
	}

	private async Task SendCurrentPromptAsync()
	{
		if (_isBusy)
		{
			return;
		}

		string prompt = _promptEditor.Text.Trim();
		if (string.IsNullOrWhiteSpace(prompt))
		{
			return;
		}

		Root = World.Current;
		if (Root == null)
		{
			PushSystemMessage("Open a world before using Forge.", isError: true);
			return;
		}

		if (!ValidateConfiguration(_settings, out string configError))
		{
			PushSystemMessage(configError, isError: true);
			OpenSettings();
			return;
		}

		EnsureActiveConversation(prompt);
		_isBusy = true;
		SetActivity("Preparing context…", 18);
		UpdateSendState();
		HideEmptyState();
		PushChatMessage("You", prompt, isUser: true);
		_promptEditor.Text = string.Empty;
		CreatorService.Interface.StatusBar?.SetStatus("Forge is working...");
		SetActivity("Thinking and using Creator tools…", 55);

		try
		{
			ForgeToolExecutor executor = new(Root);
			string contextSummary = BuildContextSummary(executor);
			ForgeCompletionResult result = await _chatClient.CompleteTurnAsync(_settings, _history, prompt, contextSummary, executor, UpdateInlineActivityAsync, ConfirmLuauExecutionAsync);
			_history.AddRange(result.TranscriptDelta.Select(static message => message.Clone()));
			SaveActiveConversation();

			string reply = string.IsNullOrWhiteSpace(result.AssistantText)
				? "The provider completed the request but returned no text reply."
				: result.AssistantText;

			foreach (ForgeToolEvent toolEvent in result.ToolEvents)
			{
				PushToolEventCard(toolEvent, executor);
			}

			PushChatMessage("Forge", reply, isUser: false);
			SetActivity("Complete", 100);
			CreatorService.Interface.StatusBar?.SetStatus("Forge completed the request");
		}
		catch (Exception ex)
		{
			PushSystemMessage(ex.Message, isError: true);
			CreatorService.Interface.StatusBar?.SetStatus("Forge request failed");
		}
		finally
		{
			_isBusy = false;
			UpdateSendState();
			GetTree().CreateTimer(1.2).Timeout += () => { if (_activityCard != null) _activityCard.Visible = false; };
		}
	}

	private string BuildContextSummary(ForgeToolExecutor executor)
	{
		PopupMenu popup = _contextMenu.GetPopup();
		StringBuilder builder = new();

		if (popup.IsItemChecked(0) && Root != null)
		{
			List<Instance> selected = Root.CreatorContext.Selections.SelectedInstances;
			builder.AppendLine("Current selection:");
			if (selected.Count == 0)
			{
				builder.AppendLine("(none)");
			}
			else
			{
				foreach (Instance instance in selected.Take(10))
				{
					builder.AppendLine($"- {instance.LuaPath} [{instance.ClassName}]");
				}
			}
			builder.AppendLine();
		}

		if (popup.IsItemChecked(1) && Tabs.Singleton?.CurrentControl is TextEditorContainer editor)
		{
			string editorText = editor.EditorRoot?.CodeEditor?.Text ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(editorText))
			{
				builder.AppendLine($"Active open script: {editor.TargetFilePath}");
				builder.AppendLine(TrimForContext(editorText, 4000));
				builder.AppendLine();
			}
		}

		if (popup.IsItemChecked(2))
		{
			builder.AppendLine("Explorer outline:");
			builder.AppendLine(executor.DescribeWorldOutline());
			builder.AppendLine();
		}

		if (popup.IsItemChecked(3))
		{
			string console = GetConsoleSnippet();
			if (!string.IsNullOrWhiteSpace(console))
			{
				builder.AppendLine("Recent console output:");
				builder.AppendLine(console);
				builder.AppendLine();
			}
		}

		if (popup.IsItemChecked(4))
		{
			builder.AppendLine("Common creatable classes:");
			builder.AppendLine(ForgeToolExecutor.GetCreatableClassPreview());
			CreatorSession? session = CreatorService.CurrentSession;
			string? definitions = session == null ? null : Path.Combine(session.BVProjectFolderPath, "luau", "def.d.luau");
			if (definitions != null && File.Exists(definitions))
			{
				builder.AppendLine();
				builder.AppendLine("Authoritative BrickVerse Luau API definitions (excerpt):");
				builder.AppendLine(TrimForContext(File.ReadAllText(definitions), 16000));
			}
		}

		return builder.ToString().Trim();
	}

	private static string TrimForContext(string text, int maxChars)
	{
		string trimmed = text.Trim();
		if (trimmed.Length <= maxChars)
		{
			return trimmed;
		}

		return trimmed[..maxChars] + "\n... truncated ...";
	}

	private void OpenSettings()
	{
		ApplySettingsToUi(_settings);
		_providerWindow.PopupCentered();
	}

	private async Task TestConnectionAsync()
	{
		ForgeProviderSettings draft = ReadSettingsFromUi();
		if (!ValidateConfiguration(draft, out string validationError))
		{
			PushSystemMessage(validationError, isError: true);
			return;
		}

		_testButton.Disabled = true;
		_saveButton.Disabled = true;
		CreatorService.Interface.StatusBar?.SetStatus("Testing Forge provider...");

		try
		{
			string response = await _chatClient.TestConnectionAsync(draft);
			PushSystemMessage("Connection test reply: " + response, isError: false);
			CreatorService.Interface.StatusBar?.SetStatus("Forge provider test succeeded");
		}
		catch (Exception ex)
		{
			PushSystemMessage(ex.Message, isError: true);
			CreatorService.Interface.StatusBar?.SetStatus("Forge provider test failed");
		}
		finally
		{
			_testButton.Disabled = false;
			_saveButton.Disabled = false;
		}
	}

	private void SaveSettings()
	{
		ForgeProviderSettings draft = ReadSettingsFromUi();
		_settings = draft;
		ForgeProviderSettingsStore.Save(_settings);
		UpdateComposerProviderSelection();
		UpdateSendState();
		_providerWindow.Hide();
		PushSystemMessage($"Saved Forge provider settings for {_settings.GetProviderLabel()}.", isError: false);
	}

	private void OnSettingsProviderChanged(long index)
	{
		ForgeProviderKind provider = (ForgeProviderKind)(int)index;
		if (string.IsNullOrWhiteSpace(_endpointEdit.Text) || _endpointEdit.Text == _settings.GetBaseEndpoint())
		{
			_endpointEdit.Text = new ForgeProviderSettings { Provider = provider }.GetBaseEndpoint();
		}
	}

	private void OnComposerProviderChanged(long index)
	{
		if (index < 0 || index >= ForgeModelCatalog.Models.Count) return;
		ForgeModelDefinition model = ForgeModelCatalog.Models[(int)index];
		_settings.Provider = model.Provider;
		_settings.Model = model.Id;
		_settings.Endpoint = string.Empty;
		_providerOption.Select((int)_settings.Provider);
		ApplySettingsToUi(_settings);
		ForgeProviderSettingsStore.Save(_settings);
		UpdateSendState();
	}

	private ForgeProviderSettings ReadSettingsFromUi()
	{
		return new ForgeProviderSettings
		{
			Provider = (ForgeProviderKind)_providerOption.Selected,
			Endpoint = _endpointEdit.Text.Trim(),
			ApiKey = _apiKeyEdit.Text.Trim(),
			Model = _modelEdit.Text.Trim(),
			StoreKey = _storeKeyCheck.ButtonPressed,
		};
	}

	private void ApplySettingsToUi(ForgeProviderSettings settings)
	{
		_providerOption.Select((int)settings.Provider);
		_endpointEdit.Text = string.IsNullOrWhiteSpace(settings.Endpoint) ? settings.GetBaseEndpoint() : settings.Endpoint;
		_apiKeyEdit.Text = settings.ApiKey;
		_modelEdit.Text = settings.Model;
		_storeKeyCheck.ButtonPressed = settings.StoreKey;
	}

	private void UpdateComposerProviderSelection()
	{
		int index = ForgeModelCatalog.Models.ToList().FindIndex(model => model.Id.Equals(_settings.Model, StringComparison.OrdinalIgnoreCase));
		_composerProvider.Select(Math.Max(0, index));
	}

	private void UpdateSendState()
	{
		_sendButton.Disabled = _isBusy || string.IsNullOrWhiteSpace(_promptEditor.Text) || !ValidateConfiguration(_settings, out _);
	}

	private static bool ValidateConfiguration(ForgeProviderSettings settings, out string error)
	{
		if (string.IsNullOrWhiteSpace(settings.Model))
		{
			error = "Forge needs a model ID before it can send a request.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(settings.GetBaseEndpoint()))
		{
			error = "Forge needs an API endpoint.";
			return false;
		}

		if (settings.RequiresApiKey && string.IsNullOrWhiteSpace(settings.ApiKey))
		{
			error = $"Forge needs an API key for {settings.GetProviderLabel()}.";
			return false;
		}

		error = string.Empty;
		return true;
	}

	private void ResetConversation()
	{
		_activeConversation = null;
		_history.Clear();
		foreach (Node child in _historyContainer.GetChildren())
		{
			child.QueueFree();
		}

		UpdateEmptyState();
		CreatorService.Interface.StatusBar?.SetStatus("Started a new Forge chat");
	}

	private void UpdateEmptyState()
	{
		bool show = _historyContainer.GetChildCount() == 0;
		_welcome.Visible = show;
		_suggestions.Visible = show;
		_providerNotice.Visible = show;
	}

	private void HideEmptyState()
	{
		_welcome.Visible = false;
		_suggestions.Visible = false;
		_providerNotice.Visible = false;
	}

	private void PushSystemMessage(string text, bool isError)
	{
		PushChatMessage(isError ? "Forge Error" : "Forge", text, isUser: false, isError: isError);
	}

	private static StyleBoxFlat MakePanelStyle(Color background, Color border, int radius = 8, int borderWidth = 1)
	{
		StyleBoxFlat style = new()
		{
			BgColor = background,
			BorderColor = border,
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomLeft = radius,
			CornerRadiusBottomRight = radius,
			BorderWidthLeft = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthBottom = borderWidth,
		};
		return style;
	}

	private static void StyleCompactButton(Button button, bool primary = false)
	{
		button.CustomMinimumSize = new Vector2(0, 30);
		button.AddThemeFontSizeOverride("font_size", 12);
		button.AddThemeStyleboxOverride("normal", MakePanelStyle(
			primary ? new Color(0.02f, 0.42f, 0.78f, 0.95f) : new Color(0.075f, 0.095f, 0.125f, 0.95f),
			primary ? new Color(0.05f, 0.55f, 1f, 1f) : new Color(0.17f, 0.22f, 0.29f, 1f), 6));
		button.AddThemeStyleboxOverride("hover", MakePanelStyle(
			primary ? new Color(0.03f, 0.5f, 0.92f, 1f) : new Color(0.11f, 0.14f, 0.19f, 1f),
			new Color(0.05f, 0.55f, 1f, 1f), 6));
	}

	private void AnimateEntry(Control control)
	{
		control.Modulate = new Color(1, 1, 1, 0);
		control.Position += new Vector2(0, 8);
		Tween tween = CreateTween().SetParallel(true);
		tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		tween.TweenProperty(control, "modulate:a", 1.0f, 0.18f);
		tween.TweenProperty(control, "position:y", control.Position.Y - 8, 0.18f);
	}

	private void PushChatMessage(string role, string text, bool isUser, bool isError = false)
	{
		HBoxContainer row = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		if (isUser) row.Alignment = BoxContainer.AlignmentMode.End;

		PanelContainer bubble = new() { SizeFlagsHorizontal = isUser ? SizeFlags.ShrinkEnd : SizeFlags.ExpandFill };
		bubble.CustomMinimumSize = new Vector2(isUser ? 180 : 0, 0);
		Color background = isError
			? new Color(0.18f, 0.055f, 0.065f, 0.96f)
			: isUser ? new Color(0.025f, 0.19f, 0.32f, 0.96f) : new Color(0.045f, 0.06f, 0.082f, 0.98f);
		Color border = isError
			? new Color(0.75f, 0.2f, 0.25f, 0.75f)
			: isUser ? new Color(0.02f, 0.48f, 0.88f, 0.8f) : new Color(0.12f, 0.16f, 0.22f, 1f);
		bubble.AddThemeStyleboxOverride("panel", MakePanelStyle(background, border, 10));

		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 13);
		margin.AddThemeConstantOverride("margin_right", 13);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_bottom", 11);
		VBoxContainer group = new();
		group.AddThemeConstantOverride("separation", 6);

		Label roleLabel = new() { Text = role.ToUpperInvariant() };
		roleLabel.AddThemeFontSizeOverride("font_size", 10);
		roleLabel.Modulate = isError ? new Color(1f, 0.45f, 0.48f) : isUser ? new Color(0.52f, 0.78f, 1f) : new Color(0.1f, 0.63f, 1f);

		RichTextLabel body = new()
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			SelectionEnabled = true,
			MetaUnderlined = true,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Text = RenderMarkdownToBbCode(text),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		body.AddThemeFontSizeOverride("normal_font_size", 13);
		body.MetaClicked += meta => OS.ShellOpen(meta.AsString());

		group.AddChild(roleLabel);
		group.AddChild(body);
		margin.AddChild(group);
		bubble.AddChild(margin);
		row.AddChild(bubble);
		_historyContainer.AddChild(row);
		AnimateEntry(row);
		CallDeferred(MethodName.ScrollToBottom);
	}

	private void PushToolEventCard(ForgeToolEvent toolEvent, ForgeToolExecutor executor)
	{
		PanelContainer card = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		card.AddThemeStyleboxOverride("panel", MakePanelStyle(new Color(0.035f, 0.05f, 0.07f, 0.98f), new Color(0.12f, 0.18f, 0.25f, 1f), 9));
		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 5);
		Label title = new() { Text = toolEvent.Title.Or(toolEvent.ToolName), TooltipText = toolEvent.ToolName };
		title.AddThemeFontSizeOverride("font_size", 12);
		layout.AddChild(title);
		if (!string.IsNullOrWhiteSpace(toolEvent.Detail))
		{
			Label detail = new() { Text = toolEvent.Detail, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			detail.Modulate = new Color(0.68f, 0.73f, 0.8f);
			layout.AddChild(detail);
		}

		HBoxContainer actions = new();
		actions.AddThemeConstantOverride("separation", 6);
		if (!string.IsNullOrWhiteSpace(toolEvent.InstancePath))
		{
			Button reveal = new() { Text = "Open in Inspector", TooltipText = "Select this item and reveal it in Inspector" };
			StyleCompactButton(reveal, primary: true);
			reveal.Pressed += () =>
			{
				string json = JsonSerializer.Serialize(new ForgeSelectInstancesArgs { Paths = [toolEvent.InstancePath!], Mode = "replace" }, ForgeJsonContext.Default.ForgeSelectInstancesArgs);
				executor.Execute("select_instances", json);
				CreatorService.Interface.StatusBar?.SetStatus($"Revealed {toolEvent.InstancePath} in Inspector");
			};
			actions.AddChild(reveal);
		}
		if (!string.IsNullOrWhiteSpace(toolEvent.Diff))
		{
			Button diff = new() { Text = "View diff" };
			StyleCompactButton(diff);
			diff.Pressed += () => ShowDiffDialog("Forge script diff", toolEvent.Diff!);
			actions.AddChild(diff);
		}
		if (toolEvent.CanRollback)
		{
			foreach (Button previous in _rollbackButtons) previous.Disabled = true;
			Button rollback = new() { Text = "Rollback", TooltipText = "Rollback the latest Forge change" };
			StyleCompactButton(rollback);
			_rollbackButtons.Add(rollback);
			rollback.Pressed += () =>
			{
				string result = executor.Execute("rollback_last_change", "{}");
				rollback.Disabled = true;
				PushSystemMessage(result, isError: false);
			};
			actions.AddChild(rollback);
		}
		if (actions.GetChildCount() > 0) layout.AddChild(actions);
		margin.AddChild(layout);
		card.AddChild(margin);
		_historyContainer.AddChild(card);
		AnimateEntry(card);
		CallDeferred(MethodName.ScrollToBottom);
	}

	private void ShowDiffDialog(string title, string diff)
	{
		AcceptDialog dialog = new()
		{
			Title = title,
			MinSize = new Vector2I(900, 640),
		};

		PanelContainer surface = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		surface.AddThemeStyleboxOverride("panel", MakePanelStyle(new Color(0.035f, 0.045f, 0.06f, 1f), new Color(0.16f, 0.2f, 0.26f, 1f), 8));

		ScrollContainer scroll = new()
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};

		RichTextLabel viewer = new()
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			SelectionEnabled = true,
			AutowrapMode = TextServer.AutowrapMode.Off,
			Text = RenderUnifiedDiffToBbCode(diff),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		viewer.AddThemeFontSizeOverride("normal_font_size", 13);

		MarginContainer padding = new();
		padding.AddThemeConstantOverride("margin_left", 8);
		padding.AddThemeConstantOverride("margin_right", 8);
		padding.AddThemeConstantOverride("margin_top", 8);
		padding.AddThemeConstantOverride("margin_bottom", 8);
		padding.AddChild(viewer);
		scroll.AddChild(padding);
		surface.AddChild(scroll);
		dialog.AddChild(surface);

		dialog.Confirmed += dialog.QueueFree;
		dialog.CloseRequested += dialog.QueueFree;
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private static string RenderUnifiedDiffToBbCode(string diff)
	{
		if (string.IsNullOrWhiteSpace(diff))
		{
			return "[color=#8b949e]No diff content was returned.[/color]";
		}

		StringBuilder output = new();
		string[] lines = diff.Replace("\r\n", "\n").Split('\n');
		int oldLine = 0;
		int newLine = 0;

		foreach (string raw in lines)
		{
			if (raw.StartsWith("@@", StringComparison.Ordinal))
			{
				Match hunk = Regex.Match(raw, @"@@\s+-(\d+)(?:,(\d+))?\s+\+(\d+)(?:,(\d+))?\s+@@");
				if (hunk.Success)
				{
					oldLine = int.Parse(hunk.Groups[1].Value);
					newLine = int.Parse(hunk.Groups[3].Value);
				}
				output.AppendLine($"[bgcolor=#0d419d66][color=#79c0ff][font=monospace]     {EscapeBb(raw)}[/font][/color][/bgcolor]");
				continue;
			}

			if (raw.StartsWith("diff --git", StringComparison.Ordinal) || raw.StartsWith("index ", StringComparison.Ordinal) || raw.StartsWith("--- ", StringComparison.Ordinal) || raw.StartsWith("+++ ", StringComparison.Ordinal))
			{
				output.AppendLine($"[bgcolor=#161b22][color=#8b949e][font=monospace]     {EscapeBb(raw)}[/font][/color][/bgcolor]");
				continue;
			}

			if (raw.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
			{
				output.AppendLine($"[bgcolor=#161b22][color=#8b949e][font=monospace]     {EscapeBb(raw)}[/font][/color][/bgcolor]");
				continue;
			}

			char marker = raw.Length > 0 ? raw[0] : ' ';
			string source = raw.Length > 0 && marker is '+' or '-' or ' ' ? raw[1..] : raw;
			string highlighted = HighlightLuauLine(source);
			string oldNumber = marker == '+' ? string.Empty : oldLine.ToString();
			string newNumber = marker == '-' ? string.Empty : newLine.ToString();
			string gutter = $"{oldNumber,4} {newNumber,4} ";

			switch (marker)
			{
				case '+':
					output.AppendLine($"[bgcolor=#033a1666][color=#3fb950][font=monospace]{EscapeBb(gutter)}+[/font][/color][font=monospace]{highlighted}[/font][/bgcolor]");
					newLine++;
					break;
				case '-':
					output.AppendLine($"[bgcolor=#67060c66][color=#f85149][font=monospace]{EscapeBb(gutter)}-[/font][/color][font=monospace]{highlighted}[/font][/bgcolor]");
					oldLine++;
					break;
				default:
					output.AppendLine($"[color=#6e7681][font=monospace]{EscapeBb(gutter)} [/font][/color][font=monospace]{highlighted}[/font]");
					oldLine++;
					newLine++;
					break;
			}
		}

		return output.ToString().TrimEnd();
	}

	private static string EscapeBb(string text) => text.Replace("[", "[lb]").Replace("]", "[rb]");

	private static string HighlightLuauLine(string line)
	{
		string escaped = EscapeBb(line);
		int comment = escaped.IndexOf("--", StringComparison.Ordinal);
		string code = comment >= 0 ? escaped[..comment] : escaped;
		string suffix = comment >= 0 ? escaped[comment..] : string.Empty;
		code = Regex.Replace(code, "(&quot;|\")(.*?)(\"|&quot;)", "[color=#d7ba7d]$1$2$3[/color]");
		code = Regex.Replace(code, @"\b(local|function|end|if|then|else|elseif|for|while|repeat|until|return|break|continue|and|or|not|in|do|true|false|nil)\b", "[color=#c586c0]$1[/color]");
		code = Regex.Replace(code, @"\b([0-9]+(?:\.[0-9]+)?)\b", "[color=#b5cea8]$1[/color]");
		code = Regex.Replace(code, @"\b(world|script|game|self)\b", "[color=#4fc1ff]$1[/color]");
		if (!string.IsNullOrEmpty(suffix)) code += "[color=#6a9955]" + suffix + "[/color]";
		return code;
	}

	private static string RenderMarkdownToBbCode(string markdown)
	{
		if (string.IsNullOrEmpty(markdown)) return string.Empty;
		StringBuilder output = new();
		string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
		bool inCode = false;
		string language = string.Empty;
		foreach (string raw in lines)
		{
			string trimmed = raw.TrimStart();
			if (trimmed.StartsWith("```", StringComparison.Ordinal))
			{
				if (!inCode)
				{
					language = trimmed[3..].Trim().ToLowerInvariant();
					output.AppendLine($"[bgcolor=#080d14][font_size=11][color=#6f849c]  {(string.IsNullOrWhiteSpace(language) ? "code" : EscapeBb(language))}[/color][/font_size]\n[font_size=12][font=monospace]");
					inCode = true;
				}
				else
				{
					output.AppendLine("[/font][/font_size][/bgcolor]");
					inCode = false;
				}
				continue;
			}
			if (inCode)
			{
				output.AppendLine(language is "lua" or "luau" ? HighlightLuauLine(raw) : EscapeBb(raw));
				continue;
			}

			string line = EscapeBb(raw);
			if (line.StartsWith("### ")) line = "[font_size=15][b]" + line[4..] + "[/b][/font_size]";
			else if (line.StartsWith("## ")) line = "[font_size=17][b]" + line[3..] + "[/b][/font_size]";
			else if (line.StartsWith("# ")) line = "[font_size=19][b]" + line[2..] + "[/b][/font_size]";
			else if (Regex.IsMatch(line, @"^\s*[-*+]\s+")) line = "  • " + Regex.Replace(line, @"^\s*[-*+]\s+", string.Empty);
			else if (line.StartsWith("&gt; ")) line = "[indent][color=#93a4b8]" + line[5..] + "[/color][/indent]";
			line = Regex.Replace(line, @"\*\*(.+?)\*\*", "[b]$1[/b]");
			line = Regex.Replace(line, @"__(.+?)__", "[b]$1[/b]");
			line = Regex.Replace(line, @"(?<!\*)\*([^*\n]+)\*(?!\*)", "[i]$1[/i]");
			line = Regex.Replace(line, @"`([^`\n]+)`", "[bgcolor=#111a25][font=monospace][color=#d6e4f0] $1 [/color][/font][/bgcolor]");
			line = Regex.Replace(line, @"\[lb\]([^\[]+)\[rb\]\((https?://[^\s\)]+)\)", "[url=$2]$1[/url]");
			line = Regex.Replace(line, @"~~(.+?)~~", "[s]$1[/s]");
			output.AppendLine(line);
		}
		if (inCode) output.AppendLine("[/font][/font_size][/bgcolor]");
		return output.ToString().TrimEnd();
	}

	private void BuildEnhancedHeader()
	{
		// The persistent header controls live in forge_chat.tscn so the scene
		// accurately represents the final dock instead of being rebuilt at runtime.
		_historyMenu = GetNode<MenuButton>("Header/Row/History");
	}

	private void PopulateModelPicker()
	{
		_composerProvider.Clear();
		foreach (ForgeModelDefinition model in ForgeModelCatalog.Models)
		{
			_composerProvider.AddItem(model.DisplayName);
			int index = _composerProvider.ItemCount - 1;
			_composerProvider.SetItemTooltip(index, $"{model.Provider}: {model.Description}");
		}
	}

	private void SetActivity(string text, double progress)
	{
		EnsureInlineActivityCard();
		_activityLabel.Text = text;
		_progress.Visible = progress > 0;
		if (_activityCard != null) _activityCard.Visible = progress > 0 || text != "Ready";

		Tween valueTween = CreateTween();
		valueTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		valueTween.TweenProperty(_progress, "value", progress, 0.28f);

		_activityPulse?.Kill();
		if (_activityCard != null && progress > 0 && progress < 100)
		{
			_activityPulse = CreateTween().SetLoops();
			_activityPulse.SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
			_activityPulse.TweenProperty(_activityCard, "modulate", new Color(1, 1, 1, 0.78f), 0.65f);
			_activityPulse.TweenProperty(_activityCard, "modulate", Colors.White, 0.65f);
		}
		else if (_activityCard != null)
		{
			_activityCard.Modulate = Colors.White;
		}
		CallDeferred(MethodName.ScrollToBottom);
	}

	private Task UpdateInlineActivityAsync(string text)
	{
		SetActivity(text, Math.Min(92, Math.Max(22, _progress?.Value + 9 ?? 22)));
		return Task.CompletedTask;
	}

	private void EnsureInlineActivityCard()
	{
		if (_activityCard != null && IsInstanceValid(_activityCard)) return;

		_activityCard = GetNodeOrNull<PanelContainer>("ChatScroll/ChatMargin/Messages/ActivityCard");
		if (_activityCard != null)
		{
			_activityLabel = _activityCard.GetNode<Label>("Margin/Layout/ActivityRow/Text");
			_progress = _activityCard.GetNode<ProgressBar>("Margin/Layout/Progress");
			return;
		}

		// Fallback for older scenes that have not yet been replaced.
		_activityCard = new PanelContainer { Name = "ForgeActivity" };
		_activityCard.AddThemeStyleboxOverride("panel", MakePanelStyle(new Color(0.025f, 0.075f, 0.12f, 0.98f), new Color(0.02f, 0.45f, 0.82f, 0.8f), 9));
		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 6);
		_activityLabel = new Label { Text = "Thinking…" };
		_activityLabel.AddThemeFontSizeOverride("font_size", 12);
		_progress = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 10, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 4) };
		_progress.AddThemeStyleboxOverride("background", MakePanelStyle(new Color(0.02f, 0.03f, 0.045f, 1f), new Color(0.02f, 0.03f, 0.045f, 1f), 2, 0));
		_progress.AddThemeStyleboxOverride("fill", MakePanelStyle(new Color(0.02f, 0.5f, 0.95f, 1f), new Color(0.02f, 0.5f, 0.95f, 1f), 2, 0));
		layout.AddChild(_activityLabel);
		layout.AddChild(_progress);
		margin.AddChild(layout);
		_activityCard.AddChild(margin);
		_historyContainer.AddChild(_activityCard);
		AnimateEntry(_activityCard);
	}

	private async Task<bool> ConfirmLuauExecutionAsync(ForgeRunLuauArgs args)
	{
		TaskCompletionSource<bool> completion = new();
		ConfirmationDialog dialog = new()
		{
			Title = "Run Luau from Forge?",
			DialogText = $"Forge wants to run this Luau in the current world. Review it carefully before allowing execution.\n\nReason: {args.Reason ?? "Testing the requested change"}\n\n{TrimForContext(args.Source, 6000)}",
			OkButtonText = "Run Luau",
			CancelButtonText = "Cancel",
			MinSize = new Vector2I(720, 520),
		};
		AddChild(dialog);
		dialog.Confirmed += () => completion.TrySetResult(true);
		dialog.Canceled += () => completion.TrySetResult(false);
		dialog.CloseRequested += () => completion.TrySetResult(false);
		dialog.PopupCentered();
		bool approved = await completion.Task;
		dialog.QueueFree();
		return approved;
	}

	private void EnsureActiveConversation(string firstPrompt)
	{
		if (_activeConversation != null) return;
		_activeConversation = new ForgeConversation
		{
			Title = firstPrompt.Length > 42 ? firstPrompt[..42] + "…" : firstPrompt,
			Model = _settings.Model,
		};
		_conversations.Insert(0, _activeConversation);
		RefreshHistoryMenu();
	}

	private void SaveActiveConversation()
	{
		if (_activeConversation == null) return;
		_activeConversation.Messages = _history.Select(static message => message.Clone()).ToList();
		_activeConversation.Model = _settings.Model;
		_activeConversation.UpdatedAt = DateTime.UtcNow;
		ForgeConversationStore.Save(_conversations);
		RefreshHistoryMenu();
	}

	private void RefreshHistoryMenu()
	{
		PopupMenu popup = _historyMenu.GetPopup();
		popup.Clear();
		popup.AddItem("New chat", 10000);
		popup.AddSeparator("Recent chats");
		for (int i = 0; i < Math.Min(_conversations.Count, 20); i++)
		{
			ForgeConversation chat = _conversations[i];
			popup.AddItem($"{chat.Title}  ·  {chat.UpdatedAt.ToLocalTime():MMM d}", i);
		}
	}

	private void OnHistorySelected(long id)
	{
		if (id == 10000) { ResetConversation(); return; }
		if (id < 0 || id >= _conversations.Count) return;
		LoadConversation(_conversations[(int)id]);
	}

	private void LoadConversation(ForgeConversation conversation)
	{
		ResetConversation();
		_activeConversation = conversation;
		_history.AddRange(conversation.Messages.Select(static message => message.Clone()));
		foreach (ForgeChatMessage message in conversation.Messages.Where(static message => message.Role is "user" or "assistant"))
			PushChatMessage(message.Role == "user" ? "You" : "Forge", message.Content ?? string.Empty, message.Role == "user");
		HideEmptyState();
	}

	private void ScrollToBottom()
	{
		_chatScroll.ScrollVertical = int.MaxValue;
	}

}
