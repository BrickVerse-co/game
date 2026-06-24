// © 2024-present https://github.com/cengiz-pz

using Godot;
using Polytoria.Shared;
using System;

namespace DeepLinkAddon;

[Tool]
public partial class Deeplink : Node
{
	public event Action<DeeplinkURL>? DeeplinkReceived;

	private const string PluginSingletonName = "DeeplinkPlugin";
	private const string DeeplinkReceivedSignalName = "deeplink_received";

	private GodotObject _pluginSingleton = null!;

	public override void _Ready()
	{
		if (_pluginSingleton == null)
		{
			if (Engine.HasSingleton(PluginSingletonName))
			{
				_pluginSingleton = Engine.GetSingleton(PluginSingletonName);
				ConnectSignals();
			}
			else if (!Globals.IsInGDEditor)
			{
				LogError($"{PluginSingletonName} singleton not found!");
			}
		}
	}

	private void ConnectSignals()
	{
		_pluginSingleton.Connect(DeeplinkReceivedSignalName, new Callable(this, nameof(OnDeeplinkReceived)));
	}

	public int Initialize()
	{
		if (_pluginSingleton != null)
		{
			return (int)_pluginSingleton.Call("initialize");
		}

		LogError($"{PluginSingletonName} plugin not initialized");
		return (int)Error.Failed;
	}

	public bool IsDomainAssociated(string domain)
	{
		if (_pluginSingleton != null)
		{
			return (bool)_pluginSingleton.Call("is_domain_associated", domain);
		}

		LogError($"{PluginSingletonName} plugin not initialized");
		return false;
	}

	public void NavigateToOpenByDefaultSettings()
	{
		if (_pluginSingleton != null)
		{
			_pluginSingleton.Call("navigate_to_open_by_default_settings");
		}
		else
		{
			LogError($"{PluginSingletonName} plugin not initialized");
		}
	}

	public string GetLinkUrl()
	{
		return _pluginSingleton != null
			? NullCheck(_pluginSingleton.Call("get_url"))
			: MissingPluginError();
	}

	public string GetLinkScheme()
	{
		return _pluginSingleton != null
			? NullCheck(_pluginSingleton.Call("get_scheme"))
			: MissingPluginError();
	}

	public string GetLinkHost()
	{
		return _pluginSingleton != null
			? NullCheck(_pluginSingleton.Call("get_host"))
			: MissingPluginError();
	}

	public string GetLinkPath()
	{
		return _pluginSingleton != null
			? NullCheck(_pluginSingleton.Call("get_path"))
			: MissingPluginError();
	}

	public void ClearData()
	{
		if (_pluginSingleton != null)
		{
			_pluginSingleton.Call("clear_data");
		}
		else
		{
			LogError($"{PluginSingletonName} plugin not initialized");
		}
	}

	private void OnDeeplinkReceived(Godot.Collections.Dictionary data)
	{
		DeeplinkReceived?.Invoke(new DeeplinkURL(data));
	}

	private static string NullCheck(Variant value)
	{
		return value.VariantType == Variant.Type.Nil ? "" : value.AsString();
	}

	private static string MissingPluginError()
	{
		LogError($"{PluginSingletonName} plugin not initialized");
		return "";
	}

	private static void LogError(string description)
	{
		GD.PushError(description);
	}

	private static void LogWarn(string description)
	{
		GD.PushWarning(description);
	}

	private static void LogInfo(string description)
	{
		GD.PrintRich($"[color=purple]INFO: {description}[/color]");
	}
}
