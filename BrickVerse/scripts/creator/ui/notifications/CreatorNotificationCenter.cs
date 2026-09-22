// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using Godot;
using BrickVerse.Datamodel.Creator;
using System;
using System.Collections.Generic;

namespace BrickVerse.Creator.UI;

public static class CreatorNotificationCenter
{
	private sealed record Notice(string Title, string Message, string Kind, DateTime CreatedAt, bool Read);
	private static readonly List<Notice> Notices = [];
	private static Button? _button;
	private static PopupPanel? _popup;
	private static VBoxContainer? _list;

	public static void Initialize(Button button)
	{
		_button = button;
		button.Pressed += Toggle;
		RefreshBadge();
	}

	public static void Notify(string title, string message, string kind = "info", bool requestNativeAttention = true)
	{
		Notices.Insert(0, new(title.Trim(), message.Trim(), kind.Trim().ToLowerInvariant(), DateTime.Now, false));
		if (Notices.Count > 100) Notices.RemoveRange(100, Notices.Count - 100);
		RefreshBadge();
		RefreshList();
		if (requestNativeAttention) DisplayServer.WindowRequestAttention();
	}

	private static void Toggle()
	{
		if (_button == null) return;
		EnsurePopup();
		RefreshList();
		Vector2 anchor = _button.GetScreenPosition();
		_popup!.Position = new Vector2I((int)anchor.X - 330, (int)anchor.Y + 34);
		_popup.Popup();
	}

	private static void EnsurePopup()
	{
		if (_popup != null && GodotObject.IsInstanceValid(_popup)) return;
		_popup = new PopupPanel { Size = new Vector2I(370, 430), MinSize = new Vector2I(330, 300), TransparentBg = true };
		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 14); margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 14); margin.AddThemeConstantOverride("margin_bottom", 12);
		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 10);
		HBoxContainer header = new();
		Label heading = new() { Text = "Notifications", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		heading.AddThemeFontSizeOverride("font_size", 18);
		header.AddChild(heading);
		Button read = new() { Text = "Mark all as read", Flat = true };
		read.Pressed += () => { for (int i = 0; i < Notices.Count; i++) Notices[i] = Notices[i] with { Read = true }; RefreshBadge(); RefreshList(); };
		header.AddChild(read); layout.AddChild(header);
		ScrollContainer scroll = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		_list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		scroll.AddChild(_list); layout.AddChild(scroll); margin.AddChild(layout); _popup.AddChild(margin);
		CreatorService.Interface.GetTree().Root.AddChild(_popup);
	}

	private static void RefreshList()
	{
		if (_list == null || !GodotObject.IsInstanceValid(_list)) return;
		foreach (Node child in _list.GetChildren()) child.QueueFree();
		if (Notices.Count == 0) { _list.AddChild(new Label { Text = "You're all caught up", HorizontalAlignment = HorizontalAlignment.Center }); return; }
		foreach (Notice notice in Notices)
		{
			Button row = new() { Text = $"{notice.Title}\n{notice.Message}\n{notice.CreatedAt:t}", Alignment = HorizontalAlignment.Left, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 66) };
			row.Modulate = notice.Read ? new Color(1, 1, 1, .6f) : Colors.White;
			row.Pressed += () => { int index = Notices.IndexOf(notice); if (index >= 0) Notices[index] = notice with { Read = true }; RefreshBadge(); RefreshList(); };
			_list.AddChild(row);
		}
	}

	private static void RefreshBadge()
	{
		if (_button == null || !GodotObject.IsInstanceValid(_button)) return;
		int unread = Notices.FindAll(notice => !notice.Read).Count;
		_button.TooltipText = unread == 0 ? "Notifications" : $"Notifications ({unread} unread)";
		_button.SelfModulate = unread == 0 ? Colors.White : new Color("36a9ff");
	}
}
