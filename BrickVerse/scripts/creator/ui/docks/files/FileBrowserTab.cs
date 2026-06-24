// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using Polytoria.Utils;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Polytoria.Creator.UI;

public partial class FileBrowserTab : Control
{
	private const string FileBrowserIconsPath = "res://assets/textures/creator/filebrowser/icons/";
	[Export] public FileBrowserTree BrowserTree = null!;
	[Export] public LineEdit SearchEdit = null!;
	public HashSet<string> AutoSelects => BrowserTree.AutoSelects;
	public CreatorSession Session = null!;
	private readonly HashSet<string> _uncollapsedItems = [];

	private readonly string[] HiddenPaths = [
		".poly/",
		"lua_clue/",
		Globals.ProjectIndexName
	];

	private readonly string[] HiddenExtensions = [
		"meta",
	];

	public override void _Ready()
	{
		base._Ready();
		BrowserTree.Session = Session;
		BrowserTree.ItemCollapsed += OnItemCollapsed;
		SearchEdit.TextChanged += OnSearch;
	}

	private void OnSearch(string newText)
	{
		BrowserTree.Search(newText);
	}

	public void RenameSelected()
	{
		BrowserTree.EditSelected(true);
	}

	private void OnItemCollapsed(TreeItem item)
	{
		if (item.HasMeta("path"))
		{
			string path = (string)item.GetMeta("path", "");
			if (item.Collapsed)
			{
				_uncollapsedItems.Remove(path);
			}
			else
			{
				_uncollapsedItems.Add(path);
			}
		}
	}

	public void ScanRootFolder()
	{
		// Call in next frame for all file updates
		Callable.From(() =>
		{
			BrowserTree.Clear();
			BrowserTree.FileToItem.Clear();
			BrowserTree.ItemToFile.Clear();
			BrowserTree.SearchItems.Clear();
			RecurseFolders(Session.ProjectFolderPath, CreateItem(Session.ProjectFolderPath)!);
			AutoSelects.Clear();
			BrowserTree.Search(SearchEdit.Text);
		}).CallDeferred();
	}

	private void RecurseFolders(string path, TreeItem parent)
	{
		foreach (string p in Directory.GetDirectories(path))
		{
			string folderPath = p + "/";
			TreeItem? item = CreateItem(folderPath, parent);
			if (item != null)
			{
				RecurseFolders(folderPath, item);
			}
		}
		foreach (string p in Directory.GetFiles(path))
		{
			CreateItem(p, parent);
		}
	}

	private TreeItem? CreateItem(string path, TreeItem? parent = null)
	{
		bool isRoot = path == Session.ProjectFolderPath;
		path = path.SanitizePath();
		string ext = path.GetExtension();

		if (HiddenExtensions.Contains(ext)) return null;

		bool searchInclude = !isRoot;

		string relativePath = Path.GetRelativePath(Session.ProjectFolderPath, path).SanitizePath();

		if (HiddenPaths.Contains(relativePath)) return null;

		// ignore hiddens
		if (relativePath.StartsWith('.')) return null;

		TreeItem item = BrowserTree.CreateItem(parent);
		item.SetMeta("path", relativePath);

		if (isRoot)
		{
			item.Collapsed = false;
		}
		else
		{
			item.Collapsed = !_uncollapsedItems.Contains(relativePath);
		}

		bool isFolder = false;
		string title;

		// Is Folder
		if (Session.GetFileAttributes(relativePath) == FileAttributes.Directory)
		{
			title = new DirectoryInfo(path).Name;
			isFolder = true;
		}
		else
		{
			title = path.GetFile();
		}

		if (isRoot)
		{
			title = "Root";
		}

		item.SetText(0, title);

		Texture2D? icon;
		string iconPath;

		if (isFolder)
		{
			iconPath = "folder";
		}
		else
		{
			iconPath = path.GetExtension();
			if (!ResourceLoader.Exists(FileBrowserIconsPath.PathJoin(iconPath) + ".svg"))
			{
				iconPath = "unknown";
			}
		}

		if (relativePath == Globals.ProjectMetaFileName)
		{
			iconPath = "polytoria";
		}
		if (relativePath == Globals.ProjectInputMapName)
		{
			iconPath = "input.json";
		}

		icon = GD.Load<Texture2D>(FileBrowserIconsPath.PathJoin(iconPath) + ".svg");
		item.SetIcon(0, icon);
		item.SetIconMaxWidth(0, 24);

		if (BrowserTree.AutoSelects.Contains(relativePath))
		{
			item.Select(0);
			item.UncollapseTree();
		}

		if (searchInclude)
		{
			BrowserTree.SearchItems[relativePath] = item;
		}

		BrowserTree.FileToItem[relativePath] = item;
		BrowserTree.ItemToFile[item] = relativePath;

		return item;
	}

	private enum ItemType
	{
		Folder,
		File
	}
}
