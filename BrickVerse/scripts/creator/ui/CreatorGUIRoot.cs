// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.Utils;
using System.Collections.Generic;

namespace BrickVerse.Creator.UI;

public sealed partial class CreatorGUIRoot : Control
{
	public static CreatorGUIRoot Singleton { get; private set; } = null!;
	private readonly HashSet<ulong> _soundHookedNodes = [];
	public CreatorGUIRoot()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		HookSoundFeedback(this, playModalOpen: false);
		GetTree().NodeAdded += OnNodeAdded;
	}

	private void OnNodeAdded(Node node) => HookSoundFeedback(node, playModalOpen: true);

	private void HookSoundFeedback(Node node, bool playModalOpen)
	{
		if (!_soundHookedNodes.Add(node.GetInstanceId())) return;
		if (node is BaseButton button)
		{
			button.MouseEntered += CreatorSoundEffects.PlayUiHover;
			button.Pressed += CreatorSoundEffects.PlayUiClick;
		}
		else if (node is Window window && node is not Popup)
		{
			bool wasVisible = window.Visible;
			if (playModalOpen && wasVisible) CreatorSoundEffects.PlayModalOpen();
			window.VisibilityChanged += () =>
			{
				bool isVisible = window.Visible;
				if (isVisible == wasVisible) return;
				if (isVisible) CreatorSoundEffects.PlayModalOpen();
				else CreatorSoundEffects.PlayModalClose();
				wasVisible = isVisible;
			};
			node.TreeExiting += () => { if (wasVisible) CreatorSoundEffects.PlayModalClose(); };
		}
		foreach (Node child in node.GetChildren()) HookSoundFeedback(child, playModalOpen: false);
	}

	public override void _ExitTree()
	{
		if (GetTree() != null) GetTree().NodeAdded -= OnNodeAdded;
		base._ExitTree();
	}
}
