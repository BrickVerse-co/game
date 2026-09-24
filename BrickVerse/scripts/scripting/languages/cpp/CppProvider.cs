// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BrickVerse.Scripting.Managed;
using Wasmtime;
using Script = BrickVerse.Datamodel.Script;

namespace BrickVerse.Scripting.Cpp;

public sealed class CppProvider : IScriptLanguageProvider
{
	private const string Header = """
#pragma once
#include <stdint.h>
#define BV_IMPORT(name) __attribute__((import_module("brickverse"), import_name(name)))
#define BV_EXPORT(name) extern "C" __attribute__((export_name(name)))
extern "C" { int32_t bv_global(const char*, int32_t) BV_IMPORT("global");
double bv_get_f64(int32_t,const char*,int32_t) BV_IMPORT("get_f64");
void bv_set_f64(int32_t,const char*,int32_t,double) BV_IMPORT("set_f64");
double bv_call0(int32_t,const char*,int32_t) BV_IMPORT("call0");
void bv_log(const char*,int32_t) BV_IMPORT("log"); }
namespace brickverse {
inline int global(const char* n,int l){return bv_global(n,l);} inline double get(int h,const char* n,int l){return bv_get_f64(h,n,l);}
inline void set(int h,const char* n,int l,double v){bv_set_f64(h,n,l,v);} inline double call(int h,const char* n,int l){return bv_call0(h,n,l);}
inline void log(const char* s,int l){bv_log(s,l);} }
""";

	private readonly Config _config = new Config().WithFuelConsumption(true).WithMaximumStackSize(512 * 1024);
	private readonly Engine _engine;
	private readonly Dictionary<Script, State> _states = [];
	public CppProvider() => _engine = new Engine(_config);

	public byte[] CompileSource(string source)
	{
		string root = Path.Combine(Path.GetTempPath(), "brickverse-cpp-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			string sourcePath = Path.Combine(root, "script.cpp");
			string headerPath = Path.Combine(root, "brickverse.hpp");
			string outputPath = Path.Combine(root, "script.wasm");
			File.WriteAllText(sourcePath, source);
			File.WriteAllText(headerPath, Header);
			string compiler = Environment.GetEnvironmentVariable("BV_WASI_CLANG") ?? "clang++";
			using Process process = new() { StartInfo = new ProcessStartInfo { FileName = compiler,
				RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
			foreach (string argument in new[] { "--target=wasm32", "-std=c++20", "-O2", "-fno-exceptions", "-fno-rtti",
				"-nostdlib", "-Wl,--no-entry", "-Wl,--export-memory", "-I", root, sourcePath, "-o", outputPath })
				process.StartInfo.ArgumentList.Add(argument);
			try { process.Start(); }
			catch (Exception exception) { throw new InvalidOperationException("C++ scripting requires clang++ with the WebAssembly target in PATH, or BV_WASI_CLANG", exception); }
			string error = process.StandardError.ReadToEnd();
			if (!process.WaitForExit(30_000)) { process.Kill(true); throw new TimeoutException("C++ compilation timed out"); }
			if (process.ExitCode != 0) throw new InvalidOperationException("C++ compilation failed: " + error.Trim());
			return File.ReadAllBytes(outputPath);
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}

	public void Run(Script script)
	{
		try
		{
			script.TryCompile();
			ScriptCapabilityBridge api = new(script);
			using Module module = Module.FromBytes(_engine, script.Name, script.Bytecode!);
			Store store = new(_engine) { Fuel = 5_000_000 };
			store.SetLimits(memorySize: 16 * 1024 * 1024, tableElements: 10_000, instances: 1, tables: 4, memories: 1);
			Linker linker = new(_engine);
			linker.Define("brickverse", "global", Function.FromCallback(store, (Caller caller, int p, int n) => api.Global(Read(caller, p, n))));
			linker.Define("brickverse", "get_f64", Function.FromCallback(store, (Caller caller, int h, int p, int n) => Convert.ToDouble(api.Get(h, Read(caller, p, n)), CultureInfo.InvariantCulture)));
			linker.Define("brickverse", "set_f64", Function.FromCallback(store, (Caller caller, int h, int p, int n, double v) => api.Set(h, Read(caller, p, n), v)));
			linker.Define("brickverse", "call0", Function.FromCallback(store, (Caller caller, int h, int p, int n) => Convert.ToDouble(api.Call(h, Read(caller, p, n)), CultureInfo.InvariantCulture)));
			linker.Define("brickverse", "log", Function.FromCallback(store, (Caller caller, int p, int n) => script.Root.ScriptService.Logger.LogInfo(script, Read(caller, p, n))));
			Instance instance = linker.Instantiate(store, module);
			State state = new(store, linker, instance);
			_states[script] = state;
			Invoke(state, "bv_start");
		}
		catch (Exception exception) { script.Root.ScriptService.Logger.LogError(script, exception.InnerException?.Message ?? exception.Message, 0); }
	}

	private static string Read(Caller caller, int pointer, int length)
	{
		if (length < 0 || length > 16_384 || !caller.TryGetMemorySpan<byte>("memory", pointer, length, out Span<byte> span))
			throw new InvalidOperationException("Guest supplied an invalid string range");
		return Encoding.UTF8.GetString(span);
	}
	private static void Invoke(State state, string name) { state.Store.Fuel = 5_000_000; state.Instance.GetAction(name)?.Invoke(); }
	private static void Invoke(State state, string name, double value) { state.Store.Fuel = 5_000_000; state.Instance.GetAction<double>(name)?.Invoke(value); }
	public Task CallAsync(Script script, string funcName, object?[]? args) { if ((args?.Length ?? 0) == 0) Execute(script, s => Invoke(s, funcName)); return Task.CompletedTask; }
	public void CallUpdate(Script script, double delta) => Execute(script, s => Invoke(s, "bv_update", delta));
	public void CallFixedUpdate(Script script, double delta) => Execute(script, s => Invoke(s, "bv_fixed_update", delta));
	private void Execute(Script script, Action<State> action)
	{
		if (!_states.TryGetValue(script, out State? state)) return;
		try { action(state); }
		catch (Exception exception) { script.Root.ScriptService.Logger.LogError(script, exception.InnerException?.Message ?? exception.Message, 0); }
	}
	public void Close(Script script) { if (_states.Remove(script, out State? s)) s.Dispose(); }
	public void FreeBVCallback(BVCallback callback) { }
	public void Dispose() { foreach (Script script in new List<Script>(_states.Keys)) Close(script); _engine.Dispose(); _config.Dispose(); }
	private sealed record State(Store Store, Linker Linker, Instance Instance) : IDisposable { public void Dispose() { Linker.Dispose(); Store.Dispose(); } }
}
