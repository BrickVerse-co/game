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
	private VBoxContainer _promoHost = null!;
	private Button _previous = null!;
	private Button _next = null!;
	private Label _pageLabel = null!;
	private int _page = 1;
	private readonly List<string?> _marketCursors = [null];
	private string? _nextCursor;
	private bool _hasNextPage;
	private bool _hasLoaded;

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
		_promoHost = GetNode<VBoxContainer>("Layout/PromoHost");
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
		bool grid = _view is MobileViewEnum.Guilds or MobileViewEnum.Marketplace or MobileViewEnum.Store;
		_listItems.Visible = !grid;
		_gridItems.Visible = grid;
		_items = grid ? _gridItems : _listItems;
		ResetPagination();
		_title.Text = TitleFor(_view);
		_search.Visible = _view is MobileViewEnum.Guilds or MobileViewEnum.Forum or MobileViewEnum.Events or MobileViewEnum.Marketplace or MobileViewEnum.Store;
		_search.PlaceholderText = $"Search {TitleFor(_view).ToLowerInvariant()}";
		foreach (Node child in _promoHost.GetChildren()) child.QueueFree();
		if (_view is MobileViewEnum.Marketplace or MobileViewEnum.Store) _promoHost.AddChild(_upgradeBannerScene.Instantiate());
		if (!_hasLoaded) { _hasLoaded = true; _ = LoadAsync(); }
	}

	public override void RefreshView() { ResetPagination(); _ = LoadAsync(); }

	private async System.Threading.Tasks.Task LoadAsync()
	{
		int version = ++_loadVersion;
		_state.Text = "Loading…";
		ClearItems();
		for (int index = 0; index < 6; index++) _items.AddChild(_skeletonScene.Instantiate());
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
			_state.Text = records.Count == 0 ? "Nothing to show yet." : $"{records.Count} shown";
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
		else if (_view == MobileViewEnum.Guilds) AddInfo("Discover communities and open a guild to see its details.");
		else if (_view == MobileViewEnum.Forum) AddInfo("Browse discussions and open threads without leaving the app.");
		else if (_view is MobileViewEnum.Marketplace or MobileViewEnum.Store) { }
		else if (_view == MobileViewEnum.Profile) AddDestination("Edit account settings", MobileViewEnum.Settings);
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
			AddDestination("Transactions", MobileViewEnum.Transactions);
			AddDestination("Upgrade", MobileViewEnum.Upgrade);
			AddDestination("Settings", MobileViewEnum.Settings);
			return;
		}
		if (_view == MobileViewEnum.Settings)
		{
			AddInfo("App preferences");
			AddSettingToggle("Background loading", "background_loading", true);
			AddSettingToggle("Push notifications", "push_notifications", true);
			AddSettingToggle("Reduce motion", "reduce_motion", false);
			AddInfo("Account & privacy\nYour signed-in account, security, privacy and parental controls remain protected by the BrickVerse API.");
			AddAction("Sign out", BVMobileAuthAPI.Logout);
			return;
		}
		AddAction("Cubes", () => MobileBillingService.Singleton?.OpenProducts(MobileProductKind.Cubes));
		AddAction("BrickVerse Membership", () => MobileBillingService.Singleton?.OpenProducts(MobileProductKind.Membership));
	}

	private void AddRecord(JsonElement record)
	{
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
		else if (_view == MobileViewEnum.Forum)
			card.Pressed += () => MobileUI.Singleton.SwitchTo(MobileViewEnum.RecordDetail, new MobileRecordDetailArgs(title, "Forum thread", detail, "", _view));
		else card.Pressed += () => OpenRecord(id);
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
		MobileListCard card = CreateListCard(name, meta, Trim(description, 90), imageUrl);
		card.Pressed += () => MobileUI.Singleton.SwitchTo(MobileViewEnum.RecordDetail, new MobileRecordDetailArgs(name, meta, description, imageUrl, _view));
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
		MobileProfileSummary summary = _profileSummaryScene.Instantiate<MobileProfileSummary>();
		_items.AddChild(summary);
		summary.Configure(
			FirstString(record, "id") ?? BVMobileAuthAPI.CurrentUserInfo.Id,
			FirstString(record, "username", "name") ?? "Profile",
			(FirstString(record, "status") ?? "BrickVerse member").Replace('_', ' '),
			FirstString(record, "description") ?? "No description provided.",
			ReadNumber(stats, "visits"), ReadNumber(stats, "profileViews"), ReadNumber(stats, "forumPosts"),
			FirstString(record, "createdAt") ?? "");
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
		string imageUrl = "";
		if (record.TryGetProperty("thumbnailId", out JsonElement thumbnail) && thumbnail.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(thumbnail.GetString()))
			imageUrl = Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + thumbnail.GetString());
		MobileListCard card = CreateListCard(name, price == 0 ? "Free" : $"◈ {price:N0}", "", imageUrl);
		card.Pressed += () => ConfirmMarketplacePurchase(id, name, price);
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
		return card;
	}
	private void ClearItems() { foreach (Node child in _listItems.GetChildren()) child.QueueFree(); foreach (Node child in _gridItems.GetChildren()) child.QueueFree(); }
	private static string TitleFor(MobileViewEnum view) => view switch { MobileViewEnum.FriendRequests => "Friend requests", MobileViewEnum.Store => "Marketplace", MobileViewEnum.Dev => "More", _ => view.ToString() };

	private string PathFor(MobileViewEnum view, string search)
	{
		string q = string.IsNullOrWhiteSpace(search) ? "" : "&search=" + Uri.EscapeDataString(search.Trim());
		return view switch
		{
			MobileViewEnum.Guilds => $"/v3/social/guilds?limit=20&page={_page}" + q,
			MobileViewEnum.Profile => "/v3/profile/" + BVMobileAuthAPI.CurrentUserInfo.Id + "/id",
			MobileViewEnum.Forum => "/v3/forum/threads?limit=30" + q,
			MobileViewEnum.Events => "/v3/social/events?limit=30" + q,
			MobileViewEnum.Notifications => "/v3/social/notifications?limit=50",
			MobileViewEnum.FriendRequests => "/v3/social/friends/requests?limit=50",
			MobileViewEnum.Marketplace or MobileViewEnum.Store => "/v3/marketplace/discover?limit=20" + q + (_page > 1 && _marketCursors.Count >= _page && !string.IsNullOrWhiteSpace(_marketCursors[_page - 1]) ? "&cursor=" + Uri.EscapeDataString(_marketCursors[_page - 1]!) : ""),
			MobileViewEnum.Transactions => "/v3/auth/me/transactions?limit=50",
			_ => "/v3/auth/me",
		};
	}

	private void ResetPagination() { _page = 1; _marketCursors.Clear(); _marketCursors.Add(null); _nextCursor = null; _hasNextPage = false; UpdatePagination(); }
	private void UpdatePagination()
	{
		bool paged = _view is MobileViewEnum.Guilds or MobileViewEnum.Marketplace or MobileViewEnum.Store;
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
