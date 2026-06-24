// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class ServerScript : Script
{
	public override void Init()
	{
		base.Init();
		if (!Root.Network.IsServer) return;
		if (Root.IsLoaded)
		{
			OnGameReady();
		}
		else
		{
			Root.Loaded.Once(OnGameReady);
		}
	}

	private void OnGameReady()
	{
		if (!Root.Network.IsServer) return;
		TryRun();
	}
}
