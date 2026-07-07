// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Static("Teams")]
public sealed partial class Teams : Instance
{
	public PTSignal<Team> TeamAdded { get; private set; } = new();
	public PTSignal<Team> TeamRemoved { get; private set; } = new();
	public event Action? TeamUpdateDispatch;

	public override void Init()
	{
		ChildAdded.Connect(OnChildAdded);
		ChildRemoved.Connect(OnChildRemoved);
		base.Init();
	}

	internal void DispatchTeamUpdate()
	{
		TeamUpdateDispatch?.Invoke();
	}

	[ScriptMethod]
	public Team[] GetTeams()
	{
		List<Team> teams = [];

		foreach (Instance item in GetChildren())
		{
			if (item is Team t)
			{
				teams.Add(t);
			}
		}

		return [.. teams];
	}

	private void OnChildAdded(Instance instance)
	{
		if (instance is Team team)
		{
			TeamAdded.Invoke(team);
		}
	}

	private void OnChildRemoved(Instance instance)
	{
		if (instance is Team team)
		{
			TeamRemoved.Invoke(team);
		}
	}
}
