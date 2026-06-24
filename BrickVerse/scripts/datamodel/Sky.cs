// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Abstract]
public partial class Sky : Instance
{
	public Material SkyMaterial { get; set; } = null!;

	public override void EnterTree()
	{
		base.EnterTree();
		Root.Lighting.ApplySky(this);
	}

	public override void Init()
	{
		base.Init();
		Root.Lighting.ApplySky(this);
	}

	public override void ExitTree()
	{
		base.ExitTree();
		Root.Lighting.RemoveSky(this);
	}
}
