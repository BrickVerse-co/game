@tool
extends EditorPlugin

var mobile_export_plugin : PolytoriaMobileExportPlugin
var dllcpy_export_plugin : PolytoriaDllCpyExportPlugin
var execpy_export_plugin : PolytoriaExeCpyExportPlugin
var export_config_plugin : PolytoriaConfigExportPlugin

func _enter_tree():
	mobile_export_plugin = PolytoriaMobileExportPlugin.new()
	dllcpy_export_plugin = PolytoriaDllCpyExportPlugin.new()
	execpy_export_plugin = PolytoriaExeCpyExportPlugin.new()
	export_config_plugin = PolytoriaConfigExportPlugin.new()
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
