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
		engine.SetValue("__bv_world_handle", api.World);
		engine.Execute("const __bv_unwrap=v=>v&&typeof v==='object'&&Object.prototype.hasOwnProperty.call(v,'__bv_handle')?v.__bv_handle:v;const __bv_wrap=h=>{if(typeof h!=='number'||h===0)return h===0?null:h;return new Proxy(Object.create(null),{get:(_,p)=>p==='__bv_handle'?h:__bv_wrap(__bv_get(h,String(p))),set:(_,p,v)=>{__bv_set(h,String(p),__bv_unwrap(v));return true;}})};const bv=Object.freeze({global:n=>__bv_wrap(__bv_global(n)),get:(h,n)=>__bv_wrap(__bv_get(__bv_unwrap(h),n)),set:(h,n,v)=>__bv_set(__bv_unwrap(h),n,__bv_unwrap(v)),call:(h,n,...a)=>__bv_wrap(__bv_call(__bv_unwrap(h),n,a.map(__bv_unwrap)))});const game=__bv_wrap(__bv_world_handle);const world=game;");
		// Expose a read-only view rather than the raw DataModel instance. The
		// integer capability handle remains available through bv, while common
		// script metadata is convenient and safe to read directly.
		engine.SetValue("script", new ScriptView(script.Name));
		engine.SetValue("__bv_print", new Action<string>(message => script.Root.ScriptService.Logger.LogInfo(script, message)));
		engine.SetValue("__bv_warn", new Action<string>(message => script.Root.ScriptService.Logger.LogWarning(script, message)));
		engine.SetValue("__bv_error", new Action<string>(message => script.Root.ScriptService.Logger.LogError(script, message)));
		engine.Execute("const print=(...args)=>__bv_print(args.map(String).join(' '));const console=Object.freeze({log:print,info:print,warn:(...args)=>__bv_warn(args.map(String).join(' ')),error:(...args)=>__bv_error(args.map(String).join(' '))});");
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
	private sealed record ScriptView(string Name);
}
