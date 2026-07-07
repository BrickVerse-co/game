// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this method to handle lua state on it's own
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HandlesLuaStateAttribute : Attribute { }
