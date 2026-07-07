// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Scripting.Luau;
/// <summary>
/// Lua Load/Call status return
/// </summary>
public enum LuaStatus
{
	/// <summary>
	///  success
	/// </summary>
	OK = 0,
	/// <summary>
	/// Yield
	/// </summary>
	Yield = 1,
	/// <summary>
	/// a runtime error. 
	/// </summary>
	ErrRun = 2,
	/// <summary>
	/// syntax error during precompilation
	/// </summary>
	ErrSyntax = 3,
	/// <summary>
	///  memory allocation error. For such errors, Lua does not call the message handler. 
	/// </summary>
	ErrMem = 4,
	/// <summary>
	///  error while running the message handler. 
	/// </summary>
	ErrErr = 5,
	/// <summary>
	/// LUA_BREAK
	/// </summary>
	Break = 6,
}
