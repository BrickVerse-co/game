// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;

namespace Polytoria.Client;

public partial class UIVersionText : Label
{
	public override void _Ready()
	{
		Text = Globals.AppVersion;
	}
}
