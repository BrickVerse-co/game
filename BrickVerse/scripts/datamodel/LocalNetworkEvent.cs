// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel;

/// <summary>
/// An in-process event/function channel. Unlike NetworkEvent, LocalNetworkEvent
/// never serializes payloads or crosses the server/client connection; each
/// runtime receives and invokes its own local instance.
/// </summary>
[Instantiable]
public sealed partial class LocalNetworkEvent : NetworkEvent
{
	/// <summary>Raised synchronously in the current runtime when Fire is called.</summary>
	[ScriptProperty] public BVSignal Invoked { get; private set; } = new();

	/// <summary>Optional callback used when this instance is invoked as a function.</summary>
	[ScriptProperty] public BVFunction? Callback { get; set; }

	/// <summary>Raises the local event with the supplied arguments.</summary>
	[ScriptMethod]
	public void Fire(params object?[] args) => Invoked.InvokeDirect(args ?? []);

	/// <summary>Alias matching the existing BindableEvent API.</summary>
	[ScriptMethod]
	public void Invoke(params object?[] args) => Fire(args);

	/// <summary>Calls Callback locally and returns all callback results.</summary>
	[ScriptMethod]
	public async Task<object?[]> InvokeFunction(params object?[] args)
	{
		if (Callback == null)
			throw new System.InvalidOperationException($"LocalNetworkEvent {LuaPath} has no Callback.");
		return await Callback.Call(args ?? []);
	}
}
