using System;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileNotificationCard : Button
{
	private Label _title = null!;
	private Label _message = null!;
	private Label _time = null!;
	private Control _unread = null!;

	public override void _Ready()
	{
		_title = GetNode<Label>("Content/Copy/Header/Title");
		_time = GetNode<Label>("Content/Copy/Header/Time");
		_message = GetNode<Label>("Content/Copy/Message");
		_unread = GetNode<Control>("Content/Unread");
		MobileMotion.Bind(this);
	}

	public void Configure(string title, string message, DateTime createdAt, bool isRead)
	{
		_title.Text = title;
		_message.Text = message;
		_unread.Visible = !isRead;
		DateTime utc = createdAt.Kind == DateTimeKind.Utc ? createdAt : DateTime.SpecifyKind(createdAt, DateTimeKind.Utc);
		TimeSpan elapsed = DateTime.UtcNow - utc;
		_time.Text = elapsed.TotalMinutes < 1 ? "Now"
			: elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes}m"
			: elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours}h"
			: elapsed.TotalDays < 7 ? $"{(int)elapsed.TotalDays}d"
			: utc.ToLocalTime().ToString("MMM d");
	}

	public void MarkRead() => _unread.Visible = false;
}
