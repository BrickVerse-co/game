// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;

namespace BrickVerse.Mobile.Utils;

public partial class MobileBillingService : Node
{
	public static MobileBillingService? Singleton { get; private set; }
	public override void _Ready() => Singleton = this;

	public void OpenProducts(MobileProductKind kind)
	{
		string singletonName = OS.GetName() == "Android" ? "GodotGooglePlayBilling" : "InAppStore";
		if (!Engine.HasSingleton(singletonName))
		{
			OS.Alert($"{(OS.GetName() == "Android" ? "Google Play Billing" : "Apple StoreKit")} is not installed in this build.", "Purchases unavailable");
			return;
		}

		// Product querying and purchase callbacks are supplied by the platform
		// billing plugin. Do not fall back to web/Stripe for mobile digital goods.
		EmitSignal(SignalName.ProductRequested, (int)kind);
	}

	[Signal] public delegate void ProductRequestedEventHandler(int kind);
}

public enum MobileProductKind { Cubes, Membership }
