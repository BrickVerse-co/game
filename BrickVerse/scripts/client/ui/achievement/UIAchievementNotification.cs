// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Utils;

namespace Polytoria.Client.UI.Notification;

public partial class UIAchievementNotification : UINotificationBase
{
	private const int NotifyTimeout = 10;
	[Export] public AnimationPlayer AnimPlay = null!;
	[Export] public Label AchievementTitle = null!;
	[Export] public TextureRect AchievementTexture = null!;
	[Export] public AudioStreamPlayer _soundPlay = null!;
	private PTImageAsset? _badgeImg;
	private bool _playing = false;

	public override async void Fire(object? data)
	{
		if (data is AchievementNotifyPayload payload)
		{
			try
			{
				Visible = false;
				APIStoreItem item = await PolyAPI.GetStoreItem(payload.Id);

				_badgeImg = new();
				_badgeImg.ResourceLoaded += OnBadgeImgLoaded;
				_badgeImg.ImageType = ImageTypeEnum.AssetThumbnail;
				_badgeImg.ImageID = (uint)payload.Id;
				_badgeImg.LoadResource();
				AchievementTitle.Text = item.Name;
				AnimPlay.Play("appear");

				if (NotificationCenter.CoreUI.Root.Achievements.UseAchievementSound)
				{
					_soundPlay.Play();
				}
			}
			catch
			{
				QueueFree();
				throw;
			}
		}
		else
		{
			QueueFree();
		}
	}

	public override void _ExitTree()
	{
		_badgeImg?.ResourceLoaded -= OnBadgeImgLoaded;
		base._ExitTree();
	}

	private async void Timeout()
	{
		await ToSignal(GetTree().CreateTimer(NotifyTimeout), SceneTreeTimer.SignalName.Timeout);
		if (IsInstanceValid(this) && !_playing)
		{
			QueueFree();
		}
	}

	private void OnBadgeImgLoaded(Resource resource)
	{
		AchievementTexture.Texture = (Texture2D)resource;
	}

	public struct AchievementNotifyPayload
	{
		public int Id;
	}
}
