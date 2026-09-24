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
	private const string RuntimeBootstrap = """
const __bv_unwrap = value => {
    const isHandle = value
        && typeof value === "object"
        && Object.prototype.hasOwnProperty.call(value, "__bv_handle");
    return isHandle ? value.__bv_handle : value;
};

const __bv_wrap = handle => {
    if (typeof handle !== "number" || handle === 0) {
        return handle === 0 ? null : handle;
    }

    return new Proxy(Object.create(null), {
        get: (_, property) => {
            if (property === "__bv_handle") return handle;
            // Prevent Promise resolution from treating every DataModel object as a thenable.
            if (property === "then") return undefined;

            const name = String(property);
            if (name === "Connect") {
                return callback => __bv_wrap(__bv_connect(handle, (...args) => {
                    callback(...args.map(__bv_wrap));
                }));
            }
            if (__bv_has_method(handle, name)) {
                return (...args) => __bv_wrap(__bv_call(handle, name, args.map(__bv_unwrap)));
            }
            return __bv_wrap(__bv_get(handle, name));
        },
        set: (_, property, value) => {
            __bv_set(handle, String(property), __bv_unwrap(value));
            return true;
        },
    });
};

const bv = Object.freeze({
    global: name => __bv_wrap(__bv_global(name)),
    get: (target, name) => __bv_wrap(__bv_get(__bv_unwrap(target), name)),
    set: (target, name, value) => __bv_set(__bv_unwrap(target), name, __bv_unwrap(value)),
    call: (target, name, ...args) => __bv_wrap(
        __bv_call(__bv_unwrap(target), name, args.map(__bv_unwrap))
    ),
});

const game = __bv_wrap(__bv_world_handle);
const world = game;
""";

	private const string ConsoleBootstrap = """
const print = (...args) => __bv_print(args.map(String).join(" "));
const console = Object.freeze({
    log: print,
    info: print,
    warn: (...args) => __bv_warn(args.map(String).join(" ")),
    error: (...args) => __bv_error(args.map(String).join(" ")),
});
""";

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
		engine.SetValue("__bv_has_method", new Func<int, string, bool>(api.HasMethod));
		engine.SetValue("__bv_connect", new Func<int, JsValue, int>((handle, callback) =>
			api.Connect(handle, args => engine.Invoke(callback, args))));
		engine.SetValue("__bv_world_handle", api.World);
		engine.Execute(RuntimeBootstrap);
		// Expose a read-only view rather than the raw DataModel instance. The
		// integer capability handle remains available through bv, while common
		// script metadata is convenient and safe to read directly.
		engine.SetValue("script", new ScriptView(script.Name));
		engine.SetValue("__bv_print", new Action<string>(message => script.Root.ScriptService.Logger.LogInfo(script, message)));
		engine.SetValue("__bv_warn", new Action<string>(message => script.Root.ScriptService.Logger.LogWarning(script, message)));
		engine.SetValue("__bv_error", new Action<string>(message => script.Root.ScriptService.Logger.LogError(script, message)));
		engine.Execute(ConsoleBootstrap);
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
