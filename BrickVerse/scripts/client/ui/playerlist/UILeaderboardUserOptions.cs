// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;

namespace BrickVerse.Client.UI.Playerlist;

public partial class UILeaderboardUserOptions : Control
{
	[Export] private AnimationPlayer _animPlay = null!;
	[Export] private Control _optionsLayout = null!;
	[Export] private Button _addFriendBtn = null!;
	[Export] private Button _removeFriendBtn = null!;
	[Export] private Button _viewProfileBtn = null!;
	[Export] private Control _loaderView = null!;
	public bool Active { get; private set; } = false;
	public UILeaderboardUserItem? Target;
	private World _root = null!;
	private int _lastReq = 0;
	private Button _blockButton = null!;
	private Button _muteVoiceButton = null!;

	public override void _Ready()
	{
		Visible = false;
		_viewProfileBtn.Pressed += OnViewProfile;
		_addFriendBtn.Pressed += OnAddFriend;
		_removeFriendBtn.Pressed += OnRemoveFriend;
		_blockButton = new Button { Text = "Block User", TooltipText = "Hide this user's chat and remove the friendship" };
		_blockButton.Pressed += OnBlock; _optionsLayout.AddChild(_blockButton);
		_muteVoiceButton = new Button { Text = "Mute Voice" }; _muteVoiceButton.Pressed += OnMuteVoice; _optionsLayout.AddChild(_muteVoiceButton);
		base._Ready();
	}

	private void OnMuteVoice()
	{
		if (Target == null) return;
		bool muted = _root.VoiceChat.IsPlayerMuted(Target.TargetPlayer);
		if (muted) _root.VoiceChat.UnmutePlayer(Target.TargetPlayer); else _root.VoiceChat.MutePlayer(Target.TargetPlayer);
		_muteVoiceButton.Text = muted ? "Mute Voice" : "Unmute Voice";
	}

	private void OnBlock()
	{
		if (Target == null) return;
		_root.Social.LocalSendFriendshipRequest(Target.TargetPlayer, Datamodel.Services.SocialService.FriendshipRequestType.Block);
		Disappear();
	}

	private void OnAddFriend()
	{
		if (Target == null) return;
		_root.Social.LocalSendFriendshipRequest(Target.TargetPlayer, Datamodel.Services.SocialService.FriendshipRequestType.Friend);
		Disappear();
	}

	private void OnRemoveFriend()
	{
		if (Target == null) return;
		_root.Social.LocalSendFriendshipRequest(Target.TargetPlayer, Datamodel.Services.SocialService.FriendshipRequestType.Unfriend);
		Disappear();
	}

	private void OnViewProfile()
	{
		if (Target == null) return;
		OS.ShellOpen($"https://brickverse.gg/@{Target.TargetPlayer.Name}");

		Disappear();
	}

	private void ShowLoader(bool show)
	{
		_loaderView.Visible = show;
		_optionsLayout.Visible = !show;
	}

	public async void PopupAt(UILeaderboardUserItem item)
	{
		if (Active) return;
		_lastReq++;
		Active = true;
		Target = item;
		_root = Target.Leaderboard.CoreUI.Root;

		GlobalPosition = item.GetNode<Control>("InfoSpawn").GlobalPosition;
		_animPlay.Stop();
		_animPlay.Play("appear");


		int myReq = _lastReq;

		ShowLoader(true);

		// Fetch friendship status
		bool isFriends = await _root.Social.WebCheckAreFriends(_root.Players.LocalPlayer.UserID, item.TargetPlayer.UserID);

		// If another option opened
		if (myReq != _lastReq) return;
		_addFriendBtn.Visible = !isFriends;
		_removeFriendBtn.Visible = isFriends;
		_muteVoiceButton.Visible = World.Current?.Players.LocalPlayer?.CanVoiceChat == true && item.TargetPlayer.CanVoiceChat;
		_muteVoiceButton.Text = _root.VoiceChat.IsPlayerMuted(item.TargetPlayer) ? "Unmute Voice" : "Mute Voice";
		ShowLoader(false);
	}

	public void Disappear()
	{
		if (!Active) return;
		Active = false;
		Target = null;
		_animPlay.Stop();
		_animPlay.Play("disappear");
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton btn && btn.IsReleased())
		{
			if (!_optionsLayout.GetGlobalRect().HasPoint(btn.GlobalPosition))
			{
				if (Target != null && Target.GetGlobalRect().HasPoint(btn.GlobalPosition))
				{
					return; // Click was on the target item, ignore
				}

				Disappear();
			}
		}
		base._Input(@event);
	}
}
