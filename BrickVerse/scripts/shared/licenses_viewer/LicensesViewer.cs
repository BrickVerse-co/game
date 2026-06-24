// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;

namespace Polytoria.Shared;

public partial class LicensesViewer : Node
{
	private const string LicensesPath = "res://licenses/";
	[Export] public Tree TreeView = null!;
	[Export] public RichTextLabel ContentLabel = null!;

	private readonly Dictionary<string, string> _licenses = [];

	public override void _Ready()
	{
		TreeItem root = TreeView.CreateItem();
		TreeView.HideRoot = true;
		TreeView.ItemSelected += OnItemSelected;
		int i = 0;
		foreach (string item in DirAccess.GetFilesAt(LicensesPath))
		{
			string licenseName = item[..^4];
			string licenseData = FileAccess.GetFileAsString(LicensesPath.PathJoin(item));
			TreeItem child = root.CreateChild();
			child.SetText(0, licenseName);
			_licenses[licenseName] = licenseData;
			if (i == 0)
			{
				TreeView.SetSelected(child, 0);
			}
			i++;
		}
	}

	private void OnItemSelected()
	{
		TreeItem child = TreeView.GetSelected();
		string licenseData = _licenses[child.GetText(0)];
		ContentLabel.Text = licenseData;
	}
}
