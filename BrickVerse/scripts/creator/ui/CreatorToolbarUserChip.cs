using Godot;
using BrickVerse.Creator.Utils;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using SystemNetHttp = System.Net.Http;

namespace BrickVerse.Creator.UI;

public sealed partial class CreatorToolbarUserChip : HBoxContainer
{
	private const int AvatarSize = 24;
	private const int BadgeSize = 16;
	private const int MenuSwitchAccount = 0;
	private const int MenuRefreshIdentity = 1;
	private const int MenuCopyUserId = 2;
	private const int MenuSignOut = 3;

	private readonly SystemNetHttp.HttpClient _http = new();

	private TextureRect _avatar = null!;
	private TextureRect _badge = null!;
	private MenuButton _usernameMenu = null!;
	private int _avatarRequestId;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Pass;
		AddThemeConstantOverride("separation", 8);

		CreateUi();
		BindEvents();
		RefreshDisplay();
	}

	public override void _ExitTree()
	{
		CreatorAPI.UserAuthenticated -= OnUserAuthenticated;
		CreatorAPI.ToolbarIdentityUpdated -= OnToolbarIdentityUpdated;
		_http.Dispose();
		base._ExitTree();
	}

	private void CreateUi()
	{
		CustomMinimumSize = new(0, AvatarSize);

		_usernameMenu = new MenuButton
		{
			Flat = true,
			SwitchOnHover = true,
			MouseFilter = MouseFilterEnum.Stop,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			TooltipText = "Account options",
		};
		_usernameMenu.AddThemeFontSizeOverride("font_size", 13);
		_usernameMenu.CustomMinimumSize = new(0, AvatarSize);

		PopupMenu menu = _usernameMenu.GetPopup();
		menu.AddItem("Switch account...", MenuSwitchAccount);
		menu.AddItem("Refresh identity", MenuRefreshIdentity);
		menu.AddSeparator();
		menu.AddItem("Copy user ID", MenuCopyUserId);
		menu.AddSeparator();
		menu.AddItem("Log out", MenuSignOut);
		menu.IdPressed += OnMenuIdPressed;

		_avatar = new TextureRect
		{
			MouseFilter = MouseFilterEnum.Stop,
			CustomMinimumSize = new(AvatarSize, AvatarSize),
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			TooltipText = "Authenticated account",
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
		};
		_avatar.Material = CreateCircleMaskMaterial();
		_avatar.GuiInput += OnAvatarGuiInput;

		_badge = new TextureRect
		{
			MouseFilter = MouseFilterEnum.Ignore,
			CustomMinimumSize = new(BadgeSize, BadgeSize),
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Visible = false,
		};

		AddChild(_badge);
		AddChild(_avatar);
		AddChild(_usernameMenu);
	}

	private void OnAvatarGuiInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
		{
			_usernameMenu.ShowPopup();
			AcceptEvent();
		}
	}

	private void BindEvents()
	{
		CreatorAPI.UserAuthenticated += OnUserAuthenticated;
		CreatorAPI.ToolbarIdentityUpdated += OnToolbarIdentityUpdated;
	}

	private void OnUserAuthenticated(OpenIdUserInfoResponse _)
	{
		RefreshDisplay();
	}

	private void OnToolbarIdentityUpdated(CreatorAPI.ToolbarIdentity? _)
	{
		RefreshDisplay();
	}

	private void RefreshDisplay()
	{
		CreatorAPI.ToolbarIdentity? identity = CreatorAPI.CurrentToolbarIdentity;
		OpenIdUserInfoResponse? openId = CreatorAPI.CurrentUserInfo;

		string username = ResolveUsername(identity, openId);
		_usernameMenu.Text = username;
		_usernameMenu.TooltipText = username;
		TooltipText = username;
		UpdateMenuState();

		string headshotUrl = ResolveHeadshotUrl(identity, openId);
		_ = UpdateAvatarAsync(headshotUrl);

		UpdateBadge(identity);
	}

	private void UpdateMenuState()
	{
		PopupMenu menu = _usernameMenu.GetPopup();
		bool isAuthenticated = CreatorAPI.IsUserAuthenticated;
		bool hasUserId = !string.IsNullOrWhiteSpace(CreatorAPI.UserID) && CreatorAPI.UserID != "0";

		menu.SetItemDisabled(menu.GetItemIndex(MenuRefreshIdentity), !isAuthenticated);
		menu.SetItemDisabled(menu.GetItemIndex(MenuCopyUserId), !hasUserId);
		menu.SetItemDisabled(menu.GetItemIndex(MenuSignOut), !isAuthenticated);
	}

	private async void OnMenuIdPressed(long id)
	{
		switch ((int)id)
		{
			case MenuSwitchAccount:
				await CreatorAPI.SwitchAccount();
				break;
			case MenuRefreshIdentity:
				await CreatorAPI.RefreshToolbarIdentityAsync();
				break;
			case MenuCopyUserId:
				if (!string.IsNullOrWhiteSpace(CreatorAPI.UserID) && CreatorAPI.UserID != "0")
				{
					DisplayServer.ClipboardSet(CreatorAPI.UserID);
				}
				break;
			case MenuSignOut:
				CreatorAPI.ClearAuth();
				break;
		}
	}

	private static string ResolveUsername(
		CreatorAPI.ToolbarIdentity? identity,
		OpenIdUserInfoResponse? openId
	)
	{
		if (identity.HasValue && !string.IsNullOrWhiteSpace(identity.Value.Username))
		{
			return identity.Value.Username;
		}

		if (openId.HasValue)
		{
			if (!string.IsNullOrWhiteSpace(openId.Value.PreferredUsername))
			{
				return openId.Value.PreferredUsername;
			}

			if (!string.IsNullOrWhiteSpace(openId.Value.Name))
			{
				return openId.Value.Name;
			}
		}

		return CreatorAPI.Username;
	}

	private static string ResolveHeadshotUrl(CreatorAPI.ToolbarIdentity? identity, OpenIdUserInfoResponse? openId)
	{
		// The authenticated thumbnail route is authoritative and resolves the
		// user's current rendered headshot instead of an account-wide fallback.
		string userId = openId?.Sub ?? CreatorAPI.UserID;
		if (!string.IsNullOrWhiteSpace(userId) && userId != "0")
		{
			return Globals.ApiEndpoint.PathJoin(
				$"/v3/user/{Uri.EscapeDataString(userId)}/thumbnail?size=48"
			);
		}

		if (identity.HasValue && !string.IsNullOrWhiteSpace(identity.Value.HeadshotUrl))
		{
			return NormalizeUrl(identity.Value.HeadshotUrl);
		}

		if (openId.HasValue)
		{
			if (!string.IsNullOrWhiteSpace(openId.Value.Picture))
			{
				return NormalizeUrl(openId.Value.Picture);
			}

			if (!string.IsNullOrWhiteSpace(openId.Value.HeadshotUrl))
			{
				return NormalizeUrl(openId.Value.HeadshotUrl);
			}
		}

		return "";
	}

	private void UpdateBadge(CreatorAPI.ToolbarIdentity? identity)
	{
		Texture2D? texture = null;
		string tooltip = "";

		if (identity.HasValue)
		{
			if (!string.IsNullOrWhiteSpace(identity.Value.BadgeIconPath))
			{
				texture = GD.Load<Texture2D>(identity.Value.BadgeIconPath);
				tooltip = identity.Value.BadgeTooltip ?? "";
			}
		}

		_badge.Texture = texture;
		_badge.Visible = texture != null;
		_badge.TooltipText = tooltip;
	}

	private async System.Threading.Tasks.Task UpdateAvatarAsync(string url)
	{
		int requestId = ++_avatarRequestId;
		await UpdateAvatarTextureAsync(url, requestId);
	}

	private async System.Threading.Tasks.Task UpdateAvatarTextureAsync(string url, int requestId)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			if (requestId == _avatarRequestId) _avatar.Texture = null;
			return;
		}

		try
		{
			using SystemNetHttp.HttpResponseMessage response = await _http.GetAsync(url);
			if (!response.IsSuccessStatusCode || requestId != _avatarRequestId)
			{
				if (requestId == _avatarRequestId) _avatar.Texture = null;
				return;
			}

			byte[] data = await response.Content.ReadAsByteArrayAsync();
			if (requestId != _avatarRequestId)
			{
				return;
			}

			Image img = new();
			Error err = img.LoadPngFromBuffer(data);
			if (err != Error.Ok)
			{
				err = img.LoadJpgFromBuffer(data);
			}

			if (err != Error.Ok || requestId != _avatarRequestId)
			{
				if (requestId == _avatarRequestId) _avatar.Texture = null;
				return;
			}

			_avatar.Texture = ImageTexture.CreateFromImage(img);
		}
		catch
		{
			if (requestId == _avatarRequestId) _avatar.Texture = null;
		}
	}

	private static string NormalizeUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return "";
		}

		if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
		{
			return url;
		}

		return Globals.ApiEndpoint.PathJoin(url);
	}

	private static ShaderMaterial CreateCircleMaskMaterial()
	{
		Shader shader = new()
		{
			Code = """
shader_type canvas_item;

void fragment() {
	vec2 centered = UV - vec2(0.5);
	float dist = length(centered * 2.0);
	if (dist > 1.0) {
		COLOR = vec4(0.0);
	} else {
		COLOR = texture(TEXTURE, UV) * COLOR;
	}
}
"""
		};

		return new ShaderMaterial
		{
			Shader = shader
		};
	}
}
