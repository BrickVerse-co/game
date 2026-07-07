// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;

namespace Polytoria.Mobile.UI;

public partial class FeedRoot : Node
{
	private const string FeedCardPath = "res://scenes/mobile/components/home/feed_card.tscn";

	private PackedScene _feedCard = null!;
	[Export] private Control _feedContainer = null!;

	public override void _Ready()
	{
		LoadFeed();
	}

	private async void LoadFeed()
	{
		try
		{
			APIFeedPostRoot feed = await PolyAPI.GetFeedPosts();
			foreach (APIFeedPostData item in feed.Data)
			{
				FeedPostCard card = Globals.CreateInstanceFromScene<FeedPostCard>(FeedCardPath);
				card.Data = item;
				_feedContainer.AddChild(card);
			}
		}
		catch
		{
			PT.PrintErr("Failed to load feed");
		}
	}
}
