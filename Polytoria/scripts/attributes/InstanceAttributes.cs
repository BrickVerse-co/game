// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this class as instantiatable via Instance.New/scripts
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class InstantiableAttribute : Attribute { }

/// <summary>
/// Mark this class as internal class
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class InternalAttribute : Attribute { }

/// <summary>
/// Stop the root finding process
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PhysicalRootStopAttribute : Attribute { }
