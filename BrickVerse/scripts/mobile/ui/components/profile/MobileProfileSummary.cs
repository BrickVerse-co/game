// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;
using Godot;
using System;

namespace BrickVerse.Mobile.UI;

public partial class MobileProfileSummary : VBoxContainer
{
	public void Configure(string userId, string username, string status, string description, int visits, int views, int posts, string joined, bool verified)
	{
		GetNode<Label>("Identity/Content/Copy/NameRow/Username").Text = username;
		GetNode<TextureRect>("Identity/Content/Copy/NameRow/Verified").Visible = verified;
		GetNode<Label>("Identity/Content/Copy/Status").Text = status;
		GetNode<Label>("About/Description").Text = description;
		GetNode<Label>("Stats/Visits/Value").Text = visits.ToString("N0");
		GetNode<Label>("Stats/Views/Value").Text = views.ToString("N0");
		GetNode<Label>("Stats/Posts/Value").Text = posts.ToString("N0");
		Label joinedLabel = GetNode<Label>("Joined");
		if (DateTime.TryParse(joined, out DateTime joinedAt))
		{
			joinedAt = joinedAt.ToLocalTime();
			joinedLabel.Text = "Member since " + joinedAt.ToString("MMMM d, yyyy");
			joinedLabel.TooltipText = joinedAt.ToString("f");
		}
		else joinedLabel.Text = "";
		_ = LoadAvatar(userId);
	}

	private async System.Threading.Tasks.Task LoadAvatar(string userId)
	{
		string url = await BVAPI.ResolveThumbnailUrl("USER_HEADSHOT", userId);
		if (string.IsNullOrWhiteSpace(url) || !IsInsideTree()) return;
		TextureRect avatar = GetNode<TextureRect>("Identity/Content/Avatar");
		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, resource => { if (IsInstanceValid(avatar)) avatar.Texture = (Texture2D)resource; });
	}
}
