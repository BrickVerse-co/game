// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this as obsolete
/// </summary>
/// <param name="message">The message regarding the obsolete reason</param>
[AttributeUsage(AttributeTargets.All)]
public sealed class ObsoleteAttribute(string message) : Attribute
{
	public string Message => message;
}
