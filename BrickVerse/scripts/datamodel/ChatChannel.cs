using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System.Collections.Generic;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class ChatChannel : Instance
{
	private readonly HashSet<string> _allowedUsers = [];
	[Editable, ScriptProperty, DefaultValue(true)] public bool AutoJoin { get; set; } = true;
	[Editable, ScriptProperty] public bool Restricted { get; set; }
	[Editable, ScriptProperty] public bool TeamOnly { get; set; }
	[Editable, ScriptProperty] public string Prefix { get; set; } = "";

	[ScriptMethod] public void GrantAccess(Player player) { if (HasAuthority && player != null) _allowedUsers.Add(player.UserID); }
	[ScriptMethod] public void RevokeAccess(Player player) { if (HasAuthority && player != null) _allowedUsers.Remove(player.UserID); }
	[ScriptMethod] public bool HasAccess(Player player) => player != null && (!Restricted || player.IsAdmin || _allowedUsers.Contains(player.UserID));
}
