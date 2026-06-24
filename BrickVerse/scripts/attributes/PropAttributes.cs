// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this property as ignored by the cleanup process
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IgnoreCleanupAttribute : Attribute { }

/// <summary>
/// Default value for this property
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DefaultValueAttribute(object? val) : Attribute
{
	public object? DefaultValue = val;
}
