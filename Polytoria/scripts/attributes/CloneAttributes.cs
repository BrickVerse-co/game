// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this property to be ignored when cloning
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CloneIgnoreAttribute : Attribute { }

/// <summary>
/// Mark this property to include when cloning
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CloneIncludeAttribute : Attribute { }
