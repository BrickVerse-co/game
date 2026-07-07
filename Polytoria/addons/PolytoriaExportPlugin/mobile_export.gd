@tool
extends EditorExportPlugin
class_name PolytoriaMobileExportPlugin

var original_settings := {}

func _get_name() -> String:
	return "MobileExportPlugin"

func _export_begin(features: PackedStringArray, is_debug: bool, path: String, flags: int) -> void:
	if "android" in features or "ios" in features:
		print("Exporting for mobile, applying settings...")

		# Save original settings before changing
		_store_original("application/boot_splash/bg_color")
		_store_original("application/boot_splash/show_image")

		# Apply mobile overrides
		ProjectSettings.set_setting("application/boot_splash/bg_color", "#213e61")
		ProjectSettings.set_setting("application/boot_splash/show_image", false)

		ProjectSettings.save()

func _supports_platform(platform) -> bool:
	if platform is EditorExportPlatformAndroid:
		return true
	return false


func _get_export_options_overrides(platform) -> Dictionary:
	return {
		"dotnet/android_use_linux_bionic": true,
	}


func _export_end() -> void:
	if not original_settings.is_empty():
		print("Restoring original settings...")

		for key in original_settings.keys():
			if original_settings[key] == null:
				ProjectSettings.clear(key)
			else:
				ProjectSettings.set_setting(key, original_settings[key])

		ProjectSettings.save()
		original_settings.clear()

func _store_original(key: String) -> void:
	if not original_settings.has(key):
		if ProjectSettings.has_setting(key):
			original_settings[key] = ProjectSettings.get_setting(key)
		else:
			original_settings[key] = null
