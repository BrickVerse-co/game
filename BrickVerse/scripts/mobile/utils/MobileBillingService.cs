// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using BrickVerse.Mobile.UI;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.Utils;

public partial class MobileBillingService : Node
{
	public static MobileBillingService? Singleton { get; private set; }
	private Node? _bridge;
	private MobileIapProductsDialog _dialog = null!;
	private readonly List<MobileStoreProduct> _products = [];
	private MobileProductKind _visibleKind;
	private bool _storeConnected;

	public override void _Ready()
	{
		Singleton = this;
		_dialog = GD.Load<PackedScene>("res://scenes/mobile/views/iap_products_dialog.tscn").Instantiate<MobileIapProductsDialog>();
		AddChild(_dialog);
		CallDeferred(MethodName.ConnectBridge);
	}

	private void ConnectBridge()
	{
		_bridge = GetNodeOrNull<Node>("/root/BrickVerseOpenIap");
		if (_bridge == null) { BV.PrintErr("OpenIAP bridge is unavailable. Enable the godot-iap plugin for mobile exports."); return; }
		_bridge.Connect("products_loaded", Callable.From<Godot.Collections.Array>(OnProductsLoaded));
		_bridge.Connect("products_unavailable", Callable.From<string>(OnProductsUnavailable));
		_bridge.Connect("purchase_received", Callable.From<Godot.Collections.Dictionary>(OnPurchaseReceived));
		_bridge.Connect("purchase_failed", Callable.From<Godot.Collections.Dictionary>(OnPurchaseFailed));
		_bridge.Connect("connection_changed", Callable.From<bool>(OnConnectionChanged));
	}

	public void OpenProducts(MobileProductKind kind)
	{
		_visibleKind = kind;
		if (_bridge == null)
		{
			OS.Alert("App-store purchases are only available in Android and iOS builds.", "Purchases unavailable");
			return;
		}
		_dialog.ShowProducts(kind, _products);
		if (!_storeConnected)
		{
			_dialog.SetStatus("Google Play is unavailable. Install this app from a Play testing track and sign in to Google Play, then try again.");
			return;
		}
		if (_products.Count == 0)
		{
			_dialog.SetStatus("Loading products from Google Play…", true);
			_bridge.Call("refresh_products");
		}
	}

	private void OnConnectionChanged(bool connected)
	{
		_storeConnected = connected;
		if (!connected && _dialog.Visible)
			_dialog.SetStatus("The app store could not be reached. Check Google Play and your connection, then try again.");
	}

	private void OnProductsUnavailable(string message)
	{
		BV.PrintErr("Mobile IAP products unavailable: ", message);
		if (_dialog.Visible)
			_dialog.SetStatus(message);
	}

	public void Purchase(string productId, bool subscription, string offerToken) => _bridge?.Call("request_product", productId, subscription, offerToken);
	public void RestorePurchases() { _dialog.SetStatus("Checking your app-store purchases…", true); _bridge?.Call("restore_purchases"); }

	private void OnProductsLoaded(Godot.Collections.Array products)
	{
		_products.Clear();
		foreach (Variant value in products)
		{
			Godot.Collections.Dictionary product = value.AsGodotDictionary();
			string id = Read(product, "id");
			if (string.IsNullOrWhiteSpace(id)) continue;
			string title = Read(product, "title");
			string price = Read(product, "displayPrice", "display_price", "localizedPrice");
			string type = Read(product, "type");
			string storeProductId = Read(product, "storeProductId", "store_product_id");
			string offerToken = Read(product, "offerToken", "offer_token");
			bool subscription = type.Contains("sub", StringComparison.OrdinalIgnoreCase) || storeProductId == "astro_membership";
			_products.Add(new(id, string.IsNullOrWhiteSpace(storeProductId) ? id : storeProductId, string.IsNullOrWhiteSpace(title) ? ProductTitle(id) : title, price, subscription, offerToken));
		}
		if (_dialog.Visible) _dialog.ShowProducts(_visibleKind, _products);
	}

	private async void OnPurchaseReceived(Godot.Collections.Dictionary purchase)
	{
		string productId = Read(purchase, "productId", "product_id");
		bool consumable = productId.StartsWith("cubes_", StringComparison.Ordinal);
		_dialog.SetStatus("Verifying your purchase with BrickVerse…", true);
		try
		{
			string purchaseJson = Json.Stringify(purchase);
			string payload = $"{{\"platform\":{JsonSerializer.Serialize(OS.GetName())},\"purchase\":{purchaseJson}}}";
			using JsonDocument verified = await BVAPI.SendJson(HttpMethod.Post, "/v3/auth/mobile-iap/verify", payload);
			if (!verified.RootElement.TryGetProperty("success", out JsonElement success) || !success.GetBoolean())
				throw new InvalidOperationException(verified.RootElement.TryGetProperty("message", out JsonElement message) ? message.GetString() : "Purchase verification failed.");
			_bridge?.Call("finish_verified_purchase", purchase, consumable);
			_dialog.SetStatus(consumable ? "Cubes added to your account." : "Membership activated.");
		}
		catch (Exception exception)
		{
			// Never finish an unverified transaction. It will be replayed for recovery.
			_dialog.SetStatus("We could not verify this purchase. It has not been consumed; use Restore purchases after retrying.");
			BV.PrintErr("Mobile IAP verification failed: ", exception);
		}
	}

	private void OnPurchaseFailed(Godot.Collections.Dictionary error)
	{
		string code = Read(error, "code");
		_dialog.SetStatus(code == "user-cancelled" ? "Purchase cancelled." : Read(error, "message"));
	}

	private static string Read(Godot.Collections.Dictionary dictionary, params string[] keys)
	{
		foreach (string key in keys) if (dictionary.TryGetValue(key, out Variant value)) return value.AsString();
		return "";
	}

	private static string ProductTitle(string id) => id.Replace('_', ' ').Replace("astro", "Astro", StringComparison.OrdinalIgnoreCase).Replace("cubes", "Cubes", StringComparison.OrdinalIgnoreCase);
}

public enum MobileProductKind { Cubes, Membership }
