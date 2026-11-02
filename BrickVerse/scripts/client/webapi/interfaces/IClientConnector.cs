// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using BrickVerse.Schemas.API;

namespace BrickVerse.Client.WebAPI.Interfaces;

internal interface IClientConnector
{
	void SetToken(string token);
	Task<APIServerStatus> CheckServerStatus();
	Task<APIClientAuthResponseMessage> Connect();
}
