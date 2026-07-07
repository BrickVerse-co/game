// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.UI.Notification;

namespace Polytoria.Client.UI;

public partial class UINotification : Control
{
	private const string NotificationLocation = "res://scenes/client/ui/core/notification";
	[Export] private Control _container = null!;
	public CoreUIRoot CoreUI { get; set; } = null!;

	public override void _Ready()
	{
		CoreUI.Root.Achievements.GotAchievement.Connect(OnGotAchievement);
		base._Ready();
	}

	private void OnGotAchievement(int id)
	{
		if (!CoreUI.Root.Achievements.NotifyAchievements) return;
		FireNotification(NotificationType.Achievement, new UIAchievementNotification.AchievementNotifyPayload() { Id = id });
	}

	public void FireMessage(string title, string subTitle = "", Texture2D? image = null)
	{
		FireNotification(NotificationType.Message, new UIMessageNotification.MessageNotifyPayload() { Title = title, SubTitle = subTitle, Icon = image });
	}

	public void FireNotification(NotificationType notifyType, object? payload = null)
	{
		string sceneToLoad = "";
		switch (notifyType)
		{
			case NotificationType.Achievement:
				sceneToLoad = NotificationLocation.PathJoin("achievement_toast.tscn");
				break;
			case NotificationType.Message:
				sceneToLoad = NotificationLocation.PathJoin("message_toast.tscn");
				break;
			case NotificationType.Screenshot:
				sceneToLoad = NotificationLocation.PathJoin("screenshot_toast.tscn");
				break;
			case NotificationType.FriendRequest:
				sceneToLoad = NotificationLocation.PathJoin("friend_request_toast.tscn");
				break;
		}

		PackedScene packed = GD.Load<PackedScene>(sceneToLoad);
		UINotificationBase noti = packed.Instantiate<UINotificationBase>();
		noti.NotificationCenter = this;
		_container.AddChild(noti);
		noti.Fire(payload);
	}

	public enum NotificationType
	{
		Achievement,
		Message,
		Screenshot,
		FriendRequest
	}
}
