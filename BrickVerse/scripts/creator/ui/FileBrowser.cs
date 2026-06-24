// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Creator.UI;

public sealed partial class FileBrowser : TabContainer
{
	private const string FileBrowserTabPath = "res://scenes/creator/docks/filebrowser/file_tab.tscn";

	private static readonly Dictionary<CreatorSession, FileBrowserTab> SessionToBrowserTab = [];

	public static FileBrowser Singleton { get; private set; } = null!;
	public FileBrowser()
	{
		Singleton = this;
	}

	public static CreatorSession? CurrentSession { get; private set; }

	public void SwitchTo(CreatorSession? session)
	{
		CurrentSession = session;
		CurrentTab = session == null ? -1 : GetTabIdxFromControl(SessionToBrowserTab[session]);
	}

	public FileBrowserTab Insert(CreatorSession session)
	{
		FileBrowserTab browserTab = Globals.CreateInstanceFromScene<FileBrowserTab>(FileBrowserTabPath);
		browserTab.Session = session;
		AddChild(browserTab);
		SessionToBrowserTab[session] = browserTab;
		return browserTab;
	}
}
