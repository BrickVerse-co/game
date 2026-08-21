// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web;
using BrickVerse.Client;
using BrickVerse.Mobile.UI;
using BrickVerse.Mobile.Utils;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Utils;
using DeepLinkAddon;
using Godot;

namespace BrickVerse.Mobile;

public partial class MobileUI : Control
{
	public static MobileUI Singleton { get; private set; } = null!;

	public MobileUI()
	{
		Singleton = this;
	}

	public event Action<MobileViewEnum>? ViewPathSwitched;

	private Control _mainView = null!;
	public MobileViewBase? CurrentViewNode;
	public MobileViewEnum CurrentView;

	[Export]
	public StartupSplash? StartSplash { get; private set; }

	[Export]
	public NewUserSplash NewUserSplash = null!;

	[Export]
	public MobileLoadingScreen LoadingScreen = null!;

	private Deeplink? _deepLink;
	private MobileAuthBrowser? _authBrowser;
	private readonly Dictionary<MobileViewEnum, MobileViewBase> _viewCache = [];
	private bool _disposed;
	private XRMobileShell? _xrShell;

	public override void _Ready()
	{
		// Keep Godot dialogs embedded in the app viewport on mobile. Native
		// subwindows are inconsistent or unsupported on iOS and Android.
		GetWindow().GuiEmbedSubwindows = true;
		AddChild(new MobileControllerNavigation { Name = "ControllerNavigation" });
		if (Globals.IsXRLaunch)
		{
			_xrShell = new XRMobileShell { Name = "XRMobileShell" };
			AddChild(_xrShell);
			_xrShell.Initialize();
		}
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();
		cmdargs.TryGetValue("token", out string? mobileToken);
		cmdargs.TryGetValue("code", out string? mobileCode);
		cmdargs.TryGetValue("state", out string? mobileState);

		// Deep links are an optional Android/iOS export plugin. Emulator and
		// sideload builds may not include its singleton, so don't initialize a
		// wrapper that can only emit errors. Browser/code login remains usable.
		if (Engine.HasSingleton("DeeplinkPlugin"))
		{
			_deepLink = new Deeplink();
			AddChild(_deepLink, true);
			if (_deepLink.Initialize() == (int)Error.Ok)
				_deepLink.DeeplinkReceived += OnDeeplinkReceived;
			else
				BV.PrintErr("Deep-link initialization failed; continuing without app links.");
		}
		else
		{
			BV.Print("Deep-link plugin unavailable; continuing without app links.");
		}

		_authBrowser = new MobileAuthBrowser();
		AddChild(_authBrowser, true);
		_authBrowser.CallbackReceived += OnAuthBrowserCallback;
		BVMobileAuthAPI.InAppBrowserLauncher = _authBrowser.Open;
		BVHttpClient.NativeSender = _authBrowser.SendAsync;

		if (Globals.IsMobileBuild && !Globals.IsXRLaunch)
		{
			GetTree().Root.ContentScaleFactor = Globals.MobileScale;
		}

		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		if (StartSplash != null)
		{
			StartSplash!.Visible = true;
		}

		BVMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		BVMobileAuthAPI.AskForAuthentication += OnAskForAuthentication;

		_ = InitializeAuthenticationAsync();
		if (mobileToken != null)
		{
			_ = CompleteAuthenticationAsync(() => BVMobileAuthAPI.LoginWithAuthToken(mobileToken));
		}
		else if (mobileCode != null && mobileState != null)
		{
			_ = CompleteAuthenticationAsync(() =>
				BVMobileAuthAPI.LoginWithCodeAndState(mobileCode, mobileState)
			);
		}

		_mainView = GetNode<Control>("Layout/MainView");
		if (Globals.IsMobileBuild && !Globals.IsXRLaunch)
		{
			DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		}

		if (Globals.IsInGDEditor)
		{
			DisplayServer.WindowSetSize((Vector2I)new Vector2(412, 700));
		}
	}

	public override void _ExitTree()
	{
		_disposed = true;
		BVMobileAuthAPI.UserAuthenticated -= OnUserAuthenticated;
		BVMobileAuthAPI.AskForAuthentication -= OnAskForAuthentication;
		if (_deepLink != null)
			_deepLink.DeeplinkReceived -= OnDeeplinkReceived;
		if (_authBrowser != null)
			_authBrowser.CallbackReceived -= OnAuthBrowserCallback;
		BVMobileAuthAPI.InAppBrowserLauncher = null;
		BVHttpClient.NativeSender = null;
		if (ReferenceEquals(Singleton, this))
			Singleton = null!;
		base._ExitTree();
	}

	private async void OnAuthBrowserCallback(string rawUrl)
	{
		try
		{
			Uri uri = new(rawUrl);
			if (
				!uri.Scheme.Equals("brickverse", StringComparison.OrdinalIgnoreCase)
				|| !uri.Host.Equals("auth", StringComparison.OrdinalIgnoreCase)
			)
				return;
			NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
			string? code = query.Get("code");
			string? state = query.Get("state");
			if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
				throw new InvalidOperationException("The sign-in response was incomplete.");
			await CompleteAuthenticationAsync(() =>
				BVMobileAuthAPI.LoginWithCodeAndState(code, state)
			);
		}
		catch (Exception exception)
		{
			BV.PrintErr("In-app authentication callback failed: ", exception);
			if (!_disposed && IsInstanceValid(this))
				OS.Alert(exception.Message, "Authentication Failure");
		}
	}

	private async System.Threading.Tasks.Task InitializeAuthenticationAsync()
	{
		try
		{
			await BVMobileAuthAPI.SetupClient();
		}
		catch (Exception exception)
		{
			// SetupClient is defensive itself; retain this boundary so a future
			// provider implementation cannot terminate the Android process.
			BV.PrintErr("Unexpected mobile authentication startup failure: ", exception);
			if (IsInstanceValid(this))
				OnAskForAuthentication();
		}
	}

	private void OnUserAuthenticated(APIV3AuthMeUser me)
	{
		if (_disposed || !IsInstanceValid(this))
			return;
		HideStartupSplash();
		if (NewUserSplash != null && IsInstanceValid(NewUserSplash))
		{
			NewUserSplash.Visible = false;
		}
		if (CurrentViewNode == null)
			SwitchTo(MobileViewEnum.Home);
	}

	private void OnAskForAuthentication()
	{
		if (_disposed || !IsInstanceValid(this))
			return;
		HideStartupSplash();
		NewUserSplash.ShowSplash();
	}

	private void HideStartupSplash()
	{
		StartSplash?.HideSplash();
		StartSplash = null;
	}

	private async void OnDeeplinkReceived(DeeplinkURL url)
	{
		// Handle brickverse://auth link
		if (url.Host == "auth")
		{
			NameValueCollection authQuery = HttpUtility.ParseQueryString(url.Query);
			string? code = authQuery.Get("code");
			string? state = authQuery.Get("state");
			if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
			{
				OS.Alert(
					"The sign-in link is incomplete. Please start sign-in again.",
					"Authentication Failure"
				);
				return;
			}

			await CompleteAuthenticationAsync(() =>
				BVMobileAuthAPI.LoginWithCodeAndState(code, state)
			);
		}

		if (url.Host == "client")
		{
			BV.Print(url);
		}
	}

	private async System.Threading.Tasks.Task CompleteAuthenticationAsync(
		Func<System.Threading.Tasks.Task> authenticate
	)
	{
		LoadingScreen.ShowScreen("Preparing world", "Checking the experience and your access…");
		try
		{
			await authenticate();
		}
		catch (Exception exception)
		{
			BV.PrintErr("Mobile authentication failed: ", exception);
			OS.Alert(exception.Message, "Authentication Failure");
			OnAskForAuthentication();
		}
		finally
		{
			if (!_disposed && IsInstanceValid(LoadingScreen))
				LoadingScreen.HideScreen();
		}
	}

	public async void LaunchGame(long placeID)
	{
		LoadingScreen.ShowScreen();

		try
		{
			APIJoinPlaceResponse res = await BVAPI.RequestJoinGame(
				new() { PlaceID = placeID, IsBeta = true },
				(title, status) =>
					Callable
						.From(() =>
						{
							if (!_disposed && IsInstanceValid(LoadingScreen))
								LoadingScreen.UpdateStatus(title, status);
						})
						.CallDeferred()
			);
			if (_disposed || !IsInstanceValid(this))
				return;
			LoadingScreen.UpdateStatus(
				"Launching BrickVerse",
				"Connecting you to the game client…"
			);

			Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
			if (app is ClientEntry ce)
			{
				ClientEntry.ClientEntryData entryData = new()
				{
					Token = res.Token,
					ReturnToAppShell = true,
				};
				ce.Entry(entryData);
			}
		}
		catch (Exception ex)
		{
			BV.PrintErr("World join failed: ", ex);
			if (!_disposed && IsInstanceValid(this))
				OS.Alert(ex.Message, "World join failed");
		}

		if (!_disposed && IsInstanceValid(LoadingScreen))
			LoadingScreen.HideScreen();
	}

	public void SwitchTo(MobileViewEnum viewEnum, object? args = null)
	{
		if (CurrentViewNode != null && viewEnum == CurrentView)
		{
			if (args != null)
				CurrentViewNode?.ShowView(args);
			return;
		}

		if (
			CurrentViewNode != null
			&& !CurrentViewNode.TryNavigateAway(() => SwitchTo(viewEnum, args))
		)
			return;

		if (CurrentViewNode != null)
		{
			CurrentViewNode.HideView();
			AnimateViewOut(CurrentViewNode, viewEnum == MobileViewEnum.PlaceInfo ? -28f : 0f);
		}

		// Check if cached
		if (!_viewCache.TryGetValue(viewEnum, out MobileViewBase? page))
		{
			BV.Print("Loading ", viewEnum);
			string pathToLoad = viewEnum switch
			{
				MobileViewEnum.Home => "res://scenes/mobile/views/home.tscn",
				MobileViewEnum.Worlds => "res://scenes/mobile/views/worlds.tscn",
				MobileViewEnum.PlaceInfo => "res://scenes/mobile/views/place_info.tscn",
				MobileViewEnum.Search => "res://scenes/mobile/views/search.tscn",
				MobileViewEnum.AddFriend => "res://scenes/mobile/views/add_friend.tscn",
				MobileViewEnum.GuildDetail => "res://scenes/mobile/views/guild_detail.tscn",
				MobileViewEnum.ParentalControls =>
					"res://scenes/mobile/views/parental_controls.tscn",
				MobileViewEnum.RecordDetail => "res://scenes/mobile/views/record_detail.tscn",
				MobileViewEnum.MarketplaceItem => "res://scenes/mobile/views/marketplace_item.tscn",
				MobileViewEnum.Avatar => "res://scenes/mobile/views/avatar.tscn",
				MobileViewEnum.Guilds => "res://scenes/mobile/views/guilds.tscn",
				MobileViewEnum.Profile
				or MobileViewEnum.Settings
				or MobileViewEnum.Forum
				or MobileViewEnum.Events
				or MobileViewEnum.Notifications
				or MobileViewEnum.Friends
				or MobileViewEnum.FriendRequests
				or MobileViewEnum.Marketplace
				or MobileViewEnum.Transactions
				or MobileViewEnum.Upgrade => "res://scenes/mobile/views/collection.tscn",
				MobileViewEnum.Store or MobileViewEnum.Dev =>
					"res://scenes/mobile/views/collection.tscn",
				_ => throw new ArgumentOutOfRangeException(
					nameof(viewEnum),
					$"No scene defined for {viewEnum}"
				),
			};

			BV.Print("Loading ", viewEnum);

			PackedScene packed = ResourceLoader.Load<PackedScene>(
				pathToLoad,
				cacheMode: ResourceLoader.CacheMode.IgnoreDeep
			);
			page = packed.Instantiate<MobileViewBase>();
			_viewCache[viewEnum] = page;
			_mainView.AddChild(page);
			page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}

		CurrentViewNode = page;
		CurrentView = viewEnum;
		page.ShowView(args);
		page.Visible = true;
		AnimateViewIn(page, viewEnum == MobileViewEnum.PlaceInfo ? 28f : 12f);
		ViewPathSwitched?.Invoke(viewEnum);
	}

	public void RefreshCurrentView()
	{
		CurrentViewNode?.RefreshView();
		if (CurrentViewNode == null)
			return;
		foreach (Node node in CurrentViewNode.FindChildren("*", "", true, false))
		{
			if (node is WorldsGrid worlds)
				worlds.Refresh();
			else if (node is HomeShelf shelf)
				shelf.Refresh();
			else if (node is FeedRoot feed)
				feed.Refresh();
		}
	}

	private static void AnimateViewIn(Control view, float offset)
	{
		view.Modulate = new Color(1, 1, 1, 0);
		view.Position = new Vector2(offset, 0);
		Tween tween = view.CreateTween()
			.SetParallel()
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(view, "modulate:a", 1f, 0.2);
		tween.TweenProperty(view, "position:x", 0f, 0.24);
	}

	private static void AnimateViewOut(Control view, float offset)
	{
		Tween tween = view.CreateTween()
			.SetParallel()
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.In);
		tween.TweenProperty(view, "modulate:a", 0f, 0.14);
		tween.TweenProperty(view, "position:x", offset, 0.14);
		tween
			.Chain()
			.TweenCallback(
				Callable.From(() =>
				{
					if (IsInstanceValid(view))
					{
						view.Visible = false;
						view.Position = Vector2.Zero;
						view.Modulate = Colors.White;
					}
				})
			);
	}
}

public enum MobileViewEnum
{
	None,
	Home,
	Worlds,
	Avatar,
	Store,
	Dev,
	PlaceInfo,
	Search,
	AddFriend,
	Guilds,
	GuildDetail,
	Profile,
	Settings,
	Forum,
	Events,
	Notifications,
	Friends,
	FriendRequests,
	Marketplace,
	Transactions,
	Upgrade,
	RecordDetail,
	MarketplaceItem,
	ParentalControls,
}
