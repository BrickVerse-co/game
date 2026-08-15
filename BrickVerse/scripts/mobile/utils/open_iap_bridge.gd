extends Node

const Types = preload("res://addons/godot-iap/types.gd")

signal products_loaded(products: Array)
signal products_unavailable(message: String)
signal purchase_received(purchase: Dictionary)
signal purchase_failed(error: Dictionary)
signal connection_changed(connected: bool)

const CUBE_SKUS: Array[String] = ["cubes_75", "cubes_250", "cubes_650", "cubes_1000", "cubes_2500", "cubes_5000", "cubes_10000"]
const ASTRO_PRODUCT_ID := "astro_membership"

var connected := false
var products: Dictionary = {}

func _ready() -> void:
	GodotIapPlugin.purchase_updated.connect(_on_purchase_updated)
	GodotIapPlugin.purchase_error.connect(_on_purchase_error)
	GodotIapPlugin.products_fetched.connect(_on_products_fetched)
	connected = await GodotIapPlugin.init_connection()
	connection_changed.emit(connected)
	if connected:
		call_deferred("refresh_products")

func _exit_tree() -> void:
	if connected:
		await GodotIapPlugin.end_connection()

func refresh_products() -> void:
	if not connected:
		return
	var cubes_request = Types.ProductRequest.new()
	cubes_request.skus = CUBE_SKUS
	cubes_request.type = Types.ProductQueryType.IN_APP
	var subscriptions_request = Types.ProductRequest.new()
	subscriptions_request.skus = [ASTRO_PRODUCT_ID]
	subscriptions_request.type = Types.ProductQueryType.SUBS
	var result: Array = await GodotIapPlugin.fetch_products(cubes_request)
	result.append_array(await GodotIapPlugin.fetch_products(subscriptions_request))
	if result.is_empty():
		products_unavailable.emit("Google Play did not return any products. Install the app from an active Play testing track and verify the product IDs are active.")
	else:
		_store_products(result)

func _on_products_fetched(result: Dictionary) -> void:
	if result.has("products"):
		_store_products(result.products)

func _store_products(result: Array) -> void:
	var serialized: Array = []
	for product in result:
		var data: Dictionary = product if product is Dictionary else product.to_dict()
		var id := str(data.get("id", ""))
		if id.is_empty():
			continue
		if id == ASTRO_PRODUCT_ID:
			for raw_offer in data.get("subscriptionOffers", []):
				var offer: Dictionary = raw_offer if raw_offer is Dictionary else raw_offer.to_dict()
				var base_plan_id := str(offer.get("basePlanIdAndroid", ""))
				var offer_token := str(offer.get("offerTokenAndroid", ""))
				if base_plan_id.is_empty() or offer_token.is_empty():
					continue
				var normalized := {
					"id": base_plan_id,
					"storeProductId": ASTRO_PRODUCT_ID,
					"title": _membership_title(base_plan_id),
					"displayPrice": str(offer.get("displayPrice", data.get("displayPrice", ""))),
					"type": "subs",
					"offerToken": offer_token,
				}
				products[base_plan_id] = normalized
				serialized.append(normalized)
		else:
			data["storeProductId"] = id
			products[id] = data
			serialized.append(data)
	products_loaded.emit(serialized)

func request_product(product_id: String, subscription: bool, offer_token: String = "") -> void:
	if not connected:
		purchase_failed.emit({"code": "not-prepared", "message": "The app store is not connected."})
		return
	var props = Types.RequestPurchaseProps.new()
	if subscription:
		if product_id != ASTRO_PRODUCT_ID or offer_token.is_empty():
			purchase_failed.emit({"code": "developer-error", "message": "The selected Astro base plan is unavailable."})
			return
		props.request_subscription = Types.RequestSubscriptionPropsByPlatforms.new()
		props.request_subscription.google = Types.RequestSubscriptionAndroidProps.new()
		props.request_subscription.google.skus = [ASTRO_PRODUCT_ID]
		var offer = Types.AndroidSubscriptionOfferInput.new()
		offer.sku = ASTRO_PRODUCT_ID
		offer.offer_token = offer_token
		props.request_subscription.google.subscription_offers = [offer]
		props.request_subscription.apple = Types.RequestSubscriptionIosProps.new()
		props.request_subscription.apple.sku = ASTRO_PRODUCT_ID
		props.type = Types.ProductQueryType.SUBS
	else:
		props.request = Types.RequestPurchasePropsByPlatforms.new()
		props.request.google = Types.RequestPurchaseAndroidProps.new()
		props.request.google.skus = [product_id]
		props.request.apple = Types.RequestPurchaseIosProps.new()
		props.request.apple.sku = product_id
		props.type = Types.ProductQueryType.IN_APP
	GodotIapPlugin.request_purchase(props)

func _membership_title(base_plan_id: String) -> String:
	match base_plan_id:
		"astro-basic": return "Astro Basic"
		"astro": return "Astro"
		"astro-premium": return "Astro Premium"
		_: return base_plan_id.replace("-", " ").capitalize()

func restore_purchases() -> void:
	if connected:
		await GodotIapPlugin.restore_purchases()
		var available_purchases: Array = await GodotIapPlugin.get_available_purchases()
		for purchase in available_purchases:
			var data: Dictionary = purchase if purchase is Dictionary else purchase.to_dict()
			purchase_received.emit(data)

func finish_verified_purchase(purchase: Dictionary, consumable: bool) -> void:
	await GodotIapPlugin.finish_transaction_dict(purchase, consumable)

func _on_purchase_updated(purchase: Dictionary) -> void:
	var state := str(purchase.get("purchaseState", "")).to_lower()
	if state == "purchased":
		purchase_received.emit(purchase)

func _on_purchase_error(error: Dictionary) -> void:
	purchase_failed.emit(error)
