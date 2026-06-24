// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting;

namespace Polytoria.Datamodel.Data;

public partial class NewServerRequestData : IScriptObject
{
	[ScriptProperty] public string WorldPath { get; set; } = "";
	[ScriptProperty] public int MaxPlayers { get; set; } = 12;

	[ScriptMethod]
	public static NewServerRequestData New()
	{
		return new();
	}
}
