// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
namespace BrickVerse.Mobile.UI;

public partial class MobileFeedRow : PanelContainer
{
	public void Configure(string username, string content)
	{
		GetNode<Label>("Layout/Username").Text = username;
		GetNode<Label>("Layout/Content").Text = content;
	}
}
