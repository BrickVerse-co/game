using System;
using System.Collections.Generic;
using BrickVerse.Mobile.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileIapProductsDialog : Window
{
	private ItemList _products = null!;
	private Button _purchase = null!;
	private Label _status = null!;
	private readonly List<(string StoreProductId, bool Subscription, string OfferToken)> _ids = [];

	public override void _Ready()
	{
		_products = GetNode<ItemList>("Layout/Products");
		_purchase = GetNode<Button>("Layout/Actions/Purchase");
		_status = GetNode<Label>("Layout/Status");
		_purchase.Pressed += PurchaseSelected;
		GetNode<Button>("Layout/Actions/Restore").Pressed += () => MobileBillingService.Singleton?.RestorePurchases();
		CloseRequested += Hide;
	}

	public void ShowProducts(MobileProductKind kind, IReadOnlyList<MobileStoreProduct> products)
	{
		Title = kind == MobileProductKind.Cubes ? "Buy Cubes" : "Choose Membership";
		_products.Clear();
		_ids.Clear();
		foreach (MobileStoreProduct product in products)
		{
			if ((kind == MobileProductKind.Membership) != product.Subscription) continue;
			_products.AddItem($"{product.Title}\n{product.DisplayPrice}");
			_ids.Add((product.StoreProductId, product.Subscription, product.OfferToken));
		}
		_status.Text = _ids.Count == 0 ? "Products are still loading from the app store." : "Purchases are processed securely by your device's app store.";
		_purchase.Disabled = _ids.Count == 0;
		PopupCentered(new Vector2I(390, 540));
	}

	public void SetStatus(string status, bool busy = false) { _status.Text = status; _purchase.Disabled = busy || _ids.Count == 0; }

	private void PurchaseSelected()
	{
		int selected = _products.GetSelectedItems().Length > 0 ? _products.GetSelectedItems()[0] : -1;
		if (selected < 0 || selected >= _ids.Count) { _status.Text = "Select a product first."; return; }
		(string id, bool subscription, string offerToken) = _ids[selected];
		SetStatus("Opening the app store…", true);
		MobileBillingService.Singleton?.Purchase(id, subscription, offerToken);
	}
}

public sealed record MobileStoreProduct(string Id, string StoreProductId, string Title, string DisplayPrice, bool Subscription, string OfferToken);
