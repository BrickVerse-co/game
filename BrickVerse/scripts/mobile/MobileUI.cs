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

	private Deeplink _deepLink = new();
	private readonly Dictionary<MobileViewEnum, MobileViewBase> _viewCache = [];

	public override void _Ready()
	{
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();
		cmdargs.TryGetValue("token", out string? mobileToken);
		cmdargs.TryGetValue("code", out string? mobileCode);
		cmdargs.TryGetValue("state", out string? mobileState);

		AddChild(_deepLink, true);

		var initResult = _deepLink.Initialize();

		_deepLink.DeeplinkReceived += OnDeeplinkReceived;

		if (Globals.IsMobileBuild)
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

		BVMobileAuthAPI.SetupClient();
		if (mobileToken != null)
		{
			_ = CompleteAuthenticationAsync(() => BVMobileAuthAPI.LoginWithAuthToken(mobileToken));
		}

		else if (mobileCode != null && mobileState != null)
		{
			_ = CompleteAuthenticationAsync(() => BVMobileAuthAPI.LoginWithCodeAndState(mobileCode, mobileState));
		}

		_mainView = GetNode<Control>("Layout/MainView");
		if (Globals.IsMobileBuild)
		{
			DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		}

		if (Globals.IsInGDEditor)
		{
			DisplayServer.WindowSetSize((Vector2I)new Vector2(412, 700));
		}

		SwitchTo(MobileViewEnum.Home);
	}

	private void OnUserAuthenticated(APIV3AuthMeUser me)
	{
		HideStartupSplash();
		if (NewUserSplash != null && IsInstanceValid(NewUserSplash))
		{
			NewUserSplash.Visible = false;
		}
	}

	private void OnAskForAuthentication()
	{
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
				OS.Alert("The sign-in link is incomplete. Please start sign-in again.", "Authentication Failure");
				return;
			}

			await CompleteAuthenticationAsync(() => BVMobileAuthAPI.LoginWithCodeAndState(code, state));
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
		LoadingScreen.ShowScreen();
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
			LoadingScreen.HideScreen();
		}
	}

	public async void LaunchGame(long placeID)
	{
		LoadingScreen.ShowScreen();

		try
		{
			APIJoinPlaceResponse res = await BVAPI.RequestJoinGame(
				new() { PlaceID = placeID, IsBeta = true }
			);

			Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
			if (app is ClientEntry ce)
			{
				ClientEntry.ClientEntryData entryData = new() { Token = res.Token };
				ce.Entry(entryData);
			}
		}
		catch (Exception ex)
		{
			OS.Alert(ex.Message, "World join failed");
		}

		LoadingScreen.HideScreen();
	}

	public void SwitchTo(MobileViewEnum viewEnum, object? args = null)
	{
		if (viewEnum == CurrentView)
		{
			if (args != null) CurrentViewNode?.ShowView(args);
			return;
		}

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
				MobileViewEnum.RecordDetail => "res://scenes/mobile/views/record_detail.tscn",
				MobileViewEnum.MarketplaceItem => "res://scenes/mobile/views/marketplace_item.tscn",
				MobileViewEnum.Avatar => "res://scenes/mobile/views/avatar.tscn",
				MobileViewEnum.Guilds or MobileViewEnum.Profile or MobileViewEnum.Settings
					or MobileViewEnum.Forum or MobileViewEnum.Events or MobileViewEnum.Notifications
					or MobileViewEnum.FriendRequests or MobileViewEnum.Marketplace
					or MobileViewEnum.Transactions or MobileViewEnum.Upgrade
					=> "res://scenes/mobile/views/collection.tscn",
				MobileViewEnum.Store or MobileViewEnum.Dev => "res://scenes/mobile/views/collection.tscn",
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
		if (CurrentViewNode == null) return;
		foreach (Node node in CurrentViewNode.FindChildren("*", "", true, false))
		{
			if (node is WorldsGrid worlds) worlds.Refresh();
			else if (node is HomeShelf shelf) shelf.Refresh();
			else if (node is FeedRoot feed) feed.Refresh();
		}
	}

	private static void AnimateViewIn(Control view, float offset)
	{
		view.Modulate = new Color(1, 1, 1, 0);
		view.Position = new Vector2(offset, 0);
		Tween tween = view.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(view, "modulate:a", 1f, 0.2);
		tween.TweenProperty(view, "position:x", 0f, 0.24);
	}

	private static void AnimateViewOut(Control view, float offset)
	{
		Tween tween = view.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tween.TweenProperty(view, "modulate:a", 0f, 0.14);
		tween.TweenProperty(view, "position:x", offset, 0.14);
		tween.Chain().TweenCallback(Callable.From(() => { if (IsInstanceValid(view)) { view.Visible = false; view.Position = Vector2.Zero; view.Modulate = Colors.White; } }));
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
	Guilds,
	Profile,
	Settings,
	Forum,
	Events,
	Notifications,
	FriendRequests,
	Marketplace,
	Transactions,
	Upgrade,
	RecordDetail,
	MarketplaceItem,
}
