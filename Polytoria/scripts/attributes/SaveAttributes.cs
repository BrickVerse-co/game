// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this property/class as ignored by save process
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class SaveIgnoreAttribute : Attribute { }

/// <summary>
/// Include this property when saving
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SaveIncludeAttribute : Attribute { }
