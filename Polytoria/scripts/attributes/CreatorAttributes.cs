// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Attributes;

/// <summary>
/// Enum options for creator
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public class CreatorEnumOptionsAttribute : Attribute
{
	public EnumSortOption SortOption = EnumSortOption.None;
}

public enum EnumSortOption
{
	None,
	Alphabetical
}
