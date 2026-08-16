// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Client.WebAPI;
using BrickVerse.Networking;
using BrickVerse.Networking.RateLimiters;
using BrickVerse.Schemas.API;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel.Services;

[Static("Chat")]
public sealed partial class ChatService : Instance
{
	private const int AllowedMessagePerWindow = 5;
	private const int AllowedMessageSecondsWindow = 5;
	private const int MaxMsgContentLength = 200;

	/// <summary>
	/// Fire when there's new chat message from player
	/// </summary>
	[ScriptProperty]
	public BVSignal<Player, string> NewChatMessage { get; private set; } = new();

	/// <summary>
	/// Fire when there's new message from broadcast/unicast
	/// </summary>
	[ScriptProperty]
	public BVSignal<string> MessageReceived { get; private set; } = new();

	/// <summary>
	/// Fire when the sent message is declined by the server
	/// </summary>
	[ScriptProperty]
	public BVSignal MessageDeclined { get; private set; } = new();

	/// <summary>
	/// Predicate function to determine if this message should be sent or not
	/// </summary>
	[ScriptProperty]
	public BVFunction? ChatPredicate { get; set; }

	private readonly BVHttpClient _client = new();

	private static readonly Dictionary<string, string> _builtInEmojis = [];
	public static IReadOnlyDictionary<string, string> BuiltInEmojis => _builtInEmojis;
	private const string EmojisPath = "res://assets/textures/client/emojis/";

	private readonly Dictionary<Player, SlidingWindowRateLimiter> _playerToRateLimiter = [];

	static ChatService()
	{
		if (!Globals.GDAvailable) return;
		foreach (string emojiFile in ResourceLoader.ListDirectory(EmojisPath))
		{
			string path = EmojisPath.PathJoin(emojiFile);
			if (ResourceLoader.Exists(path))
			{
				_builtInEmojis.Add(emojiFile[..^4], path);
			}
		}
	}

	public override void Ready()
	{
		Root.Players.PlayerRemoved.Connect(OnPlayerRemoved);
		if (HasAuthority)
		{
			EnsureChannel("Global", autoJoin: true, teamOnly: false);
			EnsureChannel("Team", autoJoin: false, teamOnly: true);
		}
		base.Ready();
	}

	private void EnsureChannel(string name, bool autoJoin, bool teamOnly)
	{
		if (GetChildrenOfClass<ChatChannel>().Any(channel => channel.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
		ChatChannel channel = Globals.LoadInstance<ChatChannel>(Root); channel.Name = name; channel.AutoJoin = autoJoin; channel.TeamOnly = teamOnly; channel.Parent = this;
	}

	public IEnumerable<string> GetCommandSuggestions(string partial)
	{
		string value = partial.TrimStart();
		string[] builtIns = ["/w", "/whisper", "/team", "/t", "/channel"];
		return builtIns.Concat(GetChildrenOfClass<SlashCommand>().Select(command => command.Prefix + command.Name))
			.Where(command => command.StartsWith(value, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(command => command);
	}

	public bool TryExecuteLocalCommand(string text)
	{
		Player player = Root.Players.LocalPlayer;
		foreach (SlashCommand command in GetChildrenOfClass<SlashCommand>().Where(command => command.LocalCommand))
		{
			string token = command.Prefix + command.Name;
			if (!text.Equals(token, StringComparison.OrdinalIgnoreCase) && !text.StartsWith(token + " ", StringComparison.OrdinalIgnoreCase)) continue;
			if (!command.HasAccess(player)) { MessageReceived.Invoke("[!] You do not have access to that command."); return true; }
			command.Invoke(player, text.Length > token.Length ? text[(token.Length + 1)..] : ""); return true;
		}
		return false;
	}

	private void OnPlayerRemoved(Player plr)
	{
		_playerToRateLimiter.Remove(plr);
	}

	public void SendChatMessage(string msgContent)
	{
		if (msgContent.Length > MaxMsgContentLength)
		{
			// Exceeded the maximum message content length
			NetMessageDeclined();
			BroadcastMessage($"[!] Your chat message is too long");
			return;
		}

		RpcId(1, nameof(NetServerRecvChatMessage), msgContent);
	}

	public void SendQuickChat(int phraseIndex)
	{
		if (phraseIndex < 0 || phraseIndex >= QuickChatCatalog.Phrases.Count) return;
		RpcId(1, nameof(NetServerRecvQuickChat), phraseIndex);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, TransferChannel = 2)]
	private async void NetServerRecvQuickChat(int phraseIndex)
	{
		Player? player = Root.Players.GetPlayerFromPeerID(RemoteSenderId);
		if (player == null || !player.CanQuickChat || phraseIndex < 0 || phraseIndex >= QuickChatCatalog.Phrases.Count) return;
		if (!_playerToRateLimiter.TryGetValue(player, out SlidingWindowRateLimiter? limiter)) _playerToRateLimiter[player] = limiter = new(3, TimeSpan.FromSeconds(5));
		if (!limiter.TryAccept()) return;
		string phrase = QuickChatCatalog.Phrases[phraseIndex]; _ = LogChatMessageAsync(player.UserID, $"[QuickChat] {phrase}"); await RouteMessage(player, phrase, GetChannel("Global"), null, quickChat: true);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, TransferChannel = 2)]
	private async void NetServerRecvChatMessage(string msgContent)
	{
		int peerID = RemoteSenderId;
		Player? player = Root.Players.GetPlayerFromPeerID(peerID);

		// Check CanChat / Age restricted limitations
		if (player != null && (!player.CanChat || player.IsAgeRestricted))
		{
			RpcId(peerID, nameof(NetMessageDeclined));
			return;
		}

		// Filter message
		string filteredContent = player != null && player.IsAdmin ? msgContent : FilterService.Filter(msgContent);

		if (player != null)
		{
			if (!player.IsAdmin)
			{
				// Escape BBCode
				filteredContent = filteredContent.Replace("[", "[lb]");
			}

			if (filteredContent.Length > MaxMsgContentLength)
			{
				// Exceeded the maximum message content length
				RpcId(peerID, nameof(NetMessageDeclined));
				UnicastMessage($"[!] Your chat message is too long", player);
				return;
			}

			if (!_playerToRateLimiter.TryGetValue(player, out var rateLimit))
			{
				_playerToRateLimiter[player] = new(AllowedMessagePerWindow, TimeSpan.FromSeconds(AllowedMessageSecondsWindow));
				rateLimit = _playerToRateLimiter[player];
			}

			if (!rateLimit.TryAccept())
			{
				// Rate limited
				RpcId(peerID, nameof(NetMessageDeclined));
				UnicastMessage($"[!] You need to cool off! Wait {AllowedMessageSecondsWindow} seconds before sending another message", player);
				return;
			}

			if (ChatPredicate != null)
			{
				// Handle ChatPredicate
				object?[] res = await ChatPredicate.Call(player, filteredContent);
				if (res.Length > 0 && res[0] is bool b && !b)
				{
					RpcId(peerID, nameof(NetMessageDeclined));
					return;
				}
			}

			if (await TryRunServerCommand(player, filteredContent)) return;

			// Log chat message
			_ = LogChatMessageAsync(player.UserID, filteredContent);
			await RouteParsedMessage(player, filteredContent);
		}
	}

	private ChatChannel? GetChannel(string name) => GetChildrenOfClass<ChatChannel>().FirstOrDefault(channel => channel.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

	private async Task<bool> TryRunServerCommand(Player player, string text)
	{
		foreach (SlashCommand command in GetChildrenOfClass<SlashCommand>().Where(command => !command.LocalCommand))
		{
			string token = command.Prefix + command.Name;
			if (!text.Equals(token, StringComparison.OrdinalIgnoreCase) && !text.StartsWith(token + " ", StringComparison.OrdinalIgnoreCase)) continue;
			if (!command.HasAccess(player)) UnicastMessage("[!] You do not have access to that command.", player);
			else command.Invoke(player, text.Length > token.Length ? text[(token.Length + 1)..] : "");
			return true;
		}
		await Task.CompletedTask; return false;
	}

	private async Task RouteParsedMessage(Player sender, string message)
	{
		ChatChannel? channel = GetChannel("Global"); Player? whisperTarget = null;
		string[] pieces = message.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
		if (pieces.Length > 0 && (pieces[0].Equals("/w", StringComparison.OrdinalIgnoreCase) || pieces[0].Equals("/whisper", StringComparison.OrdinalIgnoreCase)))
		{
			if (pieces.Length < 3 || (whisperTarget = Root.Players.GetChildrenOfClass<Player>().FirstOrDefault(player => player.Name.Equals(pieces[1], StringComparison.OrdinalIgnoreCase))) == null) { Decline(sender, "[!] Usage: /w username message"); return; }
			message = pieces[2]; channel = null;
		}
		else if (pieces.Length > 0 && (pieces[0].Equals("/t", StringComparison.OrdinalIgnoreCase) || pieces[0].Equals("/team", StringComparison.OrdinalIgnoreCase)))
		{
			message = message[(pieces[0].Length)..].Trim(); channel = GetChannel("Team");
			if (channel == null) { Decline(sender, "[!] Team chat is unavailable."); return; }
		}
		else if (pieces.Length > 1 && pieces[0].Equals("/channel", StringComparison.OrdinalIgnoreCase))
		{
			channel = GetChannel(pieces[1]); message = pieces.Length == 3 ? pieces[2] : "";
			if (channel == null) { Decline(sender, "[!] Unknown chat channel."); return; }
		}
		if (string.IsNullOrWhiteSpace(message)) return;
		await RouteMessage(sender, message, channel, whisperTarget, quickChat: false);
	}

	private async Task RouteMessage(Player sender, string message, ChatChannel? channel, Player? whisperTarget, bool quickChat)
	{
		if (channel != null && !channel.HasAccess(sender)) { Decline(sender, "[!] You do not have access to that channel."); return; }
		if (channel?.TeamOnly == true && sender.Team == null) { Decline(sender, "[!] Join a team before using team chat."); return; }
		IEnumerable<Player> recipients = whisperTarget != null ? [sender, whisperTarget] : Root.Players.GetChildrenOfClass<Player>();
		if (channel?.TeamOnly == true) recipients = recipients.Where(player => player.Team != null && player.Team == sender.Team);
		string prefix = whisperTarget != null ? $"[Whisper: {sender.Name} → {whisperTarget.Name}] " : channel != null && channel.Name != "Global" ? $"[{channel.Name}] " : "";
		foreach (Player recipient in recipients.Distinct())
		{
			if (recipient != sender && !sender.IsAdmin && await Root.Social.WebIsBlockedEitherWay(sender.UserID, recipient.UserID)) continue;
			RpcId(recipient.PeerID, nameof(NetRecvChatMessage), sender.UserID, prefix + message);
		}
		BV.Print(sender.Name, " [", whisperTarget != null ? "Whisper" : channel?.Name ?? "Private", "]: ", message);
	}

	private void Decline(Player sender, string reason) { RpcId(sender.PeerID, nameof(NetMessageDeclined)); UnicastMessage(reason, sender); }

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable, CallLocal = true, TransferChannel = 2)]
	private void NetRecvChatMessage(string userID, string msgContent)
	{
		Player? player = Root.Players.GetPlayerByID(userID);

		if (player != null)
		{
			string formatted = FormatEmojis(msgContent);
			NewChatMessage.Invoke(player, formatted);
			player.InvokeChatted(formatted);
		}
		else
		{
			BV.PrintWarn(userID, " not found in chat");
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable, TransferChannel = 2)]
	private void NetMessageDeclined()
	{
		MessageDeclined.Invoke();
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable, TransferChannel = 2)]
	private void NetRecvBroadcastMessage(string msgContent)
	{
		MessageReceived.Invoke(FormatEmojis(msgContent));
	}

	[ScriptMethod]
	public void BroadcastMessage(string msg)
	{
		string formatted = FormatEmojis(msg);
		MessageReceived.Invoke(formatted);
		if (HasAuthority)
			Rpc(nameof(NetRecvBroadcastMessage), formatted);
	}

	[ScriptMethod]
	public void UnicastMessage(string msg, Player plr)
	{
		string formatted = FormatEmojis(msg);
		if (plr == Root.Players.LocalPlayer)
		{
			MessageReceived.Invoke(formatted);
		}
		else
		{
			RpcId(plr.PeerID, nameof(NetRecvBroadcastMessage), formatted);
		}
	}

	[ScriptLegacyMethod("BroadcastMessage")]
	public void LegacyBroadcastMessage(string msg, object? _ = null)
	{
		BroadcastMessage(msg);
	}

	[ScriptLegacyMethod("UnicastMessage")]
	public void LegacyUnicastMessage(string msg, Player plr)
	{
		UnicastMessage(msg, plr);
	}

	private static readonly Regex _emojiRegex = new(@":([^:\s]+):", RegexOptions.Compiled);

	public static string FormatEmojis(string msg, float scale = 1f)
	{
		int size = Mathf.RoundToInt(24 * scale);
		return _emojiRegex.Replace(msg, match =>
		{
			string name = match.Groups[1].Value;
			if (_builtInEmojis.TryGetValue(name, out string? path))
				return $"[img={size}x{size}]{path}[/img]";
			return match.Value;
		});
	}

	// Logging for moderation
	private async Task LogChatMessageAsync(string userID, string message)
	{
		if (Root.IsLocalTest) return;

		_client.DefaultRequestHeaders["Authorization"] = ServerAPI.GetAuthorizationHeaderValue();

		try
		{
			await _client.PostAsJsonAsync(
				Globals.ApiEndpoint.PathJoin("/v3/world/server/chat/filter"),
				new APIChatFilterRequest
				{
					UserId = userID,
					Message = message,
				},
				APIGenerationContext.Default.APIChatFilterRequest
			);
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Chat Logging Error: {ex.Message}");
		}
	}
}
