// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Client.UI;
using BrickVerse.Client.UI.Purchases;
using BrickVerse.Client.WebAPI;
using BrickVerse.Networking;
using BrickVerse.Schemas.API;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel.Services;

[Static("Purchases"), ExplorerExclude, SaveIgnore]
public sealed partial class PurchasesService : Instance
{
	private readonly BVHttpClient _client = new();
	private readonly Dictionary<string, PurchaseRequest> _pendingPurchases = [];
	private readonly HashSet<Player> _pendingPlayers = [];
	private string _currentPurchaseRef = "";
	private UIPurchasePrompt? _purchasePrompt;

	public override void Init()
	{
		if (Root.IsLoaded)
		{
			OnGameReady();
		}
		else
		{
			Root.Loaded.Once(OnGameReady);
		}

		SetProcess(true);
		base.Init();
	}

	private async void OnGameReady()
	{
		if (Root == null || Root.CoreUI == null) return;
		CoreUIRoot root = await Root.CoreUI.WaitRoot();
		_purchasePrompt = root.PurchasePrompt;
		_purchasePrompt?.Requested += OnPurchasePromptRequested;
	}

	public override void PreDelete()
	{
		_purchasePrompt?.Requested -= OnPurchasePromptRequested;
		base.PreDelete();
	}

	private async void OnPurchasePromptRequested(bool accepted)
	{
		bool success = false;
		if (accepted)
		{
			if (!Root.Network.IsProd || Root.IsLocalTest)
			{
				SendPurchaseRes(true);
				return;
			}
			try
			{
				_client.DefaultRequestHeaders["Authorization"] =
					$"Bearer {ClientAuthAPI.JoinToken}";
				using HttpResponseMessage response = await _client.PostAsync(
					Globals.ApiEndpoint.PathJoin(
						$"/v3/world/client/entitlements/{_currentEntitlementId}/purchase"
					),
					new ByteArrayContent([])
				);
				success = response.IsSuccessStatusCode;
				if (!success)
					BV.PrintErr("Entitlement purchase failed: ", await response.Content.ReadAsStringAsync());
			}
			catch (Exception exception)
			{
				BV.PrintErr("Entitlement purchase failed: ", exception.Message);
			}
		}
		SendPurchaseRes(success);
	}

	private long _currentEntitlementId;

	public override void Process(double delta)
	{
		if (Root != null && Root.Network.IsServer)
		{
			CleanupExpiredPurchases();
		}
		base.Process(delta);
	}

	[ScriptMethod]
	public async Task<bool> PromptAsync(Player player, long entitlementID)
	{
		ServerGuard();
		if (_pendingPlayers.Contains(player)) return false;

		string refID = Guid.NewGuid().ToString();
		TaskCompletionSource<bool> tcs = new();

		_pendingPurchases[refID] = new()
		{
			Player = player,
			EntitlementID = entitlementID,
			TaskSource = tcs,
			Timestamp = DateTime.Now
		};

		_pendingPlayers.Add(player);

		RpcId(player.PeerID, nameof(NetRecvPurchase), entitlementID, refID);

		return await tcs.Task;
	}

	[ScriptLegacyMethod("Prompt")]
	public void LegacyPrompt(Player player, long entitlementID, BVCallback callback)
	{
		ServerGuard();
		PromptAsync(player, entitlementID).ContinueWith(task =>
		{
			if (task.IsCompletedSuccessfully)
			{
				callback.Invoke(task.Result, "Purchase processed");
			}
			else if (task.IsFaulted && task.Exception != null)
			{
				callback.Invoke(false, "Purchase processing failed");
			}
		});
	}

	[ScriptMethod]
	public async Task<bool> OwnsItemAsync(Player player, long entitlementID)
	{
		ServerGuard();
		_client.DefaultRequestHeaders["Authorization"] = ServerAPI.GetAuthorizationHeaderValue();
		using HttpResponseMessage res = await _client.GetAsync(
			Globals.ApiEndpoint.PathJoin($"/v3/world/server/entitlements/ownership?entitlementId={entitlementID}&userId={player.UserID}")
		);
		res.EnsureSuccessStatusCode();

		using JsonDocument doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
		return doc.RootElement.TryGetProperty("userOwns", out JsonElement owns) && owns.GetBoolean();
	}

	[ScriptMethod]
	public async Task<bool> IsTrialAsync(Player player, long entitlementID)
	{
		ServerGuard();
		using JsonDocument status = await GetOwnershipStatus(player, entitlementID);
		return status.RootElement.TryGetProperty("isTrial", out JsonElement trial) && trial.GetBoolean();
	}

	[ScriptMethod]
	public async Task<string> GetTrialEndsAtAsync(Player player, long entitlementID)
	{
		ServerGuard();
		using JsonDocument status = await GetOwnershipStatus(player, entitlementID);
		return status.RootElement.TryGetProperty("trialEndsAt", out JsonElement ends) && ends.ValueKind == JsonValueKind.String ? ends.GetString() ?? "" : "";
	}

	[ScriptMethod]
	public async Task<bool> IsGameTrialAsync(Player player)
	{
		ServerGuard();
		_client.DefaultRequestHeaders["Authorization"] = ServerAPI.GetAuthorizationHeaderValue();
		using HttpResponseMessage response = await _client.GetAsync(Globals.ApiEndpoint.PathJoin($"/v3/world/server/entitlements/trial-status?userId={player.UserID}"));
		response.EnsureSuccessStatusCode();
		using JsonDocument status = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		return status.RootElement.TryGetProperty("isTrial", out JsonElement trial) && trial.GetBoolean();
	}

	private async Task<JsonDocument> GetOwnershipStatus(Player player, long entitlementID)
	{
		_client.DefaultRequestHeaders["Authorization"] = ServerAPI.GetAuthorizationHeaderValue();
		using HttpResponseMessage response = await _client.GetAsync(Globals.ApiEndpoint.PathJoin($"/v3/world/server/entitlements/ownership?entitlementId={entitlementID}&userId={player.UserID}"));
		response.EnsureSuccessStatusCode();
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
	}

	private void SendPurchaseRes(bool status)
	{
		RpcId(1, nameof(NetRecvPurchaseRes), _currentPurchaseRef, status);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private async void NetRecvPurchase(long entitlementID, string refID)
	{
		_currentPurchaseRef = refID;
		_currentEntitlementId = entitlementID;

		BV.Print("Entitlement purchase initiated with ID ", entitlementID);

		try
		{
			APIStoreItem storeItem = await GetEntitlementStoreItem(entitlementID);
			if (!storeItem.Price.HasValue) throw new Exception("This item does not have a price");
			_purchasePrompt?.Prompt(storeItem);
		}
		catch (Exception ex)
		{
			BV.PrintErr("Purchase processing failure: ", ex.Message);
			SendPurchaseRes(false);
		}
	}

	private async Task<APIStoreItem> GetEntitlementStoreItem(long entitlementID)
	{
		_client.DefaultRequestHeaders["Authorization"] = $"Bearer {ClientAuthAPI.JoinToken}";
		using HttpResponseMessage response = await _client.GetAsync(
			Globals.ApiEndpoint.PathJoin(
				$"/v3/world/client/entitlements/{entitlementID}/details"
			)
		);
		response.EnsureSuccessStatusCode();

		using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		JsonElement data = document.RootElement.GetProperty("data");
		string id = data.GetProperty("id").GetString() ?? entitlementID.ToString();
		string name = data.GetProperty("name").GetString() ?? "World item";
		int price = data.GetProperty("price").GetInt32();
		string thumbnailId = data.TryGetProperty("thumbnailId", out JsonElement thumbnail)
			? thumbnail.GetString() ?? id
			: id;

		return new APIStoreItem
		{
			Id = thumbnailId,
			Name = name,
			Description = data.TryGetProperty("description", out JsonElement description)
				? description.GetString() ?? ""
				: "",
			Type = data.GetProperty("type").GetString() ?? "Entitlement",
			Price = price,
			Thumbnail = Globals.ApiEndpoint.PathJoin("/v3/thumbnails/asset/" + thumbnailId),
		};
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetRecvPurchaseRes(string refID, bool accepted)
	{
		int peerID = RemoteSenderId;
		Player? plr = Root.Players.GetPlayerFromPeerID(peerID);

		if (plr != null)
		{
			if (_pendingPurchases.TryGetValue(refID, out var request))
			{
				if (request.Player != plr)
				{
					// Player mismatch
					return;
				}
				_pendingPlayers.Remove(plr);
				_pendingPurchases.Remove(refID);
				request.TaskSource.SetResult(accepted);
				RpcId(request.Player.PeerID, nameof(NetRecvPurchaseProcessRes), refID, accepted);
			}
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvPurchaseProcessRes(string refID, bool success)
	{
		if (refID == _currentPurchaseRef)
		{
			_currentPurchaseRef = "";
		}

		if (success)
		{
			_purchasePrompt?.PlayPurchaseSuccess();
		}
		else
		{
			// TODO: Implement error modal
			_purchasePrompt?.Hide();
		}
	}

	private void CleanupExpiredPurchases()
	{
		if (_pendingPurchases.Count == 0) return;

		List<string> keysToRemove = [];
		DateTime expireTime = DateTime.Now.AddMinutes(-5);

		foreach (var kvp in _pendingPurchases)
		{
			if (kvp.Value.Timestamp < expireTime)
			{
				keysToRemove.Add(kvp.Key);
			}
		}

		foreach (var key in keysToRemove)
		{
			var request = _pendingPurchases[key];
			_pendingPurchases.Remove(key);
			_pendingPlayers.Remove(request.Player);

			request.TaskSource.SetException(new TimeoutException("Purchase request timed out."));
		}
	}

	public struct PurchaseRequest
	{
		public Player Player;
		public long EntitlementID;
		public TaskCompletionSource<bool> TaskSource;
		public DateTime Timestamp;
	}

	private void ServerGuard()
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Purchases can only be accessed by server");
	}
}
