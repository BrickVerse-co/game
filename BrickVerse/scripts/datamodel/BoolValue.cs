// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class BoolValue : ValueBase
{
	private bool _val = false;

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool Value
	{
		get => _val;
		set
		{
			bool oldVal = _val;
			_val = value;
			if (_val != oldVal)
			{
				InvokeChanged();
			}
			OnPropertyChanged();
		}
	}
}
