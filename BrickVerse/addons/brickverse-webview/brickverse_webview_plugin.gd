@tool
extends EditorPlugin

var _export_plugin: BrickVerseWebViewExportPlugin

func _enter_tree() -> void:
	_export_plugin = BrickVerseWebViewExportPlugin.new()
	add_export_plugin(_export_plugin)

func _exit_tree() -> void:
	if _export_plugin:
		remove_export_plugin(_export_plugin)
		_export_plugin = null

class BrickVerseWebViewExportPlugin extends EditorExportPlugin:
	func _get_name() -> String:
		return "BrickVerseWebView"

	func _supports_platform(platform: EditorExportPlatform) -> bool:
		return platform is EditorExportPlatformAndroid

	func _get_android_libraries(_platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
		var suffix := "debug" if debug else "release"
		var requested := "res://addons/brickverse-webview/android/BrickVerseWebView.%s.aar" % suffix
		if FileAccess.file_exists(requested):
			return PackedStringArray([requested])
		# This plugin currently ships one release AAR which is safe in debug exports too.
		return PackedStringArray(["res://addons/brickverse-webview/android/BrickVerseWebView.release.aar"])
