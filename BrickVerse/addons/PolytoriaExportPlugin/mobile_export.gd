@tool
extends EditorExportPlugin
class_name BrickVerseMobileExportPlugin

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

	if "android" in features:
		# Godot's .NET exporter keeps the primary NativeAOT image but otherwise
		# drops this runtime-pack companion. HttpClient loads it dynamically on
		# the first TLS request; omitting it terminates the Android process.
		var configuration := "ExportDebug" if is_debug else "ExportRelease"
		_add_android_runtime_library(configuration, "android-arm64", "arm64")
		_add_android_runtime_library(configuration, "android-x64", "x86_64")

func _add_android_runtime_library(configuration: String, runtime_id: String, architecture: String) -> void:
	var library_path := "res://.godot/mono/temp/bin/%s/%s/publish/libSystem.Security.Cryptography.Native.Android.so" % [configuration, runtime_id]
	if not FileAccess.file_exists(library_path):
		push_error("Required Android .NET runtime library was not published: " + library_path)
		return
	add_shared_object(library_path, PackedStringArray([architecture]), "")

func _supports_platform(platform) -> bool:
	if platform is EditorExportPlatformAndroid:
		return true
	return false


func _get_export_options_overrides(platform) -> Dictionary:
	return {
		# Linux-bionic is not the Android runtime. Forcing it on x86_64 omits
		# System.Security.Cryptography.Native.Android from the APK, causing the
		# first HTTPS request to abort the process on a .NET worker thread.
		"dotnet/android_use_linux_bionic": false,
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
