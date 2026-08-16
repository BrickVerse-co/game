// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Client.Settings;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Services;
using BrickVerse.Shared;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Client.UI.Chat;

public partial class UIChat : Control
{
	private const int MaxMessages = 100;
	private const float AspectRatio = 400f / 240f;
	private const string ChatLabelPath = "res://scenes/client/ui/chat/chat_label.tscn";
	private const string MicrophoneIconPath = "res://assets/textures/ui-icons/microphone-bold.svg";
	private const string MutedMicrophoneIconPath = "res://assets/textures/ui-icons/microphone-slash-bold.svg";
	[Export] private LineEdit _chatField = null!;
	[Export] private Control _chatLayout = null!;
	[Export] private ScrollContainer _chatScroll = null!;
	[Export] private AnimationPlayer _animPlayer = null!;
	[Export] private Button _sendButton = null!;
	[Export] private AnimationPlayer _sendAnim = null!;
	[Export] private Control _chatFieldPanel = null!;
	[Export] private Panel _chatPanel = null!;
	[Export] private TextureRect _resizeHandle = null!;
	[Export] private UIEmojiPicker _emojiPicker = null!;
	[Export] private Button _emojiButton = null!;

	public CoreUIRoot CoreUI = null!;
	private World Root => CoreUI.Root;
	private Player LocalPlayer => Root.Players.LocalPlayer;

	public bool IsOn = false;

	private bool _isAutocompleteOpen;
	private bool _suppressAutocomplete;

	private readonly Queue<UIChatLabel> _pendingMessages = [];
	private readonly List<UIChatLabel> _chatMessages = [];

	private bool _isResizing;
	private float _resizeStartWidth;
	private float _resizeStartMaxWidth;
	private Vector2 _resizeStartMousePos;
	private readonly Vector2 _minSize = new(400, 240);

	private Tween? _resizeHandleTween;
	private Button? _quickChatButton;
	private Button? _voiceChatButton;
	private PopupMenu? _commandAutocomplete;

	private const float MaxChatWidthValue = 1000f;
	private float MaxChatWidth => Mathf.Clamp(GetViewportRect().Size.X * 0.45f, _minSize.X, MaxChatWidthValue);

	public override void _Ready()
	{
		ConnectSignals();
		InitializeEmojiPicker();
		ConfigureChatAccess();
		CreateVoiceChatButton();

		ClampToViewport();
	}

	private void CreateVoiceChatButton()
	{
		if (!LocalPlayer.CanVoiceChat || _voiceChatButton != null) return;

		_voiceChatButton = new Button
		{
			ToggleMode = true,
			CustomMinimumSize = new Vector2(36, 36),
			FocusMode = FocusModeEnum.None,
			Flat = true,
			Icon = GD.Load<Texture2D>(MutedMicrophoneIconPath),
			ExpandIcon = true,
			Modulate = new Color(1, 1, 1, 0.5f),
			MouseDefaultCursorShape = CursorShape.PointingHand,
			TooltipText = "Turn microphone on"
		};
		_voiceChatButton.AddThemeConstantOverride("icon_max_width", 24);

		Container composer = _emojiButton.GetParent<Container>();
		composer.AddChild(_voiceChatButton);
		composer.MoveChild(_voiceChatButton, _emojiButton.GetIndex() + 1);
		_voiceChatButton.Toggled += OnMicrophoneToggled;
	}

	private void OnMicrophoneToggled(bool enabled)
	{
		if (_voiceChatButton == null) return;
		Root.VoiceChat.SetMicrophoneEnabled(enabled);
		_voiceChatButton.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.5f);
		_voiceChatButton.Icon = enabled ? GD.Load<Texture2D>(MicrophoneIconPath) : GD.Load<Texture2D>(MutedMicrophoneIconPath);
		_voiceChatButton.TooltipText = enabled ? "Mute microphone" : "Turn microphone on";
	}

	private void ConnectSignals()
	{
		_chatField.TextSubmitted += OnTextSubmitted;
		_chatField.GuiInput += OnGuiInput;
		_chatField.TextChanged += OnChatFieldTextChanged;

		_sendButton.Pressed += OnSendButtonPressed;
		_emojiButton.Pressed += OnEmojiButtonPressed;

		_resizeHandle.GuiInput += OnResizeHandleInput;
		_resizeHandle.MouseEntered += OnResizeHandleMouseEntered;
		_resizeHandle.MouseExited += OnResizeHandleMouseExited;

		Root.Chat.NewChatMessage.Connect(OnNewChatMessage);
		Root.Chat.MessageDeclined.Connect(OnMessageDeclined);
		Root.Chat.MessageReceived.Connect(OnMessageReceived);

		GetViewport().SizeChanged += ClampToViewport;
	}

	private void InitializeEmojiPicker()
	{
		_emojiPicker.Initialize();
		_emojiPicker.EmojiPicked += OnEmojiPicked;
	}

	private void ConfigureChatAccess()
	{
		if (LocalPlayer.IsAgeRestricted)
		{
			_chatFieldPanel.Visible = LocalPlayer.CanQuickChat;
			SetChatControlsEnabled(false);
			CreateQuickChatButton();
			return;
		}

		_chatFieldPanel.Visible = true;

		if (!LocalPlayer.CanChat)
		{
			_chatField.PlaceholderText = GetChatRestrictionMessage(
				LocalPlayer.ChatRestrictionReason
			);

			SetChatControlsEnabled(false);
			CreateQuickChatButton();
			return;
		}

		SetChatControlsEnabled(true);
	}

	private void CreateQuickChatButton()
	{
		if (!LocalPlayer.CanQuickChat || _quickChatButton != null) return;
		_quickChatButton = new Button { Text = "Quick Chat", TooltipText = "Choose a safe preset phrase", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_chatFieldPanel.AddChild(_quickChatButton);
		_quickChatButton.Pressed += () =>
		{
			PopupPanel wheel = new() { Size = new Vector2I(460, 460), Title = "Quick Chat" };
			Control surface = new() { CustomMinimumSize = new Vector2(460, 460) }; wheel.AddChild(surface);
			void Populate(string titleText, IReadOnlyList<(string Label, int Value)> items, System.Action<int> selected)
			{
				foreach (Node child in surface.GetChildren()) child.QueueFree();
				Vector2 center = new(230, 230); float radius = 155f;
				for (int i = 0; i < items.Count; i++)
				{
					int value = items[i].Value; float angle = -Mathf.Pi / 2f + Mathf.Tau * i / items.Count;
					Button choice = new() { Text = items[i].Label, Size = new Vector2(145, 48), Position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius - new Vector2(72.5f, 24f) };
					choice.Pressed += () => selected(value); surface.AddChild(choice);
				}
				Label title = new() { Text = titleText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Position = center - new Vector2(55, 32), Size = new Vector2(110, 64) }; surface.AddChild(title);
			}
			void ShowGroups() => Populate("QUICK\nCHAT", QuickChatCatalog.Groups.Select((group, index) => (group.Name, index)).ToArray(), groupIndex =>
			{
				QuickChatCatalog.Group group = QuickChatCatalog.Groups[groupIndex]; int offset = QuickChatCatalog.Groups.Take(groupIndex).Sum(item => item.Phrases.Length);
				Populate(group.Name.ToUpperInvariant(), group.Phrases.Select((phrase, index) => (phrase, offset + index)).ToArray(), phraseIndex => { Root.Chat.SendQuickChat(phraseIndex); wheel.Hide(); wheel.QueueFree(); });
			});
			ShowGroups(); AddChild(wheel);
			Vector2I viewportSize = (Vector2I)GetViewportRect().Size; wheel.Popup(new Rect2I((viewportSize - wheel.Size) / 2, wheel.Size));
		};
	}

	private void SetChatControlsEnabled(bool enabled)
	{
		_chatField.Editable = enabled;
		_sendButton.Visible = enabled;
		_emojiButton.Visible = enabled;
	}

	private static string GetChatRestrictionMessage(string? restrictionReason)
	{
		return restrictionReason switch
		{
			"AGE_RESTRICTED" =>
				"Chat is disabled due to age restrictions",

			"MUTED" =>
				"You are muted and cannot chat",

			"CHAT_MODERATION" =>
				"Chat is disabled due to moderation restrictions",

			"UNVERIFIED_EMAIL" =>
				"Chat is disabled until your email is verified",

			_ =>
				"Chat is disabled for your account"
		};
	}

	public override void _ExitTree()
	{
		if (_voiceChatButton != null) _voiceChatButton.Toggled -= OnMicrophoneToggled;
		Root.Chat.NewChatMessage.Disconnect(OnNewChatMessage);
		Root.Chat.MessageDeclined.Disconnect(OnMessageDeclined);
		Root.Chat.MessageReceived.Disconnect(OnMessageReceived);
		_chatField.TextSubmitted -= OnTextSubmitted;
		_chatField.GuiInput -= OnGuiInput;
		_chatField.TextChanged -= OnChatFieldTextChanged;
		_sendButton.Pressed -= OnSendButtonPressed;
		_resizeHandle.GuiInput -= OnResizeHandleInput;
		_resizeHandle.MouseEntered -= OnResizeHandleMouseEntered;
		_resizeHandle.MouseExited -= OnResizeHandleMouseExited;
		_emojiPicker.EmojiPicked -= OnEmojiPicked;
		_emojiButton.Pressed -= OnEmojiButtonPressed;
		GetViewport().SizeChanged -= ClampToViewport;
		base._ExitTree();
	}

	private void OnGuiInput(InputEvent @event)
	{
		if (@event is InputEventKey k && k.Pressed)
		{
			if (k.Keycode == Key.Escape)
			{
				if (_isAutocompleteOpen || _emojiPicker.Visible)
				{
					CloseEmojiPicker();
					GetViewport().SetInputAsHandled();
					return;
				}
				GetViewport().GuiReleaseFocus();
				GetViewport().SetInputAsHandled();
			}
			else if (k.Keycode == Key.Tab && _isAutocompleteOpen)
			{
				if (k.ShiftPressed)
					_emojiPicker.SelectPrev();
				else
					_emojiPicker.SelectNext();
				GetViewport().SetInputAsHandled();
			}
			else if (k.Keycode == Key.Enter && _isAutocompleteOpen)
			{
				string emojiName = _emojiPicker.GetSelectedEmojiName();
				if (!string.IsNullOrEmpty(emojiName))
					InsertEmojiAtCursor(emojiName);
				GetViewport().SetInputAsHandled();
			}
			else if (_isAutocompleteOpen && (k.Keycode == Key.Left || k.Keycode == Key.Right))
			{
				if (k.Keycode == Key.Right)
					_emojiPicker.SelectNext();
				else
					_emojiPicker.SelectPrev();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void OnMessageReceived(string msg)
	{
		CreateNewChatLabel("", msg);
	}

	private void OnSendButtonPressed()
	{
		SendMessage(_chatField.Text);
		_sendAnim.Play("send");
	}

	private void OnTextSubmitted(string newText)
	{
		SendMessage(newText);
	}

	private void SendMessage(string text)
	{
		if (!Visible) return;

		// Release focus from chat field now
		_chatField.ReleaseFocus();

		if (string.IsNullOrWhiteSpace(text))
		{
			// null/whitespace, return
			return;
		}
		_chatField.Text = "";

		// Handle commands
		if (text.StartsWith('/'))
		{
			if (Root.Chat.TryExecuteLocalCommand(text)) return;
			string[] cmd = text.Split(' ');

			if (cmd[0] == "/spectator")
			{
				Root.Capture.OpenSpectatorView();
				return;
			}
			else if (cmd[0] == "/kick")
			{
				Root.Players.AdminKick(cmd[1]);
				return;
			}

			bool routedChat = cmd[0] is "/w" or "/whisper" or "/t" or "/team" or "/channel";
			bool serverCommand = routedChat
				|| Root.Chat.GetCommandSuggestions(cmd[0]).Any(item => item.Equals(cmd[0], System.StringComparison.OrdinalIgnoreCase));
			if (!serverCommand) { Root.Players.LocalPlayer.PlayEmote(cmd[0][1..]); return; }
			if (!routedChat) { Root.Chat.SendChatMessage(text); return; }
		}

		RecordEmojisFromText(text);

		UIChatLabel newPending = NewChatMessage(Root.Players.LocalPlayer, text);
		_pendingMessages.Enqueue(newPending);
		newPending.IsPending = true;

		Root.Chat.SendChatMessage(text);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("chat"))
		{
			_chatField.GrabFocus();
		}
		base._UnhandledInput(@event);
	}

	public void SetEnabled(bool enabled)
	{
		if (enabled && !IsOn)
		{
			_animPlayer.Play("open");
		}
		else if (IsOn)
		{
			_animPlayer.Play("close");
			CloseEmojiPicker();
		}
		IsOn = enabled;
	}

	private void OnNewChatMessage(Player from, string msg)
	{
		if (from == World.Current!.Players.LocalPlayer && _pendingMessages.Count > 0)
		{
			UIChatLabel label = _pendingMessages.Dequeue();
			label.IsPending = false;
			label.Content = msg;
			return;
		}
		NewChatMessage(from, msg);
	}

	private void OnMessageDeclined()
	{
		if (_pendingMessages.Count == 0) return;
		UIChatLabel label = _pendingMessages.Dequeue();
		label.IsDeclined = true;
	}

	private UIChatLabel NewChatMessage(Player from, string msg)
	{
		return CreateNewChatLabel(from.Name, msg, from.ChatColor, from);
	}

	public UIChatLabel CreateNewChatLabel(string authorName, string content, Color? chatColor = null, Player? authorPlayer = null)
	{
		UIChatLabel chatLabel = Globals.CreateInstanceFromScene<UIChatLabel>(ChatLabelPath);
		chatLabel.AuthorName = authorName;
		chatLabel.AuthorPlayer = authorPlayer;
		chatLabel.ChatColorsEnabled = Root.PlayerDefaults.ChatColorsEnabled
			&& ClientSettingsService.Instance.Get<bool>(ClientSettingKeys.Chat.ChatColors);
		chatLabel.FontPath = ClientSettingsService.Instance.Get<string>(ClientSettingKeys.Chat.ChatFont);
		chatLabel.FontSize = (int)ClientSettingsService.Instance.Get<float>(ClientSettingKeys.Chat.ChatFontSize);
		if (chatColor.HasValue)
		{
			chatLabel.NameColor = chatColor.Value;
		}
		chatLabel.Content = content;
		_chatLayout.AddChild(chatLabel);
		_chatMessages.Add(chatLabel);

		// Scroll to the bottom only if the chat is at the bottom
		VScrollBar vScrollBar = _chatScroll.GetVScrollBar();
		bool atBottom = vScrollBar.Value + 5 >= (vScrollBar.MaxValue - vScrollBar.Page);
		if (atBottom)
		{
			BV.CallDeferred(() =>
			{
				int scrollVal = (int)vScrollBar.MaxValue + 1000;
				_chatScroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scrollVal);
			});
		}

		// Clean up old chat logs
		if (_chatMessages.Count > MaxMessages)
		{
			var oldest = _chatMessages[0];
			_chatLayout.RemoveChild(oldest);
			oldest.QueueFree();
			_chatMessages.RemoveAt(0);
		}

		return chatLabel;
	}

	public override void _Input(InputEvent @event)
	{
		if (_emojiPicker.Visible && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
		{
			Vector2 clickPos = mb.GlobalPosition;
			Rect2 pickerRect = new(_emojiPicker.GlobalPosition, _emojiPicker.Size);
			Rect2 buttonRect = new(_emojiButton.GlobalPosition, _emojiButton.Size);
			if (!pickerRect.HasPoint(clickPos) && !buttonRect.HasPoint(clickPos))
			{
				CloseEmojiPicker();
			}
		}

		if (!_isResizing) return;

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
		{
			_isResizing = false;
		}
		else if (@event is InputEventMouseMotion motion)
		{
			float newWidth = _resizeStartWidth + (motion.GlobalPosition.X - _resizeStartMousePos.X);
			newWidth = Mathf.Clamp(newWidth, _minSize.X, _resizeStartMaxWidth);
			_chatPanel.Size = new Vector2(newWidth, newWidth / AspectRatio);
		}
	}

	private void OnResizeHandleInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button)
		{
			_isResizing = true;
			_resizeStartWidth = _chatPanel.Size.X;
			_resizeStartMaxWidth = MaxChatWidth;
			_resizeStartMousePos = button.GlobalPosition;
			GetViewport().SetInputAsHandled();
		}
	}

	private void OnResizeHandleMouseEntered() => TweenResizeHandleAlpha(Colors.White, 0.15f);
	private void OnResizeHandleMouseExited() => TweenResizeHandleAlpha(new Color(1, 1, 1, 0.25f), 0.3f);

	private void TweenResizeHandleAlpha(Color target, float duration)
	{
		_resizeHandleTween?.Kill();
		_resizeHandleTween = CreateTween();
		_resizeHandleTween.TweenProperty(_resizeHandle, "modulate", target, duration);
	}

	private void ClampToViewport()
	{
		var viewSize = GetViewportRect().Size;
		float maxWidth = MaxChatWidth;

		var size = _chatPanel.Size;
		float clampedX = Mathf.Clamp(size.X, _minSize.X, maxWidth);
		float clampedY = clampedX / AspectRatio;

		if (Mathf.Abs(size.X - clampedX) > 0.01f || Mathf.Abs(size.Y - clampedY) > 0.01f)
			_chatPanel.Size = new Vector2(clampedX, clampedY);

		Vector2 pos = Position;
		float newX = Mathf.Clamp(pos.X, 0, viewSize.X - clampedX - 16);
		float newY = Mathf.Clamp(pos.Y, 0, viewSize.Y - clampedY - 16);

		if (pos.X != newX || pos.Y != newY)
			Position = new Vector2(newX, newY);
	}

	private void OnEmojiButtonPressed()
	{
		if (!_isAutocompleteOpen && _emojiPicker.Visible)
		{
			_emojiPicker.Visible = false;
			_emojiButton.ButtonPressed = false;
			return;
		}

		_isAutocompleteOpen = false;
		_emojiButton.ButtonPressed = true;
		_emojiPicker.ShowFullPicker(_chatPanel.Size.X);
		_emojiPicker.Size = new Vector2(_chatPanel.Size.X, 190);
		PositionPickerBelowField();
		_emojiPicker.Visible = true;
	}

	private void OnChatFieldTextChanged(string newText)
	{
		if (_suppressAutocomplete)
			return;

		if (_emojiPicker.Visible && !_isAutocompleteOpen)
			return;

		int cursorPos = _chatField.CaretColumn;
		if (newText.StartsWith('/') && !newText[..Mathf.Min(cursorPos, newText.Length)].Contains(' '))
		{
			string[] suggestions = Root.Chat.GetCommandSuggestions(newText[..cursorPos]).ToArray();
			if (suggestions.Length > 0)
			{
				_commandAutocomplete?.QueueFree();
				_commandAutocomplete = new PopupMenu { Unfocusable = true };
				for (int i = 0; i < suggestions.Length; i++) _commandAutocomplete.AddItem(suggestions[i], i);
				_commandAutocomplete.IdPressed += id =>
				{
					_chatField.Text = suggestions[(int)id] + " ";
					_chatField.CaretColumn = _chatField.Text.Length;
					_commandAutocomplete?.Hide();
					_chatField.GrabFocus();
				};
				AddChild(_commandAutocomplete);
				Vector2I at = (Vector2I)(_chatField.GlobalPosition + new Vector2(0, _chatField.Size.Y));
				_commandAutocomplete.Popup(new Rect2I(at, Vector2I.Zero));
				_chatField.CallDeferred(Control.MethodName.GrabFocus);
				return;
			}
		}
		_commandAutocomplete?.Hide();

		int colonIdx = -1;
		int pending = -1;
		for (int i = 0; i < cursorPos; i++)
		{
			if (newText[i] == ':')
				pending = pending >= 0 ? -1 : i;
		}
		colonIdx = pending;

		if (colonIdx >= 0 && cursorPos > colonIdx + 1)
		{
			string partial = newText[(colonIdx + 1)..cursorPos];
			if (!partial.Contains(' ') && partial.Length >= 1)
			{
				var wasClosed = !_emojiPicker.Visible;
				_emojiPicker.ShowAutocomplete(partial);
				if (_emojiPicker.VisibleItemCount > 0)
				{
					_isAutocompleteOpen = true;
					_emojiButton.ButtonPressed = true;
					_emojiPicker.Size = new Vector2(_chatPanel.Size.X, 60);
					PositionPickerBelowField();
					if (wasClosed)
						_emojiPicker.Visible = true;
					return;
				}
			}
		}

		if (_isAutocompleteOpen)
		{
			CloseEmojiPicker();
		}
	}

	private void PositionPickerBelowField()
	{
		Vector2 selfGlobal = GlobalPosition;
		Vector2 panelGlobal = _chatFieldPanel.GlobalPosition;
		_emojiPicker.Position = new Vector2(
			panelGlobal.X - selfGlobal.X,
			panelGlobal.Y - selfGlobal.Y + _chatFieldPanel.Size.Y + 4
		);
	}

	private void CloseEmojiPicker()
	{
		_emojiPicker.Visible = false;
		_isAutocompleteOpen = false;
		_emojiButton.ButtonPressed = false;
	}

	private static void RecordEmojisFromText(string msg)
	{
		var builtIn = ChatService.BuiltInEmojis;
		int idx = 0;
		while ((idx = msg.IndexOf(':', idx)) >= 0)
		{
			int end = msg.IndexOf(':', idx + 1);
			if (end > idx + 1)
			{
				string name = msg[(idx + 1)..end];
				if (builtIn.ContainsKey(name))
					UIEmojiPicker.RecordEmojiUse(name);
			}
			idx = end >= 0 ? end + 1 : idx + 1;
		}
	}

	private void OnEmojiPicked(string emojiName)
	{
		InsertEmojiAtCursor(emojiName);
	}

	private void InsertEmojiAtCursor(string emojiName)
	{
		if (!Input.IsKeyPressed(Key.Shift))
			CloseEmojiPicker();

		_suppressAutocomplete = true;

		try
		{
			UIEmojiPicker.InsertEmojiAtCursor(_chatField, emojiName);
		}
		finally
		{
			_suppressAutocomplete = false;
		}
		_chatField.GrabFocus();
	}
}
