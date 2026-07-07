// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using System;

namespace Polytoria.Scripting.Libraries;

public class LuaLibGUID : IScriptObject
{
	[ScriptMethod("new")]
	public static string New()
	{
		return Guid.NewGuid().ToString();
	}

	[ScriptMethod("empty")]
	public static string Empty()
	{
		return Guid.Empty.ToString();
	}
}
