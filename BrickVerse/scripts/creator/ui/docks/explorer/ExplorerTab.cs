// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Creator.UI;

public partial class ExplorerTab : Control
{
	public World Root = null!;
	[Export] public ExplorerTree Tree = null!;
	[Export] private LineEdit _searchEdit = null!;

	private bool _updateSearchDirty = false;

	public override void _Ready()
	{
		_searchEdit.TextChanged += OnSearch;
		base._Ready();
	}

	private void OnSearch(string newText)
	{
		QueueSearch();
	}

	private void QueueSearch()
	{
		_updateSearchDirty = true;
	}

	public override void _Process(double delta)
	{
		if (_updateSearchDirty)
		{
			_updateSearchDirty = false;
			Search();
		}
		base._Process(delta);
	}

	private void Search()
	{
		string query = _searchEdit.Text;
		bool isFirst = true;

		foreach (TreeItem item in Tree.InstanceToItem.Values)
		{
			if ((bool)item.GetMeta("_force_invisible", false)) continue;
			item.Deselect(0);
		}

		foreach ((Instance i, TreeItem item) in Tree.InstanceToItem)
		{
			if ((bool)item.GetMeta("_force_invisible", false)) continue;

			if (i.Name.Contains(query, System.StringComparison.CurrentCultureIgnoreCase))
			{
				item.Visible = true;
				RevealParents(item); // Always reveal parents

				if (isFirst)
				{
					isFirst = false;
					ExpandParents(item); // Only expand first match
					item.Select(0);
					Tree.ScrollToItem(item);
				}
			}
			else
			{
				item.Visible = false;
			}
		}

		if (string.IsNullOrEmpty(query))
		{
			foreach ((_, TreeItem item) in Tree.InstanceToItem)
			{
				Explorer.RefreshTreeItemVisibility(item);
			}
		}
	}

	private static void RevealParents(TreeItem item)
	{
		TreeItem parent = item.GetParent();
		while (parent != null)
		{
			if ((bool)parent.GetMeta("_force_invisible", false)) break;
			parent.Visible = true;
			parent = parent.GetParent();
		}
	}

	private static void ExpandParents(TreeItem item)
	{
		TreeItem parent = item.GetParent();
		while (parent != null)
		{
			parent.Collapsed = false;
			parent = parent.GetParent();
		}
	}
}
