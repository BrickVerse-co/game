// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Services;
using BrickVerse.Shared;
using BrickVerse.Client.Settings;

namespace BrickVerse.Client;

public partial class Nametag : Node3D
{
	private Label _titleLabel = null!;
	private ProgressBar _healthBar = null!;
	private TextureRect _verifiedBadge = null!;
	private TextureRect _deviceIcon = null!;
	private Node3D _nametag = null!;
	private NetworkService.ClientPlatformEnum? _displayedPlatform;

	public NPC Target = null!;

	public override void _Ready()
	{
		_nametag = Globals.CreateInstanceFromScene<Node3D>("res://scenes/client/spatial/nametag.tscn");
		AddChild(_nametag);
		_titleLabel = _nametag.GetNode<Label>("SubViewport/Control/NameRow/Title");
		_verifiedBadge = _nametag.GetNode<TextureRect>("SubViewport/Control/NameRow/Verified");
		_deviceIcon = _nametag.GetNode<TextureRect>("SubViewport/Control/NameRow/Device");
		_healthBar = _nametag.GetNode<ProgressBar>("SubViewport/Control/Healthbar");
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		UpdateNameTag();
	}

	public void UpdateNameTag()
	{
		bool useNametag = Target.UseNametag;

		Camera? cam = Target.Root.Environment.CurrentCamera;

		// Check distance from camera if is with-in radius
		if (cam != null && useNametag)
		{
			useNametag = (cam.Position - GlobalPosition).Length() < Target.NametagVisibleRadius;
		}

		if (Target is Player playerTarget)
		{
			bool isLocalPlayer = Target == Target.Root.Players?.LocalPlayer;
			if (isLocalPlayer)
			{
				bool userAllowsOwnNametag = ClientSettingsService.Instance == null
					|| ClientSettingsService.Instance.Get<bool>(ClientSettingKeys.General.ShowOwnNametag);
				useNametag = useNametag
					&& playerTarget.ShowNametagToLocalPlayer
					&& userAllowsOwnNametag;
			}
			else
			{
				useNametag = useNametag && playerTarget.ShowNametagToOtherPlayers;
			}
		}

		Visible = useNametag;
		_titleLabel.Text = Target.DisplayName != string.Empty ? Target.DisplayName : Target.Name;
		if (Target is Player player)
		{
			_verifiedBadge.Visible = player.HasVerifiedBadge;
			_deviceIcon.Visible = true;
			if (_displayedPlatform != player.UserPlatform)
			{
				_displayedPlatform = player.UserPlatform;
				_deviceIcon.Texture = GD.Load<Texture2D>(DeviceIconPath(player.UserPlatform));
				_deviceIcon.TooltipText = DeviceLabel(player.UserPlatform);
			}
		}
		else
		{
			_verifiedBadge.Visible = false;
			_deviceIcon.Visible = false;
		}
		_healthBar.Visible = (Target.Health < Target.MaxHealth);
		_healthBar.Value = Target.Health;
		_healthBar.MaxValue = Target.MaxHealth;
	}

	private static string DeviceIconPath(NetworkService.ClientPlatformEnum platform) => platform switch
	{
		NetworkService.ClientPlatformEnum.Mobile => "res://assets/textures/client/ui/devices/phone.svg",
		NetworkService.ClientPlatformEnum.Tablet => "res://assets/textures/client/ui/devices/tablet.svg",
		NetworkService.ClientPlatformEnum.Console => "res://assets/textures/client/ui/devices/console.svg",
		_ => "res://assets/textures/client/ui/devices/pc.svg"
	};

	private static string DeviceLabel(NetworkService.ClientPlatformEnum platform) => platform switch
	{
		NetworkService.ClientPlatformEnum.Mobile => "Phone",
		NetworkService.ClientPlatformEnum.Tablet => "Tablet",
		NetworkService.ClientPlatformEnum.Console => "Console",
		NetworkService.ClientPlatformEnum.VR => "VR",
		_ => "PC"
	};
}
