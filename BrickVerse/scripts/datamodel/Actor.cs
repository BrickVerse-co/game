// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Datamodel.Interfaces;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;

namespace BrickVerse.Datamodel;

/// <summary>An isolated Luau execution domain and asynchronous message boundary.</summary>
[Instantiable]
public sealed partial class Actor : Instance, IGroup
{
	private readonly Dictionary<string, BVSignal> _messages = new(StringComparer.Ordinal);

	[ScriptMethod]
	public BVSignalConnection BindToMessage(string topic, BVCallback callback)
	{
		if (string.IsNullOrWhiteSpace(topic)) throw new ArgumentException("Actor message topic cannot be empty.", nameof(topic));
		return GetMessage(topic).Connect(callback);
	}

	[ScriptMethod]
	public void SendMessage(string topic, params object?[] arguments)
	{
		if (string.IsNullOrWhiteSpace(topic)) throw new ArgumentException("Actor message topic cannot be empty.", nameof(topic));
		if (!_messages.TryGetValue(topic, out BVSignal? signal)) return;
		BV.CallDeferred(() => signal.InvokeDirect(arguments));
	}

	private BVSignal GetMessage(string topic)
	{
		if (!_messages.TryGetValue(topic, out BVSignal? signal))
		{
			signal = new();
			_messages.Add(topic, signal);
		}
		return signal;
	}
}
