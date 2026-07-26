// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Script = BrickVerse.Datamodel.Script;

namespace BrickVerse.Scripting;

public interface IScriptLanguageProvider : IDisposable
{
	void Run(Script script);
	void Close(Script script);
	byte[] CompileSource(string source);
	Task CallAsync(Script script, string funcName, object?[]? args);
	void CallUpdate(Script script, double delta);
	void CallFixedUpdate(Script script, double delta);
	void FreeBVCallback(BVCallback callback);
}

public enum ScriptLanguagesEnum
{
	Luau
}
