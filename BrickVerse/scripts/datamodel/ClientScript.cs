// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class ClientScript : Script
{
	private bool _listeningReady = false;

	public override void EnterTree()
	{
		CheckSource();
		base.EnterTree();
	}

	public override void PreDelete()
	{
		if (_listeningReady)
		{
			_listeningReady = false;
			Root.ClientScriptRunDispatch -= OnRunDispatch;
		}
		base.PreDelete();
	}

	internal void CheckSource()
	{
		if (!Root.Network.IsServer)
		{
			if (!Root.IsLoaded)
			{
				if (!_listeningReady)
				{
					_listeningReady = true;
					Root.ClientScriptRunDispatch += OnRunDispatch;
				}
			}
			else if (Source == "")
			{
				RequestSource();
			}
		}
	}

	private void OnRunDispatch()
	{
		Root.ClientScriptRunDispatch -= OnRunDispatch;
		_listeningReady = false;
		TryRun();
	}

	private void RequestSource()
	{
		Root.Network.ScriptSync.RequestSource(this);
	}
}
