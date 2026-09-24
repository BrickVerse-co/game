// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
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
	void Print(string message);
	void Warn(string message);
	void Error(string message);
}

public abstract class BrickVerseScript
{
	protected IScriptApi Bv { get; private set; } = null!;
	protected int Script => Bv.Script;
	protected int World => Bv.World;
	protected void Print(string message) => Bv.Print(message);
	protected void Warn(string message) => Bv.Warn(message);
	protected void Error(string message) => Bv.Error(message);
	internal void Attach(IScriptApi api) => Bv = api;
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
		MethodInfo info = ScriptService.ResolveMethod(script.Compatibility, method, target.GetType())
			?? throw new MissingMethodException(target.GetType().Name, method);
		Demand(info.GetCustomAttribute<ScriptMethodAttribute>()?.Permissions ?? ScriptPermissionFlags.None);
		ParameterInfo[] parameters = info.GetParameters();
		if (parameters.Length != args.Length)
			throw new ArgumentException($"{method} expects {parameters.Length} arguments, received {args.Length}");
		object?[] converted = args.Select((value, index) =>
			ScriptService.ConvertToPropertyType(FromGuest(value), parameters[index].ParameterType)).ToArray();
		return ToGuest(info.Invoke(target, converted));
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

	private object? ToGuest(object? value) => value is IScriptObject scriptObject ? Add(scriptObject) : value;
	private object? FromGuest(object? value) => value is int handle && _objects.TryGetValue(handle, out IScriptObject? obj) ? obj : value;

	private void Demand(ScriptPermissionFlags required)
	{
		if (required != ScriptPermissionFlags.None && !script.PermissionFlags.HasFlag(required))
			throw new UnauthorizedAccessException($"Script requires permission '{required}'");
	}
}
