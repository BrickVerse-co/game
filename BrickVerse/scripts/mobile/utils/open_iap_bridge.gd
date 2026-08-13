extends Node

const Types = preload("res://addons/godot-iap/types.gd")

signal products_loaded(products: Array)
signal purchase_received(purchase: Dictionary)
signal purchase_failed(error: Dictionary)
signal connection_changed(connected: bool)

const CUBE_SKUS: Array[String] = ["cubes_75", "cubes_250", "cubes_650", "cubes_1000", "cubes_2500", "cubes_5000", "cubes_10000"]
const MEMBERSHIP_SKUS: Array[String] = ["astro_basic", "astro", "astro_premium", "astro_basic_yearly", "astro_yearly", "astro_premium_yearly"]

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
	var request = Types.ProductRequest.new()
	request.skus = CUBE_SKUS + MEMBERSHIP_SKUS
	request.type = Types.ProductQueryType.ALL
	var result: Array = await GodotIapPlugin.fetch_products(request)
	_store_products(result)

func _on_products_fetched(result: Dictionary) -> void:
	if result.has("products"):
		_store_products(result.products)

func _store_products(result: Array) -> void:
	var serialized: Array = []
	for product in result:
		var data: Dictionary = product if product is Dictionary else product.to_dict()
		var id := str(data.get("id", ""))
		if not id.is_empty():
			products[id] = data
			serialized.append(data)
	products_loaded.emit(serialized)

func request_product(product_id: String, subscription: bool) -> void:
	if not connected:
		purchase_failed.emit({"code": "not-prepared", "message": "The app store is not connected."})
		return
	var props = Types.RequestPurchaseProps.new()
	props.request = Types.RequestPurchasePropsByPlatforms.new()
	props.request.google = Types.RequestPurchaseAndroidProps.new()
	props.request.google.skus = [product_id]
	props.request.apple = Types.RequestPurchaseIosProps.new()
	props.request.apple.sku = product_id
	props.type = Types.ProductQueryType.SUBS if subscription else Types.ProductQueryType.IN_APP
	GodotIapPlugin.request_purchase(props)

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
