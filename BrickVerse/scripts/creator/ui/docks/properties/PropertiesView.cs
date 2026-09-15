// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace BrickVerse.Creator.UI;

public partial class PropertiesView : Control
{
	public VBoxContainer PropertiesContainer = null!;
	public InstanceTagView TagsView = null!;
	private LineEdit _search = null!;
	private string _lastFilter = "";
	private int _lastChildCount = -1;

	public override void _EnterTree()
	{
		PropertiesContainer = GetNode<VBoxContainer>("Properties/Scroll/Margin/Container");
		_search = GetNode<LineEdit>("Properties/Search");
		_search.TextChanged += _ => ApplyPropertyFilter();
		TagsView = GetNode<InstanceTagView>("Tags");
		base._EnterTree();
	}

	public override void _Process(double delta)
	{
		// Property controls are rebuilt after selection changes. Reapply the active
		// filter when that happens without coupling the builder to this view.
		if (_lastChildCount != PropertiesContainer.GetChildCount()) ApplyPropertyFilter();
	}

	private void ApplyPropertyFilter()
	{
		string query = _search.Text.Trim();
		_lastFilter = query;
		_lastChildCount = PropertiesContainer.GetChildCount();
		Godot.Collections.Array<Node> children = PropertiesContainer.GetChildren();
		for (int i = 0; i < children.Count; i++)
		{
			if (children[i] is not PanelContainer header || i + 1 >= children.Count || children[i + 1] is not VBoxContainer fields) continue;
			bool visible = query.Length == 0 || ContainsLabel(header, query) || ContainsLabel(fields, query);
			header.Visible = visible;
			fields.Visible = visible;
		}
	}

	private static bool ContainsLabel(Node node, string query)
	{
		if (node is Label label && label.Text.Contains(query, System.StringComparison.OrdinalIgnoreCase)) return true;
		foreach (Node child in node.GetChildren()) if (ContainsLabel(child, query)) return true;
		return false;
	}
}
