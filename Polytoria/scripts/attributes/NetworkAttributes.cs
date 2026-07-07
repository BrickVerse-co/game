// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Networking;
using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark this property to be synchronized by the network, note that all value with EditableAttribute are synchronized by default
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SyncVarAttribute : Attribute
{
	/// <summary>
	/// Allow write from authority without checking NetPropAuthority
	/// </summary>
	public bool AllowAuthorWrite = false;
	/// <summary>
	/// Allow write from server only
	/// </summary>
	public bool ServerOnly = false;
	/// <summary>
	/// Set to sync to server unreliably, this will also skip the duplicate value check. This only affect client to server, not server to client
	/// </summary>
	public bool Unreliable = false;
}

/// <summary>
/// Mark this class/property not to be synchronized
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class NoSyncAttribute : Attribute { }

/// <summary>
/// Mark this method as callable via network RPC
/// </summary>
/// <param name="AuthorMode"></param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NetRpcAttribute(AuthorityMode AuthorMode) : Attribute
{
	/// <summary>
	/// Authority mode
	/// </summary>
	public AuthorityMode AuthorMode = AuthorMode;
	/// <summary>
	/// Should this RPC call locally too
	/// </summary>
	public bool CallLocal = false;
	/// <summary>
	/// Transfer mode for this NetRpc
	/// </summary>
	public TransferMode TransferMode = TransferMode.Reliable;
	/// <summary>
	/// Determine which channel to use for transferring message via this Rpc
	/// </summary>
	public int TransferChannel = 0;
	/// <summary>
	/// Only allow this RPC to be called from server, used with any broadcast to everyone
	/// </summary>
	public bool AllowToServerOnly = true;
}
