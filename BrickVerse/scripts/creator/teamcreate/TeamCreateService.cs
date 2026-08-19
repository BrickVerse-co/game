// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0.

using Godot;
using BrickVerse.Creator.Utils;
using BrickVerse.Creator.UI;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BrickVerse.Creator.TeamCreate;

public sealed partial class TeamCreateService : Node
{
	private const double PollInterval = 0.15;
	private const double FlushInterval = 0.1;
	private const double HeartbeatInterval = 1.0;
	private const double RescanInterval = 1.0;
	private const double ConnectivityInterval = 5.0;
	private const int MaxChangesPerBatch = 100;
	private const int MaxChangeBatchBytes = 240 * 1024;

	public static TeamCreateService? Instance { get; private set; }

	private readonly BVHttpClient _http = new();
	private readonly Dictionary<string, Observation> _observed = [];
	private readonly Dictionary<string, JsonObject> _pendingChanges = [];
	private readonly List<TeamCreateMember> _members = [];
	private readonly Dictionary<string, Node3D> _cameraAvatars = [];
	private long _universeId;
	private string _memberId = "";
	private string _localUserId = "";
	private long _sequence;
	private bool _enabled;
	private bool _joining;
	private bool _requestActive;
	private bool _applyingRemote;
	private bool _initialObservationComplete;
	private double _pollElapsed;
	private double _flushElapsed;
	private double _heartbeatElapsed;
	private double _rescanElapsed;
	private double _reconnectElapsed;
	private double _connectivityElapsed = ConnectivityInterval;
	private bool _connectivityRequestActive;
	private bool _manualDisconnect;
	private bool _showCameraAvatars = true;
	private TeamCreateSessionWindow? _window;
	private string _followMemberId = "";
	private Node3D? _cameraAvatarRoot;
	private World? _cameraAvatarWorld;

	public bool Connected => _enabled && !string.IsNullOrWhiteSpace(_memberId);
	public bool TeamCreateEnabled => _enabled;
	public bool Connecting => _joining;
	public bool ApiReachable { get; private set; }
	public string LastConnectionError { get; private set; } = "";
	public IReadOnlyList<TeamCreateMember> Members => _members;
	public string FollowedMemberId => _followMemberId;
	public string LocalUserId => _localUserId;
	public bool ShowCameraAvatars => _showCameraAvatars;
	public event Action<string, string>? TeamChatMessage;

	public string ResolveChatUsername(string userId)
	{
		if (userId == _localUserId && !string.IsNullOrWhiteSpace(CreatorAPI.Username)) return CreatorAPI.Username;
		TeamCreateMember? member = _members.FirstOrDefault(item => item.UserId == userId);
		return !string.IsNullOrWhiteSpace(member?.Username) ? member.Username : "Unknown member";
	}

	public void SendTeamChat(string message)
	{
		message = (message ?? "").Trim(); if (!Connected || message.Length == 0) return; if (message.Length > 300) message = message[..300];
		TeamChatMessage?.Invoke(_localUserId, message);
		QueueChange("team_chat:" + Guid.NewGuid().ToString("N"), new JsonObject { ["kind"] = "team_chat", ["id"] = "", ["senderId"] = _localUserId, ["message"] = message });
	}

	public TeamCreateService()
	{
		Instance = this;
		Name = "TeamCreate";
	}

	public override void _Ready()
	{
		if (!string.IsNullOrWhiteSpace(CreatorAPI.Token))
			_http.DefaultRequestHeaders["Authorization"] = "Bearer " + CreatorAPI.Token;
	}

	public override void _Process(double delta)
	{
		_connectivityElapsed += delta;
		if (_connectivityElapsed >= ConnectivityInterval && !_connectivityRequestActive)
		{
			_connectivityElapsed = 0;
			_ = CheckConnectivity();
		}

		long currentUniverse = World.Current?.UniverseID ?? 0;
		if (currentUniverse != _universeId)
		{
			_ = SwitchUniverse(currentUniverse);
			return;
		}

		if (!Connected)
		{
			if (currentUniverse != 0 && !_manualDisconnect)
			{
				_reconnectElapsed += delta;
				if (_reconnectElapsed >= 10.0)
				{
					_reconnectElapsed = 0;
					_ = _enabled
						? RejoinCurrentSession(currentUniverse)
						: SwitchUniverse(currentUniverse);
				}
			}
			return;
		}
		_reconnectElapsed = 0;
		UpdateFollowCamera((float)delta);
		UpdateCameraAvatarTransforms((float)delta);
		if (_requestActive) return;

		_pollElapsed += delta;
		_flushElapsed += delta;
		_heartbeatElapsed += delta;
		_rescanElapsed += delta;

		if (_rescanElapsed >= RescanInterval)
		{
			_rescanElapsed = 0;
			ObserveWorld();
		}
		if (_flushElapsed >= FlushInterval && _pendingChanges.Count > 0)
		{
			_flushElapsed = 0;
			_ = FlushChanges();
		}
		else if (_heartbeatElapsed >= HeartbeatInterval)
		{
			_heartbeatElapsed = 0;
			_ = Heartbeat();
		}
		else if (_pollElapsed >= PollInterval)
		{
			_pollElapsed = 0;
			_ = PollChanges();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (string.IsNullOrWhiteSpace(_followMemberId)) return;
		if (@event is InputEventMouseMotion
			or InputEventMouseButton { Pressed: true }
			or InputEventKey { Pressed: true })
			StopFollowing();
	}

	public override void _ExitTree()
	{
		_ = Leave();
		UnobserveAll();
		ClearCameraAvatars();
		if (ReferenceEquals(Instance, this)) Instance = null;
	}

	public void ShowSessionWindow()
	{
		if (_window == null || !IsInstanceValid(_window))
		{
			_window = new TeamCreateSessionWindow(this);
			CreatorService.Interface.AddChild(_window);
		}
		_window.Refresh();
		_window.PopupCentered();
		_ = EnsureConnected();
	}

	public Task EnsureConnected()
	{
		long universeId = World.Current?.UniverseID ?? 0;
		if (universeId == 0 || Connected || _joining) return Task.CompletedTask;
		_manualDisconnect = false;
		_reconnectElapsed = 0;
		return SwitchUniverse(universeId);
	}

	public void FollowMember(string memberId)
	{
		TeamCreateMember? member = _members.FirstOrDefault(item => item.Id == memberId);
		if (member == null || member.UserId == _localUserId) return;
		_followMemberId = memberId;
		UpdateFollowCamera(1f);
		_window?.Refresh();
	}

	public void StopFollowing()
	{
		_followMemberId = "";
		_window?.Refresh();
	}

	public void SetCameraAvatarsVisible(bool visible)
	{
		_showCameraAvatars = visible;
		if (_cameraAvatarRoot != null && IsInstanceValid(_cameraAvatarRoot))
			_cameraAvatarRoot.Visible = visible;
		_window?.Refresh();
	}

	public void Reconnect()
	{
		_manualDisconnect = false;
		_ = ReconnectInternal();
	}

	public void Disconnect()
	{
		_manualDisconnect = true;
		_ = DisconnectInternal();
	}

	private async Task ReconnectInternal()
	{
		await Leave();
		_memberId = "";
		_localUserId = "";
		_members.Clear();
		ClearCameraAvatars();
		await RejoinCurrentSession(_universeId);
	}

	private async Task DisconnectInternal()
	{
		await Leave();
		_localUserId = "";
		_members.Clear();
		_pendingChanges.Clear();
		StopFollowing();
		UnobserveAll();
		ClearCameraAvatars();
		CreatorService.Interface.StatusBar?.SetStatus("Team Create disconnected");
		_window?.Refresh();
	}

	private void UpdateFollowCamera(float delta)
	{
		if (string.IsNullOrWhiteSpace(_followMemberId)) return;
		TeamCreateMember? member = _members.FirstOrDefault(item => item.Id == _followMemberId);
		Camera3D? camera = World.Current?.CreatorContext?.Freelook?.Camera3D;
		if (member?.Camera == null || member.UserId == _localUserId || camera == null)
		{
			StopFollowing();
			return;
		}
		float weight = Mathf.Clamp(delta * 10f, 0f, 1f);
		camera.GlobalPosition = camera.GlobalPosition.Lerp(member.Camera.Position, weight);
		camera.GlobalRotation = camera.GlobalRotation.Lerp(member.Camera.Rotation, weight);
	}

	private async Task SwitchUniverse(long universeId)
	{
		if (_joining) return;
		_joining = true;
		try
		{
			await Leave();
			UnobserveAll();
			_universeId = universeId;
			_enabled = false;
			LastConnectionError = "";
			_sequence = 0;
			_localUserId = "";
			_members.Clear();
			_followMemberId = "";
			_pendingChanges.Clear();
			_initialObservationComplete = false;
			_manualDisconnect = false;
			ClearCameraAvatars();
			if (universeId == 0) return;
			string token = await CreatorAPI.GetValidAccessTokenAsync();
			_http.DefaultRequestHeaders["Authorization"] = "Bearer " + token;

			_enabled = await FetchTeamCreateEnabled();
			if (!_enabled) return;

			using HttpResponseMessage response = await SendJson(
				HttpMethod.Post,
				ApiPath("/join"),
				new JsonObject()
			);
			response.EnsureSuccessStatusCode();
			using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			_memberId = json.RootElement.GetProperty("memberId").GetString() ?? "";
			ReadSession(json.RootElement.GetProperty("session"));
			ObserveWorld();
			CreatorService.Interface.StatusBar?.SetStatus("Team Create connected");
		}
		catch (Exception error)
		{
			BV.PrintErr("Team Create connection failed: ", error.Message);
			LastConnectionError = error.Message;
			if (error is HttpRequestException)
				ApiReachable = false;
			_enabled = false;
			_memberId = "";
		}
		finally
		{
			_joining = false;
			_window?.Refresh();
		}
	}

	private async Task<bool> FetchTeamCreateEnabled()
	{
		string url = ApiPath("");
		using HttpResponseMessage response = await _http.GetAsync(url);
		ApiReachable = true;
		string body = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode)
		{
			LastConnectionError =
				$"Team Create status request returned {(int)response.StatusCode} " +
				$"({response.ReasonPhrase}).";
			BV.PrintErr(LastConnectionError, " URL: ", url, " Body: ", body);
			return false;
		}

		using JsonDocument json = JsonDocument.Parse(body);
		JsonElement root = json.RootElement;
		if (!root.TryGetProperty("success", out JsonElement success)
			|| success.ValueKind != JsonValueKind.True)
			throw new InvalidDataException(
				"Team Create status response did not report success.");
		if (!root.TryGetProperty("enabled", out JsonElement enabled)
			|| enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
			throw new InvalidDataException(
				"Team Create status response is missing the boolean 'enabled' field.");

		bool isEnabled = enabled.GetBoolean();
		/*BV.Print(
			"Team Create status for universe ",
			_universeId,
			": ",
			isEnabled ? "enabled" : "disabled");*/
		return isEnabled;
	}

	private async Task CheckConnectivity()
	{
		_connectivityRequestActive = true;
		try
		{
			using HttpResponseMessage response = await _http.GetAsync(
				Globals.ApiEndpoint.PathJoin("/v3/health")
			);
			ApiReachable = response.IsSuccessStatusCode;
		}
		catch
		{
			ApiReachable = false;
		}
		finally
		{
			_connectivityRequestActive = false;
		}
	}

	private async Task Leave()
	{
		if (_universeId == 0 || string.IsNullOrWhiteSpace(_memberId)) return;
		string memberId = _memberId;
		_memberId = "";
		try
		{
			using HttpRequestMessage request = new(
				HttpMethod.Delete,
				ApiPath("/members/" + Uri.EscapeDataString(memberId))
			);
			using HttpResponseMessage _ = await _http.SendAsync(request);
		}
		catch { }
	}

	private async Task PollChanges()
	{
		_requestActive = true;
		long requestedUniverse = _universeId;
		try
		{
			using HttpResponseMessage response = await _http.GetAsync(
				ApiPath("/changes?after=" + _sequence.ToString(CultureInfo.InvariantCulture))
			);
			if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
			{
				CallDeferred(nameof(DisableFromServer));
				return;
			}
			if (!response.IsSuccessStatusCode) return;
			string body = await response.Content.ReadAsStringAsync();
			CallDeferred(nameof(ApplyPollResponse), requestedUniverse, body);
		}
		catch (Exception error)
		{
			BV.PrintErr("Team Create poll failed: ", error.Message);
		}
		finally
		{
			_requestActive = false;
		}
	}

	private async Task Heartbeat()
	{
		string memberId = _memberId;
		if (string.IsNullOrWhiteSpace(memberId)) return;
		_requestActive = true;

		Camera3D? camera = World.Current?.CreatorContext?.Freelook?.Camera3D;
		JsonObject payload = new() { ["memberId"] = memberId };
		if (camera != null)
		{
			payload["camera"] = new JsonObject
			{
				["position"] = Vector(camera.GlobalPosition),
				["rotation"] = Vector(camera.GlobalRotation),
			};
		}
		try
		{
			using HttpResponseMessage response = await SendJson(
				HttpMethod.Post,
				ApiPath("/heartbeat"),
				payload
			);
			string body = await response.Content.ReadAsStringAsync();
			if (!response.IsSuccessStatusCode)
			{
				if (IsMissingMembershipResponse(response, body))
					CallDeferred(nameof(HandleMembershipLost), memberId, body);
				return;
			}
			using JsonDocument json = JsonDocument.Parse(body);
			if (json.RootElement.TryGetProperty("session", out JsonElement session))
				CallDeferred(nameof(ApplySessionResponse), session.GetRawText());
		}
		catch (Exception error)
		{
			BV.PrintErr("Team Create heartbeat failed: ", error.Message);
		}
		finally
		{
			_requestActive = false;
		}
	}

	private async Task FlushChanges()
	{
		string memberId = _memberId;
		if (string.IsNullOrWhiteSpace(memberId)) return;
		_requestActive = true;

		List<KeyValuePair<string, JsonObject>> batch = [];
		JsonArray changes = [];
		int batchBytes = 2;
		foreach (KeyValuePair<string, JsonObject> entry in _pendingChanges)
		{
			int changeBytes = Encoding.UTF8.GetByteCount(entry.Value.ToJsonString()) + 1;
			if (batch.Count > 0
				&& (batch.Count >= MaxChangesPerBatch || batchBytes + changeBytes > MaxChangeBatchBytes))
				break;
			batch.Add(entry);
			changes.Add(entry.Value.DeepClone());
			batchBytes += changeBytes;
		}
		if (changes.Count == 0)
		{
			_requestActive = false;
			return;
		}

		try
		{
			using HttpResponseMessage response = await SendJson(
				HttpMethod.Post,
				ApiPath("/changes"),
				new JsonObject { ["memberId"] = memberId, ["changes"] = changes }
			);
			string body = await response.Content.ReadAsStringAsync();
			if (!response.IsSuccessStatusCode)
			{
				if (IsMissingMembershipResponse(response, body))
					CallDeferred(nameof(HandleMembershipLost), memberId, body);
				else
					BV.PrintErr("Team Create rejected changes: ", body);
				return;
			}

			// Only remove changes that were actually included in this batch.
			// A newer edit may have replaced an entry with the same coalescing key
			// while the request was in flight.
			foreach ((string key, JsonObject sentChange) in batch)
			{
				if (_pendingChanges.TryGetValue(key, out JsonObject? current)
					&& ReferenceEquals(current, sentChange))
					_pendingChanges.Remove(key);
			}
		}
		catch (Exception error)
		{
			BV.PrintErr("Team Create change upload failed: ", error.Message);
		}
		finally
		{
			_requestActive = false;
		}
	}

	private static bool IsMissingMembershipResponse(
		HttpResponseMessage response,
		string body
	) =>
		response.StatusCode == System.Net.HttpStatusCode.NotFound
		&& body.Contains("member not found", StringComparison.OrdinalIgnoreCase);

	private void HandleMembershipLost(string rejectedMemberId, string responseBody)
	{
		if (string.IsNullOrWhiteSpace(rejectedMemberId)
			|| rejectedMemberId != _memberId)
			return;

		_memberId = "";
		_localUserId = "";
		_members.Clear();
		ClearCameraAvatars();
		_window?.Refresh();
		CreatorService.Interface.StatusBar?.SetStatus("Team Create reconnecting...");
		BV.Print(
			"Team Create session membership expired; reconnecting. Server response: ",
			responseBody);
		_ = RejoinCurrentSession(_universeId);
	}

	private async Task RejoinCurrentSession(long universeId)
	{
		if (_joining
			|| universeId == 0
			|| universeId != _universeId
			|| !_enabled)
			return;

		_joining = true;
		try
		{
			string token = await CreatorAPI.GetValidAccessTokenAsync();
			_http.DefaultRequestHeaders["Authorization"] = "Bearer " + token;

			using HttpResponseMessage response = await SendJson(
				HttpMethod.Post,
				ApiPath("/join"),
				new JsonObject()
			);
			string body = await response.Content.ReadAsStringAsync();
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					$"Team Create rejoin returned {(int)response.StatusCode}: {body}");

			if (universeId != _universeId) return;

			using JsonDocument json = JsonDocument.Parse(body);
			_memberId = json.RootElement.GetProperty("memberId").GetString() ?? "";
			if (string.IsNullOrWhiteSpace(_memberId))
				throw new InvalidDataException(
					"Team Create rejoin response did not include a member ID.");

			ReadSession(json.RootElement.GetProperty("session"));
			_reconnectElapsed = 0;
			LastConnectionError = "";
			CreatorService.Interface.StatusBar?.SetStatus("Team Create reconnected");
			BV.Print("Team Create reconnected.");
		}
		catch (Exception error)
		{
			LastConnectionError = error.Message;
			BV.PrintErr("Team Create rejoin failed: ", error.Message);
			_reconnectElapsed = 0;
		}
		finally
		{
			_joining = false;
			_window?.Refresh();
		}
	}

	private void ApplyPollResponse(long universeId, string body)
	{
		if (universeId != _universeId) return;
		using JsonDocument json = JsonDocument.Parse(body);
		JsonElement root = json.RootElement;
		if (root.TryGetProperty("session", out JsonElement session)) ReadSession(session);
		if (!root.TryGetProperty("events", out JsonElement events)) return;
		foreach (JsonElement relayEvent in events.EnumerateArray())
		{
			long eventSequence = relayEvent.GetProperty("sequence").GetInt64();
			string authorId = relayEvent.GetProperty("authorId").GetString() ?? "";
			if (eventSequence <= _sequence) continue;
			_sequence = eventSequence;
			if (authorId == _localUserId) continue;
			foreach (JsonElement change in relayEvent.GetProperty("changes").EnumerateArray())
				ApplyChange(change);
		}
	}

	private void ApplySessionResponse(string body)
	{
		using JsonDocument json = JsonDocument.Parse(body);
		ReadSession(json.RootElement);
	}

	private void DisableFromServer()
	{
		_enabled = false;
		_memberId = "";
		_members.Clear();
		_pendingChanges.Clear();
		StopFollowing();
		UnobserveAll();
		ClearCameraAvatars();
		CreatorService.Interface.StatusBar?.SetStatus("Team Create was disabled");
	}

	private void ReadSession(JsonElement session)
	{
		_members.Clear();
		if (!session.TryGetProperty("members", out JsonElement members)) return;
		foreach (JsonElement member in members.EnumerateArray())
		{
			TeamCreateMember item = new()
			{
				Id = member.GetProperty("id").GetString() ?? "",
				UserId = member.GetProperty("userId").GetString() ?? "",
				Username = member.GetProperty("username").GetString() ?? "Unknown",
				IsVerified = member.TryGetProperty("isVerified", out JsonElement verified) && verified.GetBoolean(),
			};
			if (member.TryGetProperty("camera", out JsonElement camera))
			{
				item.Camera = new()
				{
					Position = ReadVector3(camera.GetProperty("position")),
					Rotation = ReadVector3(camera.GetProperty("rotation")),
				};
			}
			_members.Add(item);
			if (item.Id == _memberId) _localUserId = item.UserId;
		}
		RefreshCameraAvatars();
		_window?.Refresh();
	}

	private void RefreshCameraAvatars()
	{
		World? world = World.Current;
		if (world == null)
		{
			ClearCameraAvatars();
			return;
		}

		if (_cameraAvatarRoot == null
			|| !IsInstanceValid(_cameraAvatarRoot)
			|| _cameraAvatarWorld != world)
		{
			ClearCameraAvatars();
			_cameraAvatarWorld = world;
			_cameraAvatarRoot = new Node3D { Name = "TeamCreateCameraAvatars" };
			_cameraAvatarRoot.Visible = _showCameraAvatars;
			world.GDNode.AddChild(_cameraAvatarRoot, @internal: Node.InternalMode.Back);
		}

		HashSet<string> active = [];
		foreach (TeamCreateMember member in _members)
		{
			if (member.Id == _memberId || member.Camera == null) continue;
			active.Add(member.Id);
			if (!_cameraAvatars.TryGetValue(member.Id, out Node3D? avatar)
				|| !IsInstanceValid(avatar))
			{
				avatar = CreateCameraAvatar(member);
				_cameraAvatarRoot.AddChild(avatar);
				_cameraAvatars[member.Id] = avatar;
			}

			if (avatar.GlobalPosition == Vector3.Zero)
			{
				avatar.GlobalPosition = member.Camera.Position;
				avatar.GlobalRotation = member.Camera.Rotation;
			}
			Label3D? label = avatar.GetNodeOrNull<Label3D>("Username");
			if (label != null) label.Text = DisplayUsername(member.Username, member.IsVerified);
		}

		foreach ((string id, Node3D avatar) in _cameraAvatars.ToArray())
		{
			if (active.Contains(id)) continue;
			if (IsInstanceValid(avatar)) avatar.QueueFree();
			_cameraAvatars.Remove(id);
		}
	}

	private void UpdateCameraAvatarTransforms(float delta)
	{
		float weight = Mathf.Clamp(delta * 9f, 0f, 1f);
		foreach (TeamCreateMember member in _members)
		{
			if (member.Camera == null
				|| !_cameraAvatars.TryGetValue(member.Id, out Node3D? avatar)
				|| !IsInstanceValid(avatar))
				continue;
			avatar.GlobalPosition = avatar.GlobalPosition.Lerp(member.Camera.Position, weight);
			avatar.GlobalRotation = avatar.GlobalRotation.Lerp(member.Camera.Rotation, weight);
		}
	}

	private static Node3D CreateCameraAvatar(TeamCreateMember member)
	{
		Node3D avatar = new() { Name = "Collaborator_" + member.Id };
		Color color = ColorFromUserId(member.UserId);
		StandardMaterial3D material = new()
		{
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = color,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};

		MeshInstance3D head = new()
		{
			Name = "Camera",
			Mesh = new SphereMesh
			{
				Radius = 0.22f,
				Height = 0.44f,
				Material = material,
			},
		};
		avatar.AddChild(head);

		MeshInstance3D direction = new()
		{
			Name = "FacingDirection",
			Position = new Vector3(0, 0, -0.65f),
			Mesh = new BoxMesh
			{
				Size = new Vector3(0.09f, 0.09f, 1.1f),
				Material = material,
			},
		};
		avatar.AddChild(direction);

		Label3D label = new()
		{
			Name = "Username",
			Text = DisplayUsername(member.Username, member.IsVerified),
			Position = new Vector3(0, 0.48f, 0),
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FixedSize = false,
			PixelSize = 0.004f,
			FontSize = 20,
			OutlineSize = 6,
			Modulate = Colors.White,
			OutlineModulate = new Color(0.04f, 0.04f, 0.06f, 0.95f),
			NoDepthTest = true,
		};
		avatar.AddChild(label);
		return avatar;
	}

	private static string DisplayUsername(string username, bool isVerified = false)
	{
		string displayName = username.Length <= 24 ? username : username[..21] + "...";
		return isVerified ? displayName + "  ✓" : displayName;
	}

	private static Color ColorFromUserId(string userId)
	{
		uint hash = 2166136261;
		foreach (char character in userId)
			hash = (hash ^ character) * 16777619;
		return Color.FromHsv((hash % 360) / 360f, 0.68f, 1f);
	}

	private void ClearCameraAvatars()
	{
		if (_cameraAvatarRoot != null && IsInstanceValid(_cameraAvatarRoot))
			_cameraAvatarRoot.QueueFree();
		_cameraAvatarRoot = null;
		_cameraAvatarWorld = null;
		_cameraAvatars.Clear();
	}

	private void ObserveWorld()
	{
		World? world = World.Current;
		if (world == null) return;
		bool queueDiscoveredObjects = _initialObservationComplete;
		Observe(world);
		foreach (NetworkedObject item in world.NetworkObjects.Values.ToArray())
		{
			bool newlyObserved = Observe(item);
			if (queueDiscoveredObjects && newlyObserved && item is Instance instance)
				QueueCreate(instance);
		}
		_initialObservationComplete = true;
	}

	private bool Observe(NetworkedObject item)
	{
		string id = item.NetworkedObjectID;
		if (string.IsNullOrWhiteSpace(id) || _observed.ContainsKey(id)) return false;
		Action<string> propertyHandler = property => OnPropertyChanged(item, property ?? "");
		Action deletedHandler = () =>
		{
			if (_applyingRemote || !Connected) return;
			QueueChange(
				"delete:" + id,
				new JsonObject { ["kind"] = "delete", ["id"] = id }
			);
		};
		Action<Instance>? childAddedHandler = null;
		if (item is Instance instance)
		{
			childAddedHandler = child =>
			{
				if (child is Instance instanceChild) OnChildAdded(instance, instanceChild);
			};
			instance.ChildAdded.Connect(childAddedHandler);
		}
		item.PropertyChanged.Connect(propertyHandler);
		item.Deleted += deletedHandler;
		_observed[id] = new(item, propertyHandler, deletedHandler, childAddedHandler);
		return true;
	}

	private void OnChildAdded(Instance parent, Instance child)
	{
		if (_applyingRemote || !Connected || child.Root != World.Current) return;
		EnsureCollaborativeIds(child);
		bool wasObserved = _observed.ContainsKey(child.NetworkedObjectID);
		Observe(child);
		if (wasObserved)
		{
			QueueChange(
				"reparent:" + child.NetworkedObjectID,
				new JsonObject
				{
					["kind"] = "reparent",
					["id"] = child.NetworkedObjectID,
					["parentId"] = parent.NetworkedObjectID,
				}
			);
			return;
		}
		QueueCreate(child);
		foreach (Instance descendant in child.GetDescendants())
		{
			Observe(descendant);
			QueueCreate(descendant);
		}
	}

	private static void EnsureCollaborativeIds(Instance root)
	{
		if (string.IsNullOrWhiteSpace(root.NetworkedObjectID))
			root.NetworkedObjectID = "tc-" + Guid.NewGuid().ToString("N");
		foreach (Instance item in root.GetDescendants())
		{
			if (string.IsNullOrWhiteSpace(item.NetworkedObjectID))
				item.NetworkedObjectID = "tc-" + Guid.NewGuid().ToString("N");
		}
	}

	private void QueueCreate(Instance child)
	{
		if (string.IsNullOrWhiteSpace(child.NetworkedObjectID)
			|| child.Parent == null
			|| string.IsNullOrWhiteSpace(child.Parent.NetworkedObjectID))
			return;

		JsonObject properties = [];
		foreach (System.Reflection.PropertyInfo property in child.GetEditableProperties())
		{
			if (!property.CanRead || !property.CanWrite || property.Name == nameof(NetworkedObject.Name))
				continue;
			try
			{
				JsonNode? value = EncodeValue(property.GetValue(child));
				if (value is not null) properties[property.Name] = value;
			}
			catch (Exception error)
			{
				BV.PrintWarn("Could not snapshot Team Create property ", property.Name, ": ", error.Message);
			}
		}

		JsonObject change = new()
		{
			["kind"] = "create",
			["id"] = child.NetworkedObjectID,
			["parentId"] = child.Parent.NetworkedObjectID,
			["className"] = child.ClassName,
			["name"] = child.Name,
			["properties"] = properties,
		};
		if (child is Dynamic dynamic) change["transform"] = Transform(dynamic.GetLocalTransform());
		QueueChange("create:" + child.NetworkedObjectID, change);
	}

	private void OnPropertyChanged(NetworkedObject item, string property)
	{
		if (_applyingRemote || !Connected || item.Root != World.Current) return;
		if (item is Dynamic dynamic && IsTransformProperty(property))
		{
			QueueChange(
				"transform:" + item.NetworkedObjectID,
				new JsonObject
				{
					["kind"] = "transform",
					["id"] = item.NetworkedObjectID,
					["value"] = Transform(dynamic.GetLocalTransform()),
				}
			);
			return;
		}

		System.Reflection.PropertyInfo? info = item.GetType().GetProperty(property);
		if (info == null || !info.CanRead || !info.CanWrite) return;
		JsonNode? value = EncodeValue(info.GetValue(item));
		if (value == null) return;
		QueueChange(
			"property:" + item.NetworkedObjectID + ":" + property,
			new JsonObject
			{
				["kind"] = "property",
				["id"] = item.NetworkedObjectID,
				["property"] = property,
				["value"] = value,
			}
		);
	}

	private void ApplyChange(JsonElement change)
	{
		World? world = World.Current;
		if (world == null) return;
		string kind = change.GetProperty("kind").GetString() ?? "";
		string id = change.GetProperty("id").GetString() ?? "";
		if (kind == "team_chat")
		{
			string sender = change.GetProperty("senderId").GetString() ?? ""; string message = change.GetProperty("message").GetString() ?? "";
			if (sender != _localUserId && message.Length <= 300) TeamChatMessage?.Invoke(sender, message);
			return;
		}
		_applyingRemote = true;
		try
		{
			if (kind == "delete")
			{
				world.GetNetObjectFromID(id)?.Delete();
				return;
			}
			if (kind == "create")
			{
				if (world.GetNetObjectFromID(id) != null) return;
				string className = change.GetProperty("className").GetString() ?? "";
				string parentId = change.GetProperty("parentId").GetString() ?? "";
				if (world.GetNetObjectFromID(parentId) is not Instance parent) return;
				Instance? created = Globals.LoadInstance<Instance>(
					className,
					world,
					item => item.NetworkedObjectID = id
				);
				if (created == null) return;
				created.Name = change.GetProperty("name").GetString() ?? className;
				if (created is Dynamic dynamic && change.TryGetProperty("transform", out JsonElement transform))
					dynamic.SetLocalTransform(ReadTransform(transform));
				if (change.TryGetProperty("properties", out JsonElement properties)
					&& properties.ValueKind == JsonValueKind.Object)
				{
					foreach (JsonProperty property in properties.EnumerateObject())
					{
						System.Reflection.PropertyInfo? info = created.GetType().GetProperty(property.Name);
						if (info?.CanWrite != true) continue;
						object? value = DecodeValue(property.Value, info.PropertyType);
						if (value != null || !info.PropertyType.IsValueType)
							info.SetValue(created, value);
					}
				}
				created.Parent = parent;
				created.CreatorInserted();
				Observe(created);
				return;
			}

			NetworkedObject? target = world.GetNetObjectFromID(id);
			if (target == null) return;
			if (kind == "reparent" && target is Instance targetInstance)
			{
				string parentId = change.GetProperty("parentId").GetString() ?? "";
				if (world.GetNetObjectFromID(parentId) is Instance newParent)
					targetInstance.Parent = newParent;
				return;
			}
			if (kind == "transform" && target is Dynamic targetDynamic)
			{
				targetDynamic.SetLocalTransform(ReadTransform(change.GetProperty("value")));
				return;
			}
			if (kind == "property")
			{
				string property = change.GetProperty("property").GetString() ?? "";
				System.Reflection.PropertyInfo? info = target.GetType().GetProperty(property);
				if (info?.CanWrite != true) return;
				object? value = DecodeValue(change.GetProperty("value"), info.PropertyType);
				if (value != null || !info.PropertyType.IsValueType) info.SetValue(target, value);
			}
		}
		catch (Exception error)
		{
			BV.PrintErr("Failed to apply Team Create change: ", error.Message);
		}
		finally
		{
			_applyingRemote = false;
		}
	}

	private void QueueChange(string key, JsonObject change) => _pendingChanges[key] = change;

	private void UnobserveAll()
	{
		foreach (Observation observation in _observed.Values)
		{
			observation.Item.PropertyChanged.Disconnect(observation.PropertyHandler);
			observation.Item.Deleted -= observation.DeletedHandler;
			if (observation.ChildAddedHandler is not null && observation.Item is Instance instance)
				instance.ChildAdded.Disconnect(observation.ChildAddedHandler);
		}
		_observed.Clear();
	}

	private string ApiPath(string suffix) =>
		Globals.ApiEndpoint.PathJoin(
			"/v3/team-create/" + _universeId.ToString(CultureInfo.InvariantCulture) + suffix
		);

	private async Task<HttpResponseMessage> SendJson(
		HttpMethod method,
		string url,
		JsonObject payload
	)
	{
		using HttpRequestMessage request = new(method, url)
		{
			Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
		};
		return await _http.SendAsync(request);
	}

	private static bool IsTransformProperty(string property) =>
		property is "Position" or "Rotation" or "Size"
			or "LocalPosition" or "LocalRotation" or "LocalSize"
			or "Quaternion" or "LocalQuaternion";

	private static JsonArray Vector(Vector3 value) => [value.X, value.Y, value.Z];

	private static JsonArray Transform(Transform3D value) =>
	[
		value.Basis.X.X, value.Basis.X.Y, value.Basis.X.Z,
		value.Basis.Y.X, value.Basis.Y.Y, value.Basis.Y.Z,
		value.Basis.Z.X, value.Basis.Z.Y, value.Basis.Z.Z,
		value.Origin.X, value.Origin.Y, value.Origin.Z,
	];

	private static Transform3D ReadTransform(JsonElement value)
	{
		float[] values = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
		if (values.Length != 12) return Transform3D.Identity;
		return new(
			new Basis(
				new Vector3(values[0], values[1], values[2]),
				new Vector3(values[3], values[4], values[5]),
				new Vector3(values[6], values[7], values[8])
			),
			new Vector3(values[9], values[10], values[11])
		);
	}

	private static Vector3 ReadVector3(JsonElement value)
	{
		float[] values = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
		return values.Length >= 3 ? new(values[0], values[1], values[2]) : Vector3.Zero;
	}

	private static JsonNode? EncodeValue(object? value) => value switch
	{
		null => JsonValue.Create((string?)null),
		string text => JsonValue.Create(text),
		StringName text => new JsonObject { ["$type"] = "stringName", ["value"] = text.ToString() },
		NodePath path => new JsonObject { ["$type"] = "nodePath", ["value"] = path.ToString() },
		bool flag => JsonValue.Create(flag),
		sbyte number => JsonValue.Create(number),
		byte number => JsonValue.Create(number),
		short number => JsonValue.Create(number),
		ushort number => JsonValue.Create(number),
		int number => JsonValue.Create(number),
		uint number => JsonValue.Create(number),
		long number => JsonValue.Create(number),
		ulong number => JsonValue.Create(number),
		float number => JsonValue.Create(number),
		double number => JsonValue.Create(number),
		Enum enumValue => new JsonObject
		{
			["$type"] = "enum",
			["value"] = enumValue.ToString(),
		},
		Vector2 vector => new JsonObject
		{
			["$type"] = "vector2",
			["value"] = new JsonArray(vector.X, vector.Y),
		},
		Vector3 vector => new JsonObject { ["$type"] = "vector3", ["value"] = Vector(vector) },
		Vector2I vector => new JsonObject { ["$type"] = "vector2i", ["value"] = new JsonArray(vector.X, vector.Y) },
		Vector3I vector => new JsonObject { ["$type"] = "vector3i", ["value"] = new JsonArray(vector.X, vector.Y, vector.Z) },
		Vector4 vector => new JsonObject { ["$type"] = "vector4", ["value"] = new JsonArray(vector.X, vector.Y, vector.Z, vector.W) },
		Quaternion quaternion => new JsonObject { ["$type"] = "quaternion", ["value"] = new JsonArray(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W) },
		Transform3D transform => new JsonObject { ["$type"] = "transform3d", ["value"] = Transform(transform) },
		Color color => new JsonObject
		{
			["$type"] = "color",
			["value"] = new JsonArray(color.R, color.G, color.B, color.A),
		},
		NetworkedObject reference when !string.IsNullOrWhiteSpace(reference.NetworkedObjectID) =>
			new JsonObject { ["$type"] = "reference", ["id"] = reference.NetworkedObjectID },
		Array array => EncodeArray(array),
		_ => null,
	};

	private static JsonObject EncodeArray(Array values)
	{
		JsonArray encoded = [];
		foreach (object? value in values)
			encoded.Add(EncodeValue(value));
		return new JsonObject { ["$type"] = "array", ["value"] = encoded };
	}

	private object? DecodeValue(JsonElement value, Type type)
	{
		if (value.ValueKind == JsonValueKind.Null) return null;
		Type? nullableType = Nullable.GetUnderlyingType(type);
		if (nullableType != null) return DecodeValue(value, nullableType);
		if (type == typeof(string)) return value.GetString();
		if (type == typeof(StringName)) return new StringName(value.GetProperty("value").GetString() ?? "");
		if (type == typeof(NodePath)) return new NodePath(value.GetProperty("value").GetString() ?? "");
		if (type == typeof(bool)) return value.GetBoolean();
		if (type == typeof(sbyte)) return value.GetSByte();
		if (type == typeof(byte)) return value.GetByte();
		if (type == typeof(short)) return value.GetInt16();
		if (type == typeof(ushort)) return value.GetUInt16();
		if (type == typeof(int)) return value.GetInt32();
		if (type == typeof(uint)) return value.GetUInt32();
		if (type == typeof(long)) return value.GetInt64();
		if (type == typeof(ulong)) return value.GetUInt64();
		if (type == typeof(float)) return value.GetSingle();
		if (type == typeof(double)) return value.GetDouble();
		if (type.IsEnum)
			return Enum.Parse(type, value.GetProperty("value").GetString() ?? "", true);
		if (type == typeof(Vector2))
		{
			float[] values = value.GetProperty("value").EnumerateArray().Select(item => item.GetSingle()).ToArray();
			return new Vector2(values[0], values[1]);
		}
		if (type == typeof(Vector3)) return ReadVector3(value.GetProperty("value"));
		if (type == typeof(Vector2I))
		{
			int[] values = value.GetProperty("value").EnumerateArray().Select(item => item.GetInt32()).ToArray();
			return new Vector2I(values[0], values[1]);
		}
		if (type == typeof(Vector3I))
		{
			int[] values = value.GetProperty("value").EnumerateArray().Select(item => item.GetInt32()).ToArray();
			return new Vector3I(values[0], values[1], values[2]);
		}
		if (type == typeof(Vector4))
		{
			float[] values = value.GetProperty("value").EnumerateArray().Select(item => item.GetSingle()).ToArray();
			return new Vector4(values[0], values[1], values[2], values[3]);
		}
		if (type == typeof(Quaternion))
		{
			float[] values = value.GetProperty("value").EnumerateArray().Select(item => item.GetSingle()).ToArray();
			return new Quaternion(values[0], values[1], values[2], values[3]);
		}
		if (type == typeof(Transform3D)) return ReadTransform(value.GetProperty("value"));
		if (type == typeof(Color))
		{
			float[] values = value.GetProperty("value").EnumerateArray().Select(item => item.GetSingle()).ToArray();
			return new Color(values[0], values[1], values[2], values[3]);
		}
		if (typeof(NetworkedObject).IsAssignableFrom(type))
			return World.Current?.GetNetObjectFromID(value.GetProperty("id").GetString() ?? "");
		if (type.IsArray)
		{
			JsonElement encodedValues = value.GetProperty("value");
			Type elementType = type.GetElementType()!;
			Array result = Array.CreateInstance(elementType, encodedValues.GetArrayLength());
			int index = 0;
			foreach (JsonElement encodedValue in encodedValues.EnumerateArray())
				result.SetValue(DecodeValue(encodedValue, elementType), index++);
			return result;
		}
		return null;
	}

	private sealed record Observation(
		NetworkedObject Item,
		Action<string> PropertyHandler,
		Action DeletedHandler,
		Action<Instance>? ChildAddedHandler
	);
}

public sealed class TeamCreateMember
{
	public string Id { get; init; } = "";
	public string UserId { get; init; } = "";
	public string Username { get; init; } = "";
	public bool IsVerified { get; init; }
	public TeamCreateCamera? Camera { get; set; }
}

public sealed class TeamCreateCamera
{
	public Vector3 Position { get; init; }
	public Vector3 Rotation { get; init; }
}
