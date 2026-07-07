// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.Settings;
using Polytoria.Datamodel;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.UI;

public partial class UIIndicators : Control
{
	private const int HighPingThreshold = 500;
	private World _root = null!;
	private readonly HashSet<Action> _actions = [];

	public override void _Ready()
	{
		_root = CoreUIRoot.Singleton.Root;
		UpdateVisible();

		LinkIndicator(GetNode<Control>("HighPing"), () =>
		{
			if (_root.Players.LocalPlayer == null) return false;
			return _root.Players.LocalPlayer.NetworkPing > HighPingThreshold;
		});

		LinkIndicator(GetNode<Control>("ServerUnderLoad"), () =>
		{
			return _root.ServerUnderLoad;
		});

		LinkIndicator(GetNode<Control>("Silence"), () =>
		{
			if (_root.Network.NetInstance == null) return false;
			return _root.Network.NetInstance.IsSilence;
		});

		MainUpdateLoop();
	}

	private void UpdateVisible()
	{
		bool to = ClientSettingsService.Instance.Get<bool>(ClientSettingKeys.Overlay.ConnectionIndicators);
		if (Visible != to)
			Visible = to;
	}

	private void LinkIndicator(Control target, Func<bool> func)
	{
		_actions.Add(() =>
		{
			target.Visible = func();
		});
	}

	private async void MainUpdateLoop()
	{
		UpdateAll();
		UpdateVisible();
		await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
		MainUpdateLoop();
	}

	private void UpdateAll()
	{
		foreach (Action item in _actions)
		{
			item();
		}
	}
}
