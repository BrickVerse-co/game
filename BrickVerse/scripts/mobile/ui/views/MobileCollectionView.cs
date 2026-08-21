// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BrickVerse.Mobile.Utils;
using BrickVerse.Shared;
using BrickVerse.Utils;
using BrickVerse.Shared.AssetLoaders;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileCollectionView : MobileViewBase
{
	private Label _title = null!;
	private LineEdit _search = null!;
	private Container _items = null!;
	private VBoxContainer _listItems = null!;
	private GridContainer _gridItems = null!;
	private Label _state = null!;
	private MobileViewEnum _view;
	private int _loadVersion;
	private PackedScene _listCardScene = null!;
	private PackedScene _gridCardScene = null!;
	private PackedScene _actionScene = null!;
	private PackedScene _infoScene = null!;
	private PackedScene _purchaseDialogScene = null!;
	private PackedScene _skeletonScene = null!;
	private PackedScene _settingToggleScene = null!;
	private PackedScene _profileSummaryScene = null!;
	private PackedScene _transactionCardScene = null!;
	private PackedScene _upgradeBannerScene = null!;
	private PackedScene _forumBannerScene = null!;
	private PackedScene _forumCardScene = null!;
	private PackedScene _notificationCardScene = null!;
	private PackedScene _adBannerScene = null!;
	private PackedScene _friendCardScene = null!;
	private PackedScene _friendSkeletonScene = null!;
	private VBoxContainer _promoHost = null!;
	private TabBar _category = null!;
	private Button _previous = null!;
	private Button _next = null!;
	private Label _pageLabel = null!;
	private int _page = 1;
	private readonly List<string?> _marketCursors = [null];
	private string? _nextCursor;
	private bool _hasNextPage;
	private bool _hasLoaded;
	private string? _forumCategoryId;
	private string _forumCategoryName = "";
	private string _profileUserId = "";

	public override void _Ready()
	{
		_title = GetNode<Label>("Layout/Header/Title");
		_search = GetNode<LineEdit>("Layout/Search");
		_listItems = GetNode<VBoxContainer>("Layout/Scroll/Content/Items");
		_gridItems = GetNode<GridContainer>("Layout/Scroll/Content/GridItems");
		_items = _listItems;
		_state = GetNode<Label>("Layout/State");
		_listCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/list_card.tscn");
		_gridCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/grid_card.tscn");
		_actionScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/action_button.tscn");
		_infoScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/info_label.tscn");
		_purchaseDialogScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/purchase_dialog.tscn");
		_skeletonScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/skeleton_card.tscn");
		_settingToggleScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/setting_toggle.tscn");
		_profileSummaryScene = GD.Load<PackedScene>("res://scenes/mobile/components/profile/profile_summary.tscn");
		_transactionCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/transactions/transaction_card.tscn");
		_upgradeBannerScene = GD.Load<PackedScene>("res://scenes/mobile/components/marketplace/upgrade_banner.tscn");
		_forumBannerScene = GD.Load<PackedScene>("res://scenes/mobile/components/forum/readonly_banner.tscn");
		_forumCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/forum/forum_card.tscn");
		_notificationCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/shared/notification_card.tscn");
		_adBannerScene = GD.Load<PackedScene>("res://scenes/mobile/components/home/ad_banner.tscn");
		_friendCardScene = GD.Load<PackedScene>("res://scenes/mobile/components/home/user_headshot_card.tscn");
		_friendSkeletonScene = GD.Load<PackedScene>("res://scenes/mobile/components/home/friend_skeleton.tscn");
		_promoHost = GetNode<VBoxContainer>("Layout/PromoHost");
		_category = GetNode<TabBar>("Layout/Category");
		_category.TabChanged += selected => { ResetPagination(); _ = LoadAsync(); };
		Resized += UpdateGridColumns;
		UpdateGridColumns();
		_previous = GetNode<Button>("Layout/Pagination/Previous");
		_next = GetNode<Button>("Layout/Pagination/Next");
		_pageLabel = GetNode<Label>("Layout/Pagination/Page");
		_search.TextSubmitted += query => { ResetPagination(); _ = LoadAsync(); };
		GetNode<Button>("Layout/Header/Refresh").Pressed += () => _ = LoadAsync();
		_previous.Pressed += () => { if (_page > 1) { _page--; _ = LoadAsync(); } };
		_next.Pressed += () =>
		{
			if (_view is MobileViewEnum.Marketplace or MobileViewEnum.Store && _marketCursors.Count == _page && !string.IsNullOrWhiteSpace(_nextCursor)) _marketCursors.Add(_nextCursor);
			_page++;
			_ = LoadAsync();
		};
	}

	public override void ShowView(object? args)
	{
		_view = args is MobileViewEnum requested ? requested : MobileUI.Singleton.CurrentView;
		string requestedProfileId = args is string userId && !string.IsNullOrWhiteSpace(userId) ? userId : BVMobileAuthAPI.CurrentUserInfo.Id;
		bool profileChanged = _view == MobileViewEnum.Profile && requestedProfileId != _profileUserId;
		if (_view == MobileViewEnum.Profile) _profileUserId = requestedProfileId;
		bool forumReset = _view == MobileViewEnum.Forum && args is MobileViewEnum && _forumCategoryId != null;
		if (_view == MobileViewEnum.Forum && args is MobileViewEnum) { _forumCategoryId = null; _forumCategoryName = ""; }
		bool grid = _view is MobileViewEnum.Friends or MobileViewEnum.Guilds or MobileViewEnum.Marketplace or MobileViewEnum.Store;
		_listItems.Visible = !grid;
		_gridItems.Visible = grid;
		_items = grid ? _gridItems : _listItems;
		UpdateGridColumns();
		ResetPagination();
		_title.Text = _view == MobileViewEnum.Forum && _forumCategoryId != null ? _forumCategoryName : TitleFor(_view);
		_state.Visible = _view != MobileViewEnum.Profile;
		_search.Visible = _view is MobileViewEnum.Friends or MobileViewEnum.Guilds or MobileViewEnum.Forum or MobileViewEnum.Events or MobileViewEnum.Marketplace or MobileViewEnum.Store;
		_search.PlaceholderText = $"Search {TitleFor(_view).ToLowerInvariant()}";
		foreach (Node child in _promoHost.GetChildren()) child.QueueFree();
		if (_view is MobileViewEnum.Marketplace or MobileViewEnum.Store) _promoHost.AddChild(_upgradeBannerScene.Instantiate());
		else if (_view == MobileViewEnum.Forum) _promoHost.AddChild(_forumBannerScene.Instantiate());
		else if (_view is MobileViewEnum.Guilds or MobileViewEnum.Events or MobileViewEnum.Notifications) _promoHost.AddChild(_adBannerScene.Instantiate());
		_category.Visible = _view is MobileViewEnum.Marketplace or MobileViewEnum.Store or MobileViewEnum.Guilds;
		if (_view == MobileViewEnum.Guilds)
		{
			_category.TabCount = 2;
			_category.SetTabTitle(0, "Discover");
			_category.SetTabTitle(1, "My Guilds");
		}
		else if (_view is MobileViewEnum.Marketplace or MobileViewEnum.Store)
		{
			_category.TabCount = 4;
			_category.SetTabTitle(0, "Featured"); _category.SetTabTitle(1, "Top Selling");
			_category.SetTabTitle(2, "Trending"); _category.SetTabTitle(3, "New");
		}
		if (!_hasLoaded || profileChanged || forumReset) { _hasLoaded = true; _ = LoadAsync(); }
	}

	public override void RefreshView() { ResetPagination(); _ = LoadAsync(); }

	private async System.Threading.Tasks.Task LoadAsync()
	{
		int version = ++_loadVersion;
		_state.Text = "Loading…";
		ClearItems();
		for (int index = 0; index < 6; index++) _items.AddChild((_view == MobileViewEnum.Friends ? _friendSkeletonScene : _skeletonScene).Instantiate());
		try
		{
			if (_view is MobileViewEnum.Settings or MobileViewEnum.Upgrade or MobileViewEnum.Dev)
			{
				ClearItems();
				BuildAccountActions();
				_state.Text = "";
				return;
			}
			if (_view == MobileViewEnum.Notifications)
			{
				AddAsyncAction("Mark all as read", async () =>
				{
					using (await BVAPI.SendJson(System.Net.Http.HttpMethod.Post, "/v3/social/notifications/read-all")) { }
					await LoadAsync();
				});
			}
			else if (_view == MobileViewEnum.FriendRequests)
			{
			}
			else if (_view == MobileViewEnum.Guilds)
			{
			}
			else if (_view == MobileViewEnum.Forum)
			{
			}
			else if (_view is MobileViewEnum.Marketplace or MobileViewEnum.Store)
			{
			}
			else if (_view == MobileViewEnum.Profile)
			{
			}
			using JsonDocument document = await BVAPI.GetJson(PathFor(_view, _search.Text));
			if (version != _loadVersion) return;
			ClearItems();
			RestoreLeadingActions();
			List<JsonElement> records = FindRecords(document.RootElement).Take(50).ToList();
			_nextCursor = document.RootElement.TryGetProperty("nextCursor", out JsonElement cursor) && cursor.ValueKind == JsonValueKind.String ? cursor.GetString() : null;
			_hasNextPage = !string.IsNullOrWhiteSpace(_nextCursor) || ReadHasNextPage(document.RootElement);
			foreach (JsonElement record in records) AddRecord(record);
			_state.Text = _view == MobileViewEnum.Profile ? "" : records.Count == 0 ? "Nothing to show yet." : $"{records.Count} shown";
			UpdatePagination();
		}
		catch (Exception exception)
		{
			ClearItems();
			_state.Text = "Could not load this view. Tap refresh to try again.";
			BV.PrintErr($"Mobile {_view} failed: {exception}");
		}
	}

	private void RestoreLeadingActions()
	{
		if (_view == MobileViewEnum.Notifications)
			AddAsyncAction("Mark all as read", async () => { using (await BVAPI.SendJson(System.Net.Http.HttpMethod.Post, "/v3/social/notifications/read-all")) { } await LoadAsync(); });
		else if (_view == MobileViewEnum.FriendRequests) AddInfo("Review incoming requests below.");
		else if (_view == MobileViewEnum.Guilds) { }
		else if (_view == MobileViewEnum.Forum && _forumCategoryId != null)
			AddAction("‹ All forum categories", () => { _forumCategoryId = null; _forumCategoryName = ""; _title.Text = "Forum"; _ = LoadAsync(); });
		else if (_view is MobileViewEnum.Marketplace or MobileViewEnum.Store) { }
		else if (_view == MobileViewEnum.Profile) { }
	}

	private void BuildAccountActions()
	{
		if (_view == MobileViewEnum.Dev)
		{
			AddDestination("Guilds", MobileViewEnum.Guilds);
			AddDestination("My profile", MobileViewEnum.Profile);
			AddDestination("Forum", MobileViewEnum.Forum);
			AddDestination("Events", MobileViewEnum.Events);
			AddDestination("Notifications", MobileViewEnum.Notifications);
			AddDestination("Friend requests", MobileViewEnum.FriendRequests);
			AddDestination("Parental controls", MobileViewEnum.ParentalControls);
			AddDestination("Transactions", MobileViewEnum.Transactions);
			AddDestination("Upgrade", MobileViewEnum.Upgrade);
			AddDestination("Settings", MobileViewEnum.Settings);
			return;
		}
		if (_view == MobileViewEnum.Settings)
		{
			AddInfo("App experience");
			AddSettingToggle("Background loading", "background_loading", true);
			AddSettingToggle("Push notifications", "push_notifications", true);
			AddSettingToggle("Reduce motion", "reduce_motion", false);
			AddSettingToggle("Autoplay 3D previews", "autoplay_3d_previews", true);
			AddSettingToggle("Use cellular data for images", "cellular_images", true);
			AddInfo("Accessibility");
			AddSettingToggle("Larger interface text", "large_interface_text", false);
			AddSettingToggle("High contrast controls", "high_contrast", false);
			AddSettingToggle("Haptic feedback", "haptics", true);
			AddInfo("Privacy & communication");
			AddSettingToggle("Show online status", "show_online_status", true);
			AddSettingToggle("Allow friend requests", "allow_friend_requests", true);
			AddSettingToggle("Direct-message notifications", "dm_notifications", true);
			AddInfo("Account & security");
			AddAction("Manage account details", () => Open("/my/settings?section=account"));
			AddAction("Privacy and parental controls", () => Open("/my/settings?section=privacy"));
			AddAction("Security and active sessions", () => Open("/my/settings?section=security"));
			AddAction("Open full settings on website", () => Open("/my/settings"));
			AddDangerAction("Sign out", ConfirmSignOut);
			return;
		}
		AddAction("Cubes", () => MobileBillingService.Singleton?.OpenProducts(MobileProductKind.Cubes));
		AddAction("BrickVerse Membership", () => MobileBillingService.Singleton?.OpenProducts(MobileProductKind.Membership));
	}

	private void AddRecord(JsonElement record)
	{
		if (_view == MobileViewEnum.Friends)
		{
			JsonElement user = record.TryGetProperty("user", out JsonElement nestedUser) ? nestedUser : record;
			string userId = FirstString(user, "id") ?? "";
			if (string.IsNullOrWhiteSpace(userId)) return;
			string username = FirstString(user, "username", "name") ?? "Unknown user";
			string presence = FirstString(record, "presence", "presenceState", "state") ?? FirstString(user, "presence", "presenceState", "state") ?? "OFFLINE";
			if (presence.Equals("ACCEPTED", StringComparison.OrdinalIgnoreCase)) presence = "OFFLINE";
			UserHeadshotCard friend = _friendCardScene.Instantiate<UserHeadshotCard>();
			friend.UserID = userId;
			friend.InitialUsername = username;
			friend.InitialPresence = presence;
			friend.IsVerified = user.TryGetProperty("isVerified", out JsonElement verified) && verified.ValueKind == JsonValueKind.True;
			friend.IsAdmin = user.TryGetProperty("isStaff", out JsonElement staff) && staff.ValueKind == JsonValueKind.True;
			_gridItems.AddChild(friend);
			MobileMotion.Enter(friend, _gridItems.GetChildCount() - 1);
			return;
		}
		if (_view == MobileViewEnum.Notifications)
		{
			AddNotificationRecord(record);
			return;
		}
		if (_view is MobileViewEnum.Marketplace or MobileViewEnum.Store)
		{
			AddMarketplaceRecord(record);
			return;
		}
		if (_view == MobileViewEnum.Transactions)
		{
			AddTransactionRecord(record);
			return;
		}
		if (_view is MobileViewEnum.Guilds or MobileViewEnum.Events)
		{
			AddDiscoveryRecord(record);
			return;
		}
		if (_view == MobileViewEnum.Profile)
		{
			AddProfileRecord(record);
			return;
		}
		if (_view == MobileViewEnum.Forum)
		{
			AddForumRecord(record);
			return;
		}
		string title = FirstString(record, "name", "title", "username", "type", "action") ?? "BrickVerse item";
		string detail = FirstString(record, "description", "content", "message", "status", "createdAt") ?? "";
		MobileListCard card = CreateListCard(title, "", Trim(detail, 120));
		string? id = FirstString(record, "id", "worldId", "userId", "guildId");
		if (_view == MobileViewEnum.Notifications && id != null)
		{
			bool isRead = record.TryGetProperty("isRead", out JsonElement readNode) && readNode.GetBoolean();
			card.Text = (isRead ? "" : "● ") + card.Text;
			card.Pressed += async () =>
			{
				if (!isRead) using (await BVAPI.SendJson(System.Net.Http.HttpMethod.Post, $"/v3/social/notifications/{Uri.EscapeDataString(id)}/read")) { }
				await LoadAsync();
			};
		}
		else if (_view == MobileViewEnum.FriendRequests)
		{
			card.Pressed += () => Open("/my/friends?tab=requests");
		}
		else if (_view == MobileViewEnum.Forum && _forumCategoryId == null)
		{
			int threads = record.TryGetProperty("_count", out JsonElement counts) ? ReadNumber(counts, "threads") : 0;
			int replies = record.TryGetProperty("_count", out counts) ? ReadNumber(counts, "replies") : 0;
			string latest = record.TryGetProperty("latestThread", out JsonElement latestThread) && latestThread.ValueKind == JsonValueKind.Object
				? "Latest: " + (FirstString(latestThread, "title") ?? "Thread") : "No threads yet";
			card.Configure(title, $"{threads:N0} threads • {replies:N0} replies", latest);
			card.Pressed += () => { _forumCategoryId = id; _forumCategoryName = title; _title.Text = title; _ = LoadAsync(); };
		}
		else if (_view == MobileViewEnum.Forum)
		{
			string author = NestedName(record, "user", "username") ?? "Unknown author";
			int replies = ReadNumber(record, "totalReplies");
			int views = ReadNumber(record, "views");
			string category = record.TryGetProperty("category", out JsonElement categoryNode) ? FirstString(categoryNode, "name") ?? _forumCategoryName : _forumCategoryName;
			card.Configure(title, $"{category} • By {author}", $"{replies:N0} replies • {views:N0} views");
			card.Pressed += () => MobileUI.Singleton.SwitchTo(MobileViewEnum.RecordDetail, new MobileRecordDetailArgs(title, $"By {author}", detail, "", _view, id ?? ""));
		}
		else card.Pressed += () => OpenRecord(id);
	}

	private void AddForumRecord(JsonElement record)
	{
		string id = FirstString(record, "id") ?? "";
		string title = FirstString(record, "name", "title") ?? "Forum";
		MobileForumCard card = _forumCardScene.Instantiate<MobileForumCard>();
		_items.AddChild(card);
		if (_forumCategoryId == null)
		{
			int threads = record.TryGetProperty("_count", out JsonElement counts) ? ReadNumber(counts, "threads") : 0;
			int replies = record.TryGetProperty("_count", out counts) ? ReadNumber(counts, "replies") : 0;
			string latest = record.TryGetProperty("latestThread", out JsonElement latestThread) && latestThread.ValueKind == JsonValueKind.Object
				? "Latest: " + (FirstString(latestThread, "title") ?? "Thread") : "No threads yet";
			card.Configure(title, $"{threads:N0} threads • {replies:N0} replies", latest, FirstString(record, "icon") ?? "");
			card.Pressed += () => { _forumCategoryId = id; _forumCategoryName = title; _title.Text = title; _ = LoadAsync(); };
		}
		else
		{
			string author = NestedName(record, "user", "username") ?? "Unknown author";
			int replies = ReadNumber(record, "totalReplies");
			int views = ReadNumber(record, "views");
			string category = record.TryGetProperty("category", out JsonElement categoryNode) ? FirstString(categoryNode, "name") ?? _forumCategoryName : _forumCategoryName;
			card.Configure(title, $"{category} • By {author}", $"{replies:N0} replies • {views:N0} views", "chat");
			card.Pressed += () => MobileUI.Singleton.SwitchTo(MobileViewEnum.RecordDetail, new MobileRecordDetailArgs(title, $"By {author}", FirstString(record, "content") ?? "", "", _view, id));
		}
		MobileMotion.Enter(card, _items.GetChildCount() - 1);
	}

	private void AddNotificationRecord(JsonElement record)
	{
		string id = FirstString(record, "id") ?? "";
		string title = FirstString(record, "title", "type") ?? "Notification";
		string message = FirstString(record, "message", "content") ?? "You have a new update.";
		bool isRead = record.TryGetProperty("isRead", out JsonElement readNode) && readNode.ValueKind == JsonValueKind.True;
		DateTime.TryParse(FirstString(record, "createdAt") ?? "", null,
			System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
			out DateTime createdAt);
		MobileNotificationCard card = _notificationCardScene.Instantiate<MobileNotificationCard>();
		_items.AddChild(card);
		card.Configure(title, message, createdAt == default ? DateTime.UtcNow : createdAt, isRead);
		card.Pressed += async () =>
		{
			if (isRead || string.IsNullOrWhiteSpace(id)) return;
			using (await BVAPI.SendJson(System.Net.Http.HttpMethod.Post, $"/v3/social/notifications/{Uri.EscapeDataString(id)}/read")) { }
			isRead = true;
			card.MarkRead();
		};
		MobileMotion.Enter(card, _items.GetChildCount() - 1);
	}

	private void AddDiscoveryRecord(JsonElement record)
	{
		string id = FirstString(record, "id") ?? "";
		string name = FirstString(record, "name", "title") ?? TitleFor(_view);
		string description = FirstString(record, "description") ?? "";
		string meta = _view == MobileViewEnum.Guilds
			? $"{ReadNumber(record, "memberCount"):N0} members"
			: EventState(record);
		string? imageId = FirstString(record, _view == MobileViewEnum.Guilds ? "logoId" : "iconUrl");
		string imageUrl = "";
		if (!string.IsNullOrWhiteSpace(imageId))
			imageUrl = imageId.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? imageId : Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + imageId);
		else if (_view == MobileViewEnum.Guilds) imageUrl = "res://assets/textures/ui-icons/users-group.svg";
		MobileListCard card = CreateListCard(name, meta, Trim(description, 90), imageUrl);
		card.SetVerified(record.TryGetProperty("isVerified", out JsonElement verified) && verified.ValueKind == JsonValueKind.True);
		card.Pressed += () => MobileUI.Singleton.SwitchTo(_view == MobileViewEnum.Guilds ? MobileViewEnum.GuildDetail : MobileViewEnum.RecordDetail, new MobileRecordDetailArgs(name, meta, description, imageUrl, _view, id));
	}

	private void AddProfileRecord(JsonElement record)
	{
		BuildProfileSummary(record);
		return;
#if false
		string username = FirstString(record, "username", "name") ?? "Profile";
		string description = FirstString(record, "description") ?? "No description provided.";
		string status = FirstString(record, "status") ?? "";
		AddInfo(username);
		AddInfo(status.Replace('_', ' '));
		AddInfo(description);
		if (record.TryGetProperty("statistics", out JsonElement stats))
			AddInfo($"{ReadNumber(stats, "visits"):N0} visits   •   {ReadNumber(stats, "profileViews"):N0} profile views   •   {ReadNumber(stats, "forumPosts"):N0} forum posts");
#endif
	}

	private static int ReadNumber(JsonElement item, string name) => item.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : 0;
	private static string EventState(JsonElement record)
	{
		DateTime now = DateTime.UtcNow;
		DateTime.TryParse(FirstString(record, "startTime") ?? "", out DateTime start);
		DateTime.TryParse(FirstString(record, "endTime") ?? "", out DateTime end);
		if (end != default && end < now) return "Ended";
		if (start != default && start > now) return "Upcoming • " + start.ToLocalTime().ToString("MMM d");
		return "Live now";
	}

	private void AddTransactionRecord(JsonElement record)
	{
		BuildTransactionCard(record);
		return;
#if false
		int amount = 0;
		foreach (string field in new[] { "creditAmount", "givenAmount", "amount" })
			if (record.TryGetProperty(field, out JsonElement amountNode) && amountNode.TryGetInt32(out amount)) break;
		bool received = record.TryGetProperty("wasReceived", out JsonElement receivedNode) && receivedNode.ValueKind == JsonValueKind.True;
		string status = FirstString(record, "status", "type") ?? "Transaction";
		string date = FirstString(record, "createdAt", "receivedAt") ?? "";
		string from = NestedName(record, "fromUser", "username") ?? NestedName(record, "fromGuild", "name") ?? "System";
		string to = NestedName(record, "toUser", "username") ?? NestedName(record, "toGuild", "name") ?? "System";
		MobileListCard row = CreateListCard($"{(received ? "+" : "−")} ◈ {amount:N0}", status.Replace('_', ' '), $"{from} → {to}{(string.IsNullOrWhiteSpace(date) ? "" : "   " + date)}");
		row.Modulate = received ? new Color(0.62f, 0.95f, 0.72f) : Colors.White;
#endif
	}

	private static string? NestedName(JsonElement record, string objectName, string fieldName)
	{
		return record.TryGetProperty(objectName, out JsonElement nested)
			&& nested.ValueKind == JsonValueKind.Object
			&& nested.TryGetProperty(fieldName, out JsonElement value)
			? value.GetString() : null;
	}

	private void BuildProfileSummary(JsonElement record)
	{
		JsonElement stats = record.TryGetProperty("statistics", out JsonElement statistics) ? statistics : default;
		_title.Text = FirstString(record, "username", "name") ?? "Profile";
		MobileProfileSummary summary = _profileSummaryScene.Instantiate<MobileProfileSummary>();
		_items.AddChild(summary);
		summary.Configure(
		FirstString(record, "id") ?? BVMobileAuthAPI.CurrentUserInfo.Id,
		FirstString(record, "description") ?? "No description provided.",
		ReadNumber(stats, "visits"), ReadNumber(stats, "profileViews"), ReadNumber(stats, "forumPosts"), ReadNumber(stats, "friends"), ReadNumber(stats, "followers"), ReadNumber(stats, "following"),
		FirstString(record, "createdAt") ?? "", FirstString(record, "lastSeenAt") ?? "", FirstString(record, "bodyshotUrl") ?? "");
		BuildProfileActions(record);
	}

	private void BuildProfileActions(JsonElement record)
	{
		string userId = FirstString(record, "id") ?? "";
		bool ownProfile = userId == BVMobileAuthAPI.CurrentUserInfo.Id;
		AddAsyncAction("View equipped avatar items", () => ShowEquippedItems(userId));
		if (!ownProfile)
		{
			AddAction("Add friend / Manage friendship", () => Open($"/users/{Uri.EscapeDataString(userId)}"));
			if (record.TryGetProperty("state", out JsonElement state)
				&& state.TryGetProperty("currentActivity", out JsonElement activity))
			{
				string? worldId = FirstString(activity, "worldId", "universeId");
				if (long.TryParse(worldId, out long placeId)) AddAction("Join game", () => MobileUI.Singleton.LaunchGame(placeId));
			}
		}
		if (record.TryGetProperty("socialLinks", out JsonElement socialLinks) && socialLinks.ValueKind == JsonValueKind.Array && socialLinks.GetArrayLength() > 0)
		{
			AddInfo("Social links");
			ScrollContainer socialScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 48) };
			HBoxContainer chips = new(); chips.AddThemeConstantOverride("separation", 8); socialScroll.AddChild(chips); _items.AddChild(socialScroll);
			foreach (JsonElement social in socialLinks.EnumerateArray())
			{
				string provider = FirstString(social, "provider") ?? "Social";
				string label = FirstString(social, "displayName", "username") ?? provider;
				string url = FirstString(social, "profileUrl") ?? "";
				if (!string.IsNullOrWhiteSpace(url))
				{
					Button chip = new() { Text = label, Icon = PlatformIcon(provider), CustomMinimumSize = new Vector2(0, 42) }; chip.AddThemeConstantOverride("icon_max_width", 19);
					StyleBoxFlat style = new() { BgColor = Color.FromHtml("1A1E24"), BorderColor = Color.FromHtml("272C34"), BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1, ContentMarginLeft = 14, ContentMarginRight = 14, CornerRadiusTopLeft = 21, CornerRadiusTopRight = 21, CornerRadiusBottomLeft = 21, CornerRadiusBottomRight = 21 };
					chip.AddThemeStyleboxOverride("normal", style); chip.AddThemeStyleboxOverride("hover", style); chip.Pressed += () => ConfirmExternalLink(url, provider); chips.AddChild(chip); MobileMotion.Bind(chip);
				}
			}
		}
		if (record.TryGetProperty("achievements", out JsonElement achievements) && achievements.ValueKind == JsonValueKind.Array)
		{
			AddInfo("Achievements");
			GridContainer grid = new() { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 10); _items.AddChild(grid);
			foreach (JsonElement achievement in achievements.EnumerateArray().Take(8))
			{
				Button badge = new() { TooltipText = FirstString(achievement, "description") ?? "Earned achievement", CustomMinimumSize = new Vector2(112, 132), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
				StyleBoxFlat badgeStyle = new() { BgColor = Color.FromHtml("14171C"), BorderColor = Color.FromHtml("33404D"), BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1, CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12 }; badge.AddThemeStyleboxOverride("normal", badgeStyle); badge.AddThemeStyleboxOverride("hover", badgeStyle);
				VBoxContainer content = new() { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center }; content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); content.OffsetLeft = 10; content.OffsetTop = 10; content.OffsetRight = -10; content.OffsetBottom = -10; content.AddThemeConstantOverride("separation", 7); TextureRect icon = new() { CustomMinimumSize = new Vector2(76, 76), MouseFilter = Control.MouseFilterEnum.Ignore, Texture = GD.Load<Texture2D>("res://assets/textures/client/placeholder/achievement.png"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered }; content.AddChild(icon); Label name = new() { Text = FirstString(achievement, "name") ?? "Achievement", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Ignore }; name.AddThemeFontSizeOverride("font_size", 12); content.AddChild(name); badge.AddChild(content);
				grid.AddChild(badge); MobileMotion.Bind(badge); string iconUrl = FirstString(achievement, "icon", "iconUrl") ?? ""; if (iconUrl.StartsWith('/')) iconUrl = Globals.MainEndpoint.PathJoin(iconUrl); LoadProfileImage(icon, iconUrl);
			}
		}
	}

	private static Texture2D? PlatformIcon(string provider) { string key = provider.ToLowerInvariant(); if (key is not ("discord" or "github" or "steam" or "x")) return null; return GD.Load<Texture2D>($"res://assets/textures/client/ui/brands/{key}.svg"); }
	private static void LoadProfileImage(TextureRect target, string url) { if (string.IsNullOrWhiteSpace(url)) return; WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, resource => { if (IsInstanceValid(target)) target.Texture = (Texture2D)resource; }); }
	private void ConfirmExternalLink(string url, string provider)
	{
		ConfirmationDialog warning = new() { Title = "Leave BrickVerse?", DialogText = $"You’re opening {provider} outside BrickVerse. External sites have their own privacy and safety policies.", OkButtonText = "Continue", CancelButtonText = "Stay here", Exclusive = true };
		warning.Confirmed += () => { OS.ShellOpen(url); warning.QueueFree(); }; warning.Canceled += warning.QueueFree; AddChild(warning); warning.PopupCentered();
	}

	private async System.Threading.Tasks.Task ShowEquippedItems(string userId)
	{
		AcceptDialog dialog = new() { Title = "Equipped avatar items", Size = new Vector2I(640, 700), Exclusive = true };
		ScrollContainer scroll = new() { OffsetLeft = 18, OffsetTop = 18, OffsetRight = 622, OffsetBottom = 620 }; GridContainer list = new() { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; list.AddThemeConstantOverride("h_separation", 10); list.AddThemeConstantOverride("v_separation", 10); scroll.AddChild(list); dialog.AddChild(scroll); AddChild(dialog);
		try
		{
			BrickVerse.Schemas.API.APIAvatarResponse avatar = await BVAPI.GetUserAvatarFromID(userId);
			if (avatar.Assets.Length == 0) list.AddChild(new Label { Text = "No avatar items equipped.", HorizontalAlignment = HorizontalAlignment.Center });
			foreach (BrickVerse.Schemas.API.APIAvatarAsset item in avatar.Assets)
			{
				MobileListCard card = _gridCardScene.Instantiate<MobileListCard>(); card.CustomMinimumSize = new Vector2(170, 220); card.Configure(string.IsNullOrWhiteSpace(item.Name) ? "Avatar item" : item.Name, item.Type, "Equipped", string.IsNullOrWhiteSpace(item.Thumbnail) ? "marketplace-item://" + item.ID : item.Thumbnail); card.Pressed += () => { dialog.QueueFree(); MobileUI.Singleton.SwitchTo(MobileViewEnum.MarketplaceItem, item.ID); }; list.AddChild(card);
			}
			dialog.PopupCenteredRatio(0.88f);
		}
		catch (Exception exception) { dialog.QueueFree(); OS.Alert(exception.Message, "Could not load equipped items"); }
	}

	private void BuildTransactionCard(JsonElement record)
	{
		int amount = 0;
		foreach (string field in new[] { "creditAmount", "givenAmount", "amount" })
			if (record.TryGetProperty(field, out JsonElement node) && node.TryGetInt32(out amount)) break;
		bool received = record.TryGetProperty("wasReceived", out JsonElement receivedNode) && receivedNode.ValueKind == JsonValueKind.True;
		MobileTransactionCard card = _transactionCardScene.Instantiate<MobileTransactionCard>();
		_items.AddChild(card);
		card.Configure(amount, received, FirstString(record, "status", "type") ?? "Transaction",
			NestedName(record, "fromUser", "username") ?? NestedName(record, "fromGuild", "name") ?? "System",
			NestedName(record, "toUser", "username") ?? NestedName(record, "toGuild", "name") ?? "System",
			FirstString(record, "createdAt", "receivedAt") ?? "");
	}

	private void AddMarketplaceRecord(JsonElement record)
	{
		string id = FirstString(record, "id") ?? "";
		string name = FirstString(record, "name") ?? "Marketplace item";
		int price = record.TryGetProperty("price", out JsonElement priceNode) && priceNode.TryGetInt32(out int value) ? value : 0;
		string type = FirstString(record, "type") ?? "Accessory";
		string creator = FirstString(record, "creatorName") ?? "BrickVerse creator";
		MobileListCard marketCard = CreateListCard(name, price == 0 ? "Free" : $"{price:N0} Cubes", $"{type.Replace('_', ' ')} • By {creator}", "marketplace-item://" + id);
		marketCard.SetVerified(record.TryGetProperty("creatorVerified", out JsonElement creatorVerified) && creatorVerified.ValueKind == JsonValueKind.True);
		marketCard.Pressed += () => MobileUI.Singleton.SwitchTo(MobileViewEnum.MarketplaceItem, id);
		return;
#if false
		string imageUrl = "";
		if (record.TryGetProperty("thumbnailId", out JsonElement thumbnail) && thumbnail.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(thumbnail.GetString()))
			imageUrl = Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + thumbnail.GetString());
		MobileListCard card = CreateListCard(name, price == 0 ? "Free" : $"◈ {price:N0}", "", imageUrl);
		card.Pressed += () => ConfirmMarketplacePurchase(id, name, price);
#endif
	}

	private void ConfirmMarketplacePurchase(string id, string name, int price)
	{
		ConfirmationDialog dialog = _purchaseDialogScene.Instantiate<ConfirmationDialog>();
		dialog.DialogText = $"Purchase {name} for {(price == 0 ? "free" : $"{price:N0} Cubes")}?";
		dialog.OkButtonText = price == 0 ? "Get" : "Buy";
		dialog.Confirmed += async () =>
		{
			try
			{
				using JsonDocument response = await BVAPI.SendJson(System.Net.Http.HttpMethod.Post, $"/v3/marketplace/{Uri.EscapeDataString(id)}/buy");
				string message = response.RootElement.TryGetProperty("message", out JsonElement messageNode) ? messageNode.GetString() ?? "Purchase complete." : "Purchase complete.";
				OS.Alert(message, "Marketplace");
			}
			catch (Exception exception) { OS.Alert(exception.Message, "Purchase failed"); }
			finally { dialog.QueueFree(); }
		};
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private void OpenRecord(string? id)
	{
		if (string.IsNullOrWhiteSpace(id)) return;
		if (_view == MobileViewEnum.Guilds) Open("/guilds/" + id);
		else if (_view == MobileViewEnum.Forum) Open("/forum/thread/" + id);
		else if (_view == MobileViewEnum.Events) Open("/events/" + id);
		else if (_view is MobileViewEnum.Marketplace or MobileViewEnum.Store) Open("/market/" + id);
	}

	private static void Open(string path) => OS.ShellOpen(Globals.MainEndpoint.PathJoin(path));
	private void AddLink(string label, string path) => AddAction(label, () => Open(path));
	private void AddAsyncAction(string label, Func<System.Threading.Tasks.Task> action)
	{
		Button button = _actionScene.Instantiate<Button>();
		button.Text = label;
		button.Pressed += async () =>
		{
			button.Disabled = true;
			try { await action(); }
			catch (Exception exception) { _state.Text = exception.Message; }
			finally { if (IsInstanceValid(button)) button.Disabled = false; }
		};
		_items.AddChild(button);
	}
	private void AddDestination(string label, MobileViewEnum view) => AddAction(label, () => MobileUI.Singleton.SwitchTo(view, view));
	private void AddAction(string label, Action action) { Button button = _actionScene.Instantiate<Button>(); button.Text = label; button.Pressed += action; _items.AddChild(button); }
	private void AddDangerAction(string label, Action action)
	{
		Button button = GD.Load<PackedScene>("res://scenes/mobile/components/shared/danger_button.tscn").Instantiate<Button>();
		button.Text = label;
		button.Pressed += action;
		MobileMotion.Bind(button);
		_items.AddChild(button);
	}
	private void ConfirmSignOut()
	{
		ConfirmationDialog dialog = _purchaseDialogScene.Instantiate<ConfirmationDialog>();
		dialog.Title = "Sign out?";
		dialog.DialogText = "You will need to authenticate again to use BrickVerse Mobile.";
		dialog.OkButtonText = "Sign out";
		dialog.Confirmed += () => { BVMobileAuthAPI.Logout(); dialog.QueueFree(); };
		dialog.Canceled += dialog.QueueFree;
		AddChild(dialog);
		dialog.PopupCentered();
	}
	private void AddInfo(string text) { Label label = _infoScene.Instantiate<Label>(); label.Text = text; _items.AddChild(label); }
	private void AddSettingToggle(string label, string key, bool fallback)
	{
		CheckButton toggle = _settingToggleScene.Instantiate<CheckButton>();
		toggle.Text = label;
		ConfigFile config = new();
		config.Load("user://mobile_settings.cfg");
		toggle.ButtonPressed = (bool)config.GetValue("app", key, fallback);
		toggle.Toggled += enabled => { config.SetValue("app", key, enabled); config.Save("user://mobile_settings.cfg"); };
		_items.AddChild(toggle);
	}
	private MobileListCard CreateListCard(string title, string meta = "", string detail = "", string imageUrl = "")
	{
		MobileListCard card = (_items == _gridItems ? _gridCardScene : _listCardScene).Instantiate<MobileListCard>();
		_items.AddChild(card);
		card.Configure(title, meta, detail, imageUrl);
		MobileMotion.Enter(card, _items.GetChildCount() - 1);
		return card;
	}
	private void UpdateGridColumns()
	{
		float available = GetViewportRect().Size.X - 32f;
		float targetWidth = _view == MobileViewEnum.Friends ? 118f : 190f;
		_gridItems.Columns = Mathf.Clamp(Mathf.FloorToInt((available + 10f) / targetWidth), 2, _view == MobileViewEnum.Friends ? 6 : 4);
	}
	private void ClearItems() { ClearContainer(_listItems); ClearContainer(_gridItems); }
	private static void ClearContainer(Node container)
	{
		foreach (Node child in container.GetChildren()) { container.RemoveChild(child); child.QueueFree(); }
	}
	private static string TitleFor(MobileViewEnum view) => view switch { MobileViewEnum.Friends => "Friends", MobileViewEnum.FriendRequests => "Friend requests", MobileViewEnum.Store => "Marketplace", MobileViewEnum.Dev => "More", _ => view.ToString() };

	private string PathFor(MobileViewEnum view, string search)
	{
		string q = string.IsNullOrWhiteSpace(search) ? "" : "&search=" + Uri.EscapeDataString(search.Trim());
		return view switch
		{
			MobileViewEnum.Friends => "/v3/social/friends",
			MobileViewEnum.Guilds => _category.CurrentTab == 1
				? "/v3/social/guilds/user/" + Uri.EscapeDataString(BVMobileAuthAPI.CurrentUserInfo.Id)
				: $"/v3/social/guilds?limit=20&page={_page}" + q,
			MobileViewEnum.Profile => "/v3/profile/" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(_profileUserId) ? BVMobileAuthAPI.CurrentUserInfo.Id : _profileUserId) + "/id",
			MobileViewEnum.Forum => _forumCategoryId == null ? "/v3/forum/categories" : "/v3/forum/threads?limit=30&categoryId=" + Uri.EscapeDataString(_forumCategoryId) + q,
			MobileViewEnum.Events => "/v3/social/events?limit=30" + q,
			MobileViewEnum.Notifications => "/v3/social/notifications?limit=50",
			MobileViewEnum.FriendRequests => "/v3/social/friends/requests?limit=50",
			MobileViewEnum.Marketplace or MobileViewEnum.Store => "/v3/marketplace/discover?limit=20&sortBy=" + (_category.CurrentTab switch { 0 => "featured", 1 => "topSelling", 2 => "trending", _ => "newlyCreated" }) + q + (_page > 1 && _marketCursors.Count >= _page && !string.IsNullOrWhiteSpace(_marketCursors[_page - 1]) ? "&cursor=" + Uri.EscapeDataString(_marketCursors[_page - 1]!) : ""),
			MobileViewEnum.Transactions => "/v3/auth/me/transactions?limit=50",
			_ => "/v3/auth/me",
		};
	}

	private void ResetPagination() { _page = 1; _marketCursors.Clear(); _marketCursors.Add(null); _nextCursor = null; _hasNextPage = false; UpdatePagination(); }
	private void UpdatePagination()
	{
		bool paged = (_view == MobileViewEnum.Guilds && _category.CurrentTab == 0)
			|| _view is MobileViewEnum.Marketplace or MobileViewEnum.Store;
		GetNode<Control>("Layout/Pagination").Visible = paged;
		_previous.Disabled = _page <= 1;
		_next.Disabled = !_hasNextPage;
		_pageLabel.Text = $"Page {_page}";
	}
	private static bool ReadHasNextPage(JsonElement root)
	{
		if (!root.TryGetProperty("pagination", out JsonElement pagination)) return false;
		if (pagination.TryGetProperty("totalPages", out JsonElement total) && total.TryGetInt32(out int totalPages)
			&& pagination.TryGetProperty("page", out JsonElement page) && page.TryGetInt32(out int currentPage)) return currentPage < totalPages;
		return pagination.TryGetProperty("hasNextPage", out JsonElement hasNext) && hasNext.ValueKind == JsonValueKind.True;
	}

	private static IEnumerable<JsonElement> FindRecords(JsonElement root)
	{
		if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().Select(e => e.Clone());
		if (root.ValueKind != JsonValueKind.Object) return [];
		foreach (JsonProperty property in root.EnumerateObject())
			if (property.Value.ValueKind == JsonValueKind.Array) return property.Value.EnumerateArray().Select(e => e.Clone());
		foreach (string propertyName in new[] { "user", "guild", "event", "item", "world" })
			if (root.TryGetProperty(propertyName, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object) return [nested.Clone()];
		return [root.Clone()];
	}

	private static string? FirstString(JsonElement item, params string[] names)
	{
		if (item.ValueKind != JsonValueKind.Object) return item.ToString();
		foreach (string name in names)
			if (item.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number) return value.ToString();
		return null;
	}
	private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
