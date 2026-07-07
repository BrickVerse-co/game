using Godot;
using Godot.Collections;

namespace DeepLinkAddon;

public partial class DeeplinkURL : RefCounted
{
	// Constants
	public const string SCHEME_PROPERTY = "scheme";
	public const string USER_PROPERTY = "user";
	public const string PASSWORD_PROPERTY = "password";
	public const string HOST_PROPERTY = "host";
	public const string PORT_PROPERTY = "port";
	public const string PATH_PROPERTY = "path";
	public const string PATH_EXTENSION_PROPERTY = "pathExtension";
	public const string PATH_COMPONENTS_PROPERTY = "pathComponents";
	public const string PARAMETER_STRING_PROPERTY = "parameterString";
	public const string QUERY_PROPERTY = "query";
	public const string FRAGMENT_PROPERTY = "fragment";

	private Dictionary _data = [];

	public DeeplinkURL(Dictionary? data = null)
	{
		data ??= [];

		_data[SCHEME_PROPERTY] = data.ContainsKey(SCHEME_PROPERTY) ? data[SCHEME_PROPERTY] : "";
		_data[USER_PROPERTY] = data.ContainsKey(USER_PROPERTY) ? data[USER_PROPERTY] : "";
		_data[PASSWORD_PROPERTY] = data.ContainsKey(PASSWORD_PROPERTY) ? data[PASSWORD_PROPERTY] : "";
		_data[HOST_PROPERTY] = data.ContainsKey(HOST_PROPERTY) ? data[HOST_PROPERTY] : "";
		_data[PORT_PROPERTY] = data.ContainsKey(PORT_PROPERTY) ? data[PORT_PROPERTY] : -1;
		_data[PATH_PROPERTY] = data.ContainsKey(PATH_PROPERTY) ? data[PATH_PROPERTY] : "";
		_data[PATH_EXTENSION_PROPERTY] = data.ContainsKey(PATH_EXTENSION_PROPERTY) ? data[PATH_EXTENSION_PROPERTY] : "";
		_data[PATH_COMPONENTS_PROPERTY] = data.ContainsKey(PATH_COMPONENTS_PROPERTY) ? data[PATH_COMPONENTS_PROPERTY] : new Array();
		_data[PARAMETER_STRING_PROPERTY] = data.ContainsKey(PARAMETER_STRING_PROPERTY) ? data[PARAMETER_STRING_PROPERTY] : "";
		_data[QUERY_PROPERTY] = data.ContainsKey(QUERY_PROPERTY) ? data[QUERY_PROPERTY] : "";
		_data[FRAGMENT_PROPERTY] = data.ContainsKey(FRAGMENT_PROPERTY) ? data[FRAGMENT_PROPERTY] : "";
	}

	public Dictionary GetData()
	{
		return _data;
	}

	public string Scheme { get => _data[SCHEME_PROPERTY].AsString(); set => _data[SCHEME_PROPERTY] = value; }
	public string User { get => _data[USER_PROPERTY].AsString(); set => _data[USER_PROPERTY] = value; }
	public string Password { get => _data[PASSWORD_PROPERTY].AsString(); set => _data[PASSWORD_PROPERTY] = value; }
	public string Host { get => _data[HOST_PROPERTY].AsString(); set => _data[HOST_PROPERTY] = value; }
	public int Port { get => _data[PORT_PROPERTY].AsInt32(); set => _data[PORT_PROPERTY] = value; }
	public string Path { get => _data[PATH_PROPERTY].AsString(); set => _data[PATH_PROPERTY] = value; }
	public string PathExtension { get => _data[PATH_EXTENSION_PROPERTY].AsString(); set => _data[PATH_EXTENSION_PROPERTY] = value; }
	public Array PathComponents { get => _data[PATH_COMPONENTS_PROPERTY].AsGodotArray(); set => _data[PATH_COMPONENTS_PROPERTY] = value; }
	public string ParameterString { get => _data[PARAMETER_STRING_PROPERTY].AsString(); set => _data[PARAMETER_STRING_PROPERTY] = value; }
	public string Query { get => _data[QUERY_PROPERTY].AsString(); set => _data[QUERY_PROPERTY] = value; }
	public string Fragment { get => _data[FRAGMENT_PROPERTY].AsString(); set => _data[FRAGMENT_PROPERTY] = value; }
}
