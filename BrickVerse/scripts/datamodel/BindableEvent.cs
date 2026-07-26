// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Scripting;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class BindableEvent : Instance
{
	[ScriptProperty] public BVSignal Invoked { get; private set; } = new();

	[ScriptMethod]
	public void Invoke(params object?[] par)
	{
		Invoked.InvokeDirect(par);
	}
}
