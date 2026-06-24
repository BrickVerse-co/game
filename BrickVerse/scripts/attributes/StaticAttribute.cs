// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this class as static class, can be accessed via script by it's `alias`. This will also make this class not reparentable and it's name cannot be changed.
/// </summary>
/// <param name="alias"></param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class StaticAttribute(string? alias = null) : Attribute
{
	public string? Alias = alias;
}
