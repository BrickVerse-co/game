// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BrickVerse.Scripting.Managed;
using Jint;
using Jint.Native;
using Script = BrickVerse.Datamodel.Script;

namespace BrickVerse.Scripting.JavaScript;

public class JavaScriptProvider : IScriptLanguageProvider
{
	private readonly Dictionary<Script, Engine> _engines = [];

	public virtual byte[] CompileSource(string source) => Encoding.UTF8.GetBytes(source);

	public void Run(Script script)
	{
		try { script.TryCompile(); }
		catch (Exception exception)
		{
			script.Root.ScriptService.Logger.LogError(script, exception.Message, 0);
			return;
		}
		ScriptCapabilityBridge api = new(script);
		Engine engine = new(options => options
			.Strict()
			.LimitMemory(16_000_000)
			.MaxStatements(250_000)
			.LimitRecursion(128)
			.MaxArraySize(100_000)
			.TimeoutInterval(TimeSpan.FromSeconds(2)));
		engine.SetValue("__bv_global", new Func<string, int>(api.Global));
		engine.SetValue("__bv_get", new Func<int, string, object?>(api.Get));
		engine.SetValue("__bv_set", new Action<int, string, object?>(api.Set));
		engine.SetValue("__bv_call", new Func<int, string, object?[], object?>(api.Call));
		engine.Execute("const bv=Object.freeze({global:__bv_global,get:__bv_get,set:__bv_set,call:(h,n,...a)=>__bv_call(h,n,a)});");
		engine.SetValue("script", api.Script);
		engine.SetValue("world", api.World);
		engine.SetValue("game", api.World);
		engine.SetValue("print", new Action<object?>(value => script.Root.ScriptService.Logger.LogInfo(script, value?.ToString() ?? "null")));
		_engines[script] = engine;
		try { engine.Execute(Encoding.UTF8.GetString(script.Bytecode!)); }
		catch (Exception exception)
		{
			_engines.Remove(script);
			script.Root.ScriptService.Logger.LogError(script, exception.Message, 0);
		}
	}

	public Task CallAsync(Script script, string funcName, object?[]? args)
	{
		Invoke(script, funcName, args ?? []);
		return Task.CompletedTask;
	}

	public void CallUpdate(Script script, double delta) => Invoke(script, "update", [delta]);
	public void CallFixedUpdate(Script script, double delta) => Invoke(script, "fixedUpdate", [delta]);

	private void Invoke(Script script, string name, object?[] args)
	{
		if (!_engines.TryGetValue(script, out Engine? engine)) return;
		JsValue function = engine.GetValue(name);
		if (!function.IsCallable()) return;
		try { engine.Invoke(function, args); }
		catch (Exception exception) { script.Root.ScriptService.Logger.LogError(script, exception.Message, 0); }
	}

	public void Close(Script script) => _engines.Remove(script);
	public void FreeBVCallback(BVCallback callback) { }
	public void Dispose() => _engines.Clear();
}
