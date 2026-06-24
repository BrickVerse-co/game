// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Creator.Properties;

public interface IProperty
{
	event Action<object?>? ValueChanged;
	Type PropertyType { get; set; }

	object? GetValue();
	void SetValue(object? value);
}

public interface IProperty<T> : IProperty
{
	T Value { get; set; }

	void Refresh();
}
