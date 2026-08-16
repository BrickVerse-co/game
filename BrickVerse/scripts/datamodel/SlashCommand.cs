using BrickVerse.Attributes;
using BrickVerse.Scripting;
using System.Collections.Generic;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class SlashCommand : Instance
{
	private readonly HashSet<string> _allowedUsers = [];
	[Editable, ScriptProperty] public string Prefix { get; set; } = "/";
	[Editable, ScriptProperty] public string Description { get; set; } = "";
	[Editable, ScriptProperty] public string Usage { get; set; } = "";
	[Editable, ScriptProperty] public bool Restricted { get; set; }
	[Editable, ScriptProperty] public bool LocalCommand { get; set; }
	[ScriptProperty] public BVSignal<Player, string> Executed { get; private set; } = new();

	[ScriptMethod] public void GrantAccess(Player player) { if ((HasAuthority || LocalCommand) && player != null) _allowedUsers.Add(player.UserID); }
	[ScriptMethod] public void RevokeAccess(Player player) { if ((HasAuthority || LocalCommand) && player != null) _allowedUsers.Remove(player.UserID); }
	[ScriptMethod] public bool HasAccess(Player player) => player != null && (!Restricted || player.IsAdmin || _allowedUsers.Contains(player.UserID));
	internal void Invoke(Player player, string arguments) => Executed.Invoke(player, arguments);
}
