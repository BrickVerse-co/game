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
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BrickVerse.Creator.TeamCreate;

public sealed partial class TeamCreateService : Node
{
	private const double PollInterval = 0.5;
	private const double FlushInterval = 0.25;
	private const double HeartbeatInterval = 1.0;
	private const double RescanInterval = 1.0;

	public static TeamCreateService Instance { get; private set; } = null!;

	private readonly BVHttpClient _http = new();
	private readonly Dictionary<string, NetworkedObject> _observed = [];
	private readonly Dictionary<string, JsonObject> _pendingChanges = [];
	private readonly List<TeamCreateMember> _members = [];
	private long _universeId;
	private string _memberId = "";
	private string _localUserId = "";
	private long _sequence;
	private bool _enabled;
	private bool _joining;
	private bool _requestActive;
	private bool _applyingRemote;
	private double _pollElapsed;
	private double _flushElapsed;
	private double _heartbeatElapsed;
	private double _rescanElapsed;
	private double _reconnectElapsed;
	private TeamCreateSessionWindow? _window;
	private string _followMemberId = "";

	public bool Connected => _enabled && !string.IsNullOrWhiteSpace(_memberId);
	public IReadOnlyList<TeamCreateMember> Members => _members;
	public string FollowedMemberId => _followMemberId;

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
		long currentUniverse = World.Current?.UniverseID ?? 0;
		if (currentUniverse != _universeId)
		{
			_ = SwitchUniverse(currentUniverse);
			return;
		}

		if (!Connected)
		{
			if (currentUniverse != 0)
			{
				_reconnectElapsed += delta;
				if (_reconnectElapsed >= 10.0)
				{
					_reconnectElapsed = 0;
					_ = SwitchUniverse(currentUniverse);
				}
			}
			return;
		}
		_reconnectElapsed = 0;
		if (_requestActive) return;
		UpdateFollowCamera((float)delta);

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

	public override void _ExitTree()
	{
		_ = Leave();
		UnobserveAll();
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
	}

	public void FollowMember(string memberId)
	{
		_followMemberId = memberId;
		UpdateFollowCamera(1f);
		_window?.Refresh();
	}

	public void StopFollowing()
	{
		_followMemberId = "";
		_window?.Refresh();
	}

	private void UpdateFollowCamera(float delta)
	{
		if (string.IsNullOrWhiteSpace(_followMemberId)) return;
		TeamCreateMember? member = _members.FirstOrDefault(item => item.Id == _followMemberId);
		Camera3D? camera = World.Current?.CreatorContext?.Freelook?.Camera3D;
		if (member?.Camera == null || camera == null) return;
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
			_sequence = 0;
			_localUserId = "";
			_members.Clear();
			_followMemberId = "";
			_pendingChanges.Clear();
			if (universeId == 0 || string.IsNullOrWhiteSpace(CreatorAPI.Token)) return;
			_http.DefaultRequestHeaders["Authorization"] = "Bearer " + CreatorAPI.Token;

			using HttpResponseMessage config = await _http.GetAsync(ApiPath(""));
			if (!config.IsSuccessStatusCode) return;
			using JsonDocument configJson = JsonDocument.Parse(await config.Content.ReadAsStringAsync());
			_enabled =
				configJson.RootElement.TryGetProperty("enabled", out JsonElement enabled)
				&& enabled.GetBoolean();
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
			_enabled = false;
			_memberId = "";
		}
		finally
		{
			_joining = false;
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
				CallDeferred(MethodName.DisableFromServer);
				return;
			}
			if (!response.IsSuccessStatusCode) return;
			string body = await response.Content.ReadAsStringAsync();
			CallDeferred(MethodName.ApplyPollResponse, requestedUniverse, body);
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
		Camera3D? camera = World.Current?.CreatorContext?.Freelook?.Camera3D;
		JsonObject payload = new() { ["memberId"] = _memberId };
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
			if (!response.IsSuccessStatusCode) return;
			using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			if (json.RootElement.TryGetProperty("session", out JsonElement session))
				CallDeferred(MethodName.ApplySessionResponse, session.GetRawText());
		}
		catch (Exception error)
		{
			BV.PrintErr("Team Create heartbeat failed: ", error.Message);
		}
	}

	private async Task FlushChanges()
	{
		JsonArray changes = [];
		foreach (JsonObject change in _pendingChanges.Values) changes.Add(change.DeepClone());
		_pendingChanges.Clear();
		if (changes.Count == 0) return;

		try
		{
			using HttpResponseMessage response = await SendJson(
				HttpMethod.Post,
				ApiPath("/changes"),
				new JsonObject { ["memberId"] = _memberId, ["changes"] = changes }
			);
			if (!response.IsSuccessStatusCode)
				BV.PrintErr("Team Create rejected changes: ", await response.Content.ReadAsStringAsync());
		}
		catch (Exception error)
		{
			BV.PrintErr("Team Create change upload failed: ", error.Message);
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
		_window?.Refresh();
	}

	private void ObserveWorld()
	{
		World? world = World.Current;
		if (world == null) return;
		Observe(world);
		foreach (NetworkedObject item in world.NetworkObjects.Values.ToArray()) Observe(item);
	}

	private void Observe(NetworkedObject item)
	{
		string id = item.NetworkedObjectID;
		if (string.IsNullOrWhiteSpace(id) || _observed.ContainsKey(id)) return;
		_observed[id] = item;
		item.PropertyChanged.Connect(property => OnPropertyChanged(item, property?.ToString() ?? ""));
		item.Deleted += () =>
		{
			if (_applyingRemote || !Connected) return;
			QueueChange(
				"delete:" + id,
				new JsonObject { ["kind"] = "delete", ["id"] = id }
			);
		};
		if (item is Instance instance)
			instance.ChildAdded.Connect(child =>
			{
				if (child is Instance instanceChild) OnChildAdded(instance, instanceChild);
			});
	}

	private void OnChildAdded(Instance parent, Instance child)
	{
		if (_applyingRemote || !Connected || child.Root != World.Current) return;
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
		JsonObject change = new()
		{
			["kind"] = "create",
			["id"] = child.NetworkedObjectID,
			["parentId"] = parent.NetworkedObjectID,
			["className"] = child.ClassName,
			["name"] = child.Name,
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
		bool flag => JsonValue.Create(flag),
		byte number => JsonValue.Create(number),
		short number => JsonValue.Create(number),
		int number => JsonValue.Create(number),
		long number => JsonValue.Create(number),
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
		Color color => new JsonObject
		{
			["$type"] = "color",
			["value"] = new JsonArray(color.R, color.G, color.B, color.A),
		},
		_ => null,
	};

	private static object? DecodeValue(JsonElement value, Type type)
	{
		if (value.ValueKind == JsonValueKind.Null) return null;
		if (type == typeof(string)) return value.GetString();
		if (type == typeof(bool)) return value.GetBoolean();
		if (type == typeof(byte)) return value.GetByte();
		if (type == typeof(short)) return value.GetInt16();
		if (type == typeof(int)) return value.GetInt32();
		if (type == typeof(long)) return value.GetInt64();
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
		if (type == typeof(Color))
		{
			float[] values = value.GetProperty("value").EnumerateArray().Select(item => item.GetSingle()).ToArray();
			return new Color(values[0], values[1], values[2], values[3]);
		}
		return null;
	}
}

public sealed class TeamCreateMember
{
	public string Id { get; init; } = "";
	public string UserId { get; init; } = "";
	public string Username { get; init; } = "";
	public TeamCreateCamera? Camera { get; set; }
}

public sealed class TeamCreateCamera
{
	public Vector3 Position { get; init; }
	public Vector3 Rotation { get; init; }
}
