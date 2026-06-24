// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting;

namespace Polytoria.Datamodel;

[Abstract]
public partial class ValueBase : Instance
{
	[ScriptProperty]
	public PTSignal Changed { get; private set; } = new();

	protected void InvokeChanged()
	{
		Changed.Invoke();
	}
}
