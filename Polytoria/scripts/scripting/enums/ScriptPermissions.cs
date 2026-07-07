// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.Json.Serialization;

namespace Polytoria.Scripting;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<ScriptPermissionFlags>))]
public enum ScriptPermissionFlags
{
	None,
	IORead,
	IOWrite,
	CreatorAccess,
	ContextAccess
}
