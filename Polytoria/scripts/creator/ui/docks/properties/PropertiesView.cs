// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Creator.UI;

public partial class PropertiesView : Control
{
	public VBoxContainer PropertiesContainer = null!;
	public InstanceTagView TagsView = null!;

	public override void _EnterTree()
	{
		PropertiesContainer = GetNode<VBoxContainer>("Properties/Margin/Container");
		TagsView = GetNode<InstanceTagView>("Tags");
		base._EnterTree();
	}
}
