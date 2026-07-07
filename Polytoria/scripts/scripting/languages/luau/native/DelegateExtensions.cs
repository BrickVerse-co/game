// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Runtime.InteropServices;

namespace Polytoria.Scripting.Luau;

static class DelegateExtensions
{
	public static LuaFunction? ToLuaFunction(this IntPtr ptr)
	{
		if (ptr == IntPtr.Zero)
			return null;

		return Marshal.GetDelegateForFunctionPointer<LuaFunction>(ptr);
	}

	public static IntPtr ToFunctionPointer(this LuaFunction d)
	{
		if (d == null)
			return IntPtr.Zero;

		return Marshal.GetFunctionPointerForDelegate<LuaFunction>(d);
	}
}
