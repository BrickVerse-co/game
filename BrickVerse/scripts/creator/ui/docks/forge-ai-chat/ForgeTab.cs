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
using System.Text;
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

			if (result.ToolEvents.Count > 0)
			{
				reply = $"Used tools: {string.Join(", ", result.ToolEvents.Distinct())}\n\n{reply}";
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

	private void PushChatMessage(string role, string text, bool isUser, bool isError = false)
	{
		VBoxContainer group = new();
		group.AddThemeConstantOverride("separation", 4);
		group.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		Label roleLabel = new();
		roleLabel.Text = role;
		roleLabel.HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
		roleLabel.Modulate = isError
			? new Color(0.96f, 0.37f, 0.37f)
			: isUser
				? new Color(0.62f, 0.74f, 0.98f)
				: new Color(0.44f, 0.83f, 1f);

		RichTextLabel body = new();
		body.BbcodeEnabled = false;
		body.FitContent = true;
		body.ScrollActive = false;
		body.SelectionEnabled = true;
		body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		body.Text = text;
		body.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		group.AddChild(roleLabel);
		group.AddChild(body);
		_historyContainer.AddChild(group);
		CallDeferred(MethodName.ScrollToBottom);
	}

	private void BuildEnhancedHeader()
	{
		HBoxContainer headerRow = GetNode<HBoxContainer>("Header/Row");
		_historyMenu = new MenuButton { Text = "History", TooltipText = "Open previous Forge chats" };
		headerRow.AddChild(_historyMenu);
		headerRow.MoveChild(_historyMenu, Math.Max(0, headerRow.GetChildCount() - 3));


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
		_progress.Value = progress;
		_progress.Visible = progress > 0;
		if (_activityCard != null) _activityCard.Visible = progress > 0 || text != "Ready";
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
		_activityCard = new PanelContainer { Name = "ForgeActivity" };
		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 6);
		_activityLabel = new Label { Text = "Thinking…" };
		_activityLabel.AddThemeFontSizeOverride("font_size", 12);
		_progress = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 10, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 3) };
		layout.AddChild(_activityLabel);
		layout.AddChild(_progress);
		margin.AddChild(layout);
		_activityCard.AddChild(margin);
		_historyContainer.AddChild(_activityCard);
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
