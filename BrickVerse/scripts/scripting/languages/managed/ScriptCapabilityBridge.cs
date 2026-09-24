// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using BrickVerse.Attributes;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Services;

namespace BrickVerse.Scripting.Managed;

/// <summary>
/// Capability-only bridge used by non-Luau runtimes. Guest code receives opaque
/// handles, never CLR DataModel objects, so CLR reflection cannot escape the
/// ScriptProperty/ScriptMethod allow-list.
/// </summary>
public interface IScriptApi
{
	int Script { get; }
	int World { get; }
	int Global(string name);
	object? Get(int handle, string property);
	void Set(int handle, string property, object? value);
	object? Call(int handle, string method, params object?[] args);
	bool HasMethod(int handle, string method);
	int Connect(int signalHandle, Action<object?[]> callback);
	void Print(string message);
	void Warn(string message);
	void Error(string message);
}

public abstract class BrickVerseScript
{
	protected IScriptApi Bv { get; private set; } = null!;
	protected GameApi Game { get; private set; } = null!;
	protected int Script => Bv.Script;
	protected int World => Bv.World;
	protected int Global(string name) => Bv.Global(name);
	protected T? Get<T>(int handle, string property) => (T?)Bv.Get(handle, property);
	protected void Set(int handle, string property, object? value) => Bv.Set(handle, property, value);
	protected T? Call<T>(int handle, string method, params object?[] args) => (T?)Bv.Call(handle, method, args);
	protected void Call(int handle, string method, params object?[] args) => Bv.Call(handle, method, args);
	protected void Print(string message) => Bv.Print(message);
	protected void Warn(string message) => Bv.Warn(message);
	protected void Error(string message) => Bv.Error(message);
	protected int Connect(int signalHandle, Action<object?[]> callback) => Bv.Connect(signalHandle, callback);
	internal void Attach(IScriptApi api) { Bv = api; Game = new(api); }
	public virtual void Start() { }
	public virtual void Update(double delta) { }
	public virtual void FixedUpdate(double delta) { }
	public virtual object? Invoke(string name, object?[] args) => null;
}

public sealed class ScriptCapabilityBridge(Script script) : IScriptApi
{
	private readonly Dictionary<int, IScriptObject> _objects = [];
	private readonly Dictionary<IScriptObject, int> _handles = new(ReferenceEqualityComparer.Instance);
	private int _nextHandle = 1;

	public int Script => Add(script);
	public int World => Add(script.Root);

	public int Global(string name)
	{
		if (!ScriptService.GetStaticObjects(script.Root, script).TryGetValue(name, out IScriptObject? value)
			|| value == null) return 0;
		return Add(value);
	}

	public object? Get(int handle, string property)
	{
		IScriptObject target = Resolve(handle);
		if (target is Instance instance)
		{
			if (property.Equals("Name", StringComparison.Ordinal)) return instance.Name;
			Instance? child = instance.FindFirstChild(property);
			if (child != null) return Add(child);
		}
		PropertyInfo info = ScriptService.GetScriptPropertyOfName(target.GetType(), property, script.Compatibility)
			?? throw new MissingMemberException(target.GetType().Name, property);
		Demand(info.GetCustomAttribute<ScriptPropertyAttribute>()?.Permissions ?? ScriptPermissionFlags.None);
		return ToGuest(info.GetValue(target));
	}

	public void Set(int handle, string property, object? value)
	{
		IScriptObject target = Resolve(handle);
		if (target is Instance instance && property.Equals("Name", StringComparison.Ordinal))
		{
			if (value is not string name || string.IsNullOrWhiteSpace(name))
				throw new ArgumentException("Instance.Name must be a non-empty string.", nameof(value));
			instance.Name = name;
			return;
		}
		PropertyInfo info = ScriptService.GetScriptPropertyOfName(target.GetType(), property, script.Compatibility)
			?? throw new MissingMemberException(target.GetType().Name, property);
		Demand(info.GetCustomAttribute<ScriptPropertyAttribute>()?.Permissions ?? ScriptPermissionFlags.None);
		if (!info.CanWrite) throw new InvalidOperationException($"{property} is read-only");
		info.SetValue(target, ScriptService.ConvertToPropertyType(FromGuest(value), info.PropertyType));
	}

	public object? Call(int handle, string method, params object?[] args)
	{
		IScriptObject target = Resolve(handle);
		object?[] guestArguments = args.Select(FromGuest).ToArray();
		MethodInfo info = ResolveCallableMethod(target.GetType(), method, guestArguments)
			?? throw new MissingMethodException(target.GetType().Name, method);
		Demand(info.GetCustomAttribute<ScriptMethodAttribute>()?.Permissions ?? ScriptPermissionFlags.None);
		ParameterInfo[] parameters = info.GetParameters();
		object?[] converted = new object?[parameters.Length];
		int guestIndex = 0;
		for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
		{
			ParameterInfo parameter = parameters[parameterIndex];
			if (parameter.GetCustomAttribute<ScriptingCallerAttribute>() != null)
			{
				converted[parameterIndex] = script;
				continue;
			}
			converted[parameterIndex] = guestIndex < guestArguments.Length
				? ScriptService.ConvertToPropertyType(guestArguments[guestIndex++], parameter.ParameterType)
				: parameter.DefaultValue;
		}
		object? result = info.Invoke(target, converted);
		if (result is System.Threading.Tasks.Task task)
		{
			task.GetAwaiter().GetResult();
			result = task.GetType().IsGenericType ? task.GetType().GetProperty("Result")?.GetValue(task) : null;
		}
		return ToGuest(result);
	}

	public bool HasMethod(int handle, string method) =>
		Resolve(handle).GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
			.Any(candidate => IsExposedMethod(candidate, method));

	private MethodInfo? ResolveCallableMethod(Type type, string name, object?[] arguments) =>
		type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
			.Where(candidate => IsExposedMethod(candidate, name))
			.FirstOrDefault(candidate => ParametersAccept(candidate.GetParameters(), arguments));

	private bool IsExposedMethod(MethodInfo method, string requestedName)
	{
		ScriptMethodAttribute? attribute = method.GetCustomAttribute<ScriptMethodAttribute>();
		if (attribute != null && (method.Name == requestedName || attribute.MethodName == requestedName)) return true;
		if (!script.Compatibility) return false;
		ScriptLegacyMethodAttribute? legacy = method.GetCustomAttribute<ScriptLegacyMethodAttribute>();
		return string.Equals(legacy?.MethodName, requestedName, StringComparison.OrdinalIgnoreCase);
	}

	private static bool ParametersAccept(ParameterInfo[] parameters, object?[] arguments)
	{
		ParameterInfo[] visible = parameters.Where(parameter => parameter.GetCustomAttribute<ScriptingCallerAttribute>() == null).ToArray();
		int required = visible.Count(parameter => !parameter.IsOptional);
		if (arguments.Length < required || arguments.Length > visible.Length) return false;
		for (int index = 0; index < arguments.Length; index++)
			if (arguments[index] != null && !ScriptService.IsObjectConvertible(arguments[index]!, visible[index].ParameterType)) return false;
		return true;
	}

	public int Connect(int handle, Action<object?[]> callback)
	{
		if (Resolve(handle) is not BVSignal signal)
			throw new InvalidOperationException("Connect is only available on BrickVerse signals.");
		BVCallback bvCallback = new(args => callback(args.Select(ToGuest).ToArray()))
		{
			FromScript = script,
			LangProvider = script.LanguageProvider,
		};
		return Add(signal.Connect(bvCallback));
	}

	public void Print(string message) => script.Root.ScriptService.Logger.LogInfo(script, message ?? string.Empty);
	public void Warn(string message) => script.Root.ScriptService.Logger.LogWarning(script, message ?? string.Empty);
	public void Error(string message) => script.Root.ScriptService.Logger.LogError(script, message ?? string.Empty);

	private int Add(IScriptObject value)
	{
		if (_handles.TryGetValue(value, out int handle)) return handle;
		handle = _nextHandle++;
		_handles[value] = handle;
		_objects[handle] = value;
		return handle;
	}

	private IScriptObject Resolve(int handle) => _objects.TryGetValue(handle, out IScriptObject? value)
		? value : throw new InvalidOperationException("Invalid or expired script object handle");

	private object? ToGuest(object? value)
	{
		if (value is IScriptObject scriptObject) return Add(scriptObject);
		if (value is Array array) return array.Cast<object?>().Select(ToGuest).ToArray();
		if (value is IDictionary dictionary)
		{
			Dictionary<string, object?> result = [];
			foreach (DictionaryEntry entry in dictionary)
				if (entry.Key is string key) result[key] = ToGuest(entry.Value);
			return result;
		}
		return value;
	}

	private object? FromGuest(object? value)
	{
		if (value is ScriptObject wrapper) return Resolve(wrapper.Handle);
		if (value is int handle && _objects.TryGetValue(handle, out IScriptObject? obj)) return obj;
		if (value is object?[] array) return array.Select(FromGuest).ToArray();
		return value;
	}

	private void Demand(ScriptPermissionFlags required)
	{
		if (required != ScriptPermissionFlags.None && !script.PermissionFlags.HasFlag(required))
			throw new UnauthorizedAccessException($"Script requires permission '{required}'");
	}
}
