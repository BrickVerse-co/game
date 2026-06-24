@tool
extends EditorPlugin

var mobile_export_plugin : BrickVerseMobileExportPlugin
var dllcpy_export_plugin : BrickVerseDllCpyExportPlugin
var execpy_export_plugin : BrickVerseExeCpyExportPlugin
var export_config_plugin : BrickVerseConfigExportPlugin

func _enter_tree():
	mobile_export_plugin = BrickVerseMobileExportPlugin.new()
	dllcpy_export_plugin = BrickVerseDllCpyExportPlugin.new()
	execpy_export_plugin = BrickVerseExeCpyExportPlugin.new()
	export_config_plugin = BrickVerseConfigExportPlugin.new()
	add_export_plugin(mobile_export_plugin)
	add_export_plugin(dllcpy_export_plugin)
	add_export_plugin(execpy_export_plugin)
	add_export_plugin(export_config_plugin)


func _exit_tree():
	remove_export_plugin(mobile_export_plugin)
	remove_export_plugin(export_config_plugin)
	remove_export_plugin(execpy_export_plugin)
	remove_export_plugin(dllcpy_export_plugin)
	mobile_export_plugin = null
	dllcpy_export_plugin = null
	execpy_export_plugin = null
	export_config_plugin = null
