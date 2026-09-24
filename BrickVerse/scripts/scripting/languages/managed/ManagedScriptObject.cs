// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Linq;

namespace BrickVerse.Scripting.Managed;

/// <summary>Typed, capability-scoped view of a DataModel object for managed guest scripts.</summary>
public class ScriptObject
{
	internal IScriptApi Api { get; }
	internal int Handle { get; }
	public ScriptObject(IScriptApi api, int handle) { Api = api; Handle = handle; }

	protected T? Read<T>(string property) => ConvertValue<T>(Api.Get(Handle, property));
	protected void Write(string property, object? value) => Api.Set(Handle, property, Unwrap(value));
	protected T? Invoke<T>(string method, params object?[] args) => ConvertValue<T>(Api.Call(Handle, method, UnwrapAll(args)));
	protected void Invoke(string method, params object?[] args) => Api.Call(Handle, method, UnwrapAll(args));

	internal T? ConvertValue<T>(object? value)
	{
		if (value is null) return default;
		Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
		if (typeof(ScriptObject).IsAssignableFrom(target) && value is int handle)
			return (T?)Activator.CreateInstance(target, Api, handle);
		if (target.IsEnum) return (T)Enum.ToObject(target, value);
		if (value is T typed) return typed;
		return (T?)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
	}

	internal static object? Unwrap(object? value) => value is ScriptObject item ? item.Handle : value;
	private static object?[] UnwrapAll(object?[] values)
	{
		object?[] result = new object?[values.Length];
		for (int index = 0; index < values.Length; index++) result[index] = Unwrap(values[index]);
		return result;
	}
}

public sealed class ScriptSignal<T> : ScriptObject
{
	public ScriptSignal(IScriptApi api, int handle) : base(api, handle) { }
	public int Connect(Action<T> callback) => Api.Connect(Handle, args =>
		callback(ConvertValue<T>(args.Length == 0 ? null : args[0])!));
}

public sealed class ScriptSignal<T1, T2> : ScriptObject
{
	public ScriptSignal(IScriptApi api, int handle) : base(api, handle) { }
	public int Connect(Action<T1, T2> callback) => Api.Connect(Handle, args =>
		callback(ConvertValue<T1>(args.ElementAtOrDefault(0))!, ConvertValue<T2>(args.ElementAtOrDefault(1))!));
}

public sealed class ScriptSignal<T1, T2, T3> : ScriptObject
{
	public ScriptSignal(IScriptApi api, int handle) : base(api, handle) { }
	public int Connect(Action<T1, T2, T3> callback) => Api.Connect(Handle, args => callback(
		ConvertValue<T1>(args.ElementAtOrDefault(0))!, ConvertValue<T2>(args.ElementAtOrDefault(1))!,
		ConvertValue<T3>(args.ElementAtOrDefault(2))!));
}

public sealed class ScriptSignal<T1, T2, T3, T4> : ScriptObject
{
	public ScriptSignal(IScriptApi api, int handle) : base(api, handle) { }
	public int Connect(Action<T1, T2, T3, T4> callback) => Api.Connect(Handle, args => callback(
		ConvertValue<T1>(args.ElementAtOrDefault(0))!, ConvertValue<T2>(args.ElementAtOrDefault(1))!,
		ConvertValue<T3>(args.ElementAtOrDefault(2))!, ConvertValue<T4>(args.ElementAtOrDefault(3))!));
}

public sealed class ScriptSignal : ScriptObject
{
	public ScriptSignal(IScriptApi api, int handle) : base(api, handle) { }
	public int Connect(Action callback) => Api.Connect(Handle, _ => callback());
}

public sealed class GameApi
{
	private readonly IScriptApi _api;
	public GameApi(IScriptApi api) => _api = api;
	public T GetService<T>(string name) where T : ScriptObject =>
		(T)Activator.CreateInstance(typeof(T), _api, _api.Global(name))!;
	public T Global<T>(string name) where T : ScriptObject => GetService<T>(name);
}
