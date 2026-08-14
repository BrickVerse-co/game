// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Net.Http;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Shared;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileMarketplaceItem : MobileViewBase
{
	private string _itemId = "";
	private JsonElement _item;
	private TextureRect _preview = null!;
	private Button _buy = null!;
	private Button _previewToggle = null!;
	private SubViewportContainer _preview3D = null!;
	private Node3D _previewRoot = null!;
	private Camera3D _previewCamera = null!;
	private Control _layout = null!;
	private Control _loadingSkeleton = null!;
	private Tween? _skeletonTween;
	private int _loadVersion;
	private bool _showing3D;
	private bool _mouseDragging;
	private float _cameraDistance = 4f;
	private float _cameraHeight;
	private readonly Dictionary<int, Vector2> _touches = new();
	private Control _cameraControls = null!;
	private static readonly System.Net.Http.HttpClient PreviewClient = new() { Timeout = TimeSpan.FromSeconds(25) };

	public override void _Ready()
	{
		_preview = GetNode<TextureRect>("Layout/PreviewFrame/Padding/Preview");
		_buy = GetNode<Button>("Layout/Buy");
		_previewToggle = GetNode<Button>("Layout/PreviewFrame/Overlay/PreviewToggle");
		_preview3D = GetNode<SubViewportContainer>("Layout/PreviewFrame/Padding/Preview3D");
		_previewRoot = GetNode<Node3D>("Layout/PreviewFrame/Padding/Preview3D/Viewport/PreviewRoot");
		_previewCamera = GetNode<Camera3D>("Layout/PreviewFrame/Padding/Preview3D/Viewport/Camera");
		_cameraControls = GetNode<Control>("Layout/PreviewFrame/Overlay/CameraControls");
		_layout = GetNode<Control>("Layout");
		_loadingSkeleton = GetNode<Control>("LoadingSkeleton");
		_previewToggle.Pressed += TogglePreview;
		_preview3D.GuiInput += HandlePreviewInput;
		Button zoomOut = GetNode<Button>("Layout/PreviewFrame/Overlay/CameraControls/ZoomOut");
		Button zoomIn = GetNode<Button>("Layout/PreviewFrame/Overlay/CameraControls/ZoomIn");
		zoomOut.Pressed += () => ZoomBy(1.18f);
		zoomIn.Pressed += () => ZoomBy(0.78f);
		MobileMotion.Bind(zoomOut);
		MobileMotion.Bind(zoomIn);
		MobileMotion.Bind(_previewToggle);
		MobileMotion.Bind(_buy);
		MobileMotion.Bind(GetNode<Button>("Layout/Header/Back"));
		GetNode<Button>("Layout/Header/Back").Pressed += () => MobileUI.Singleton.SwitchTo(MobileViewEnum.Store, MobileViewEnum.Store);
		_buy.Pressed += Buy;
		GetNode<Button>("Layout/Report").Pressed += () => OS.ShellOpen(Globals.MainEndpoint.PathJoin($"/report?type=marketplace_item&id={Uri.EscapeDataString(_itemId)}"));
		GetNode<Button>("Layout/Creator/Name").Pressed += OpenCreator;
	}

	public override async void ShowView(object? args)
	{
		int version = ++_loadVersion;
		_itemId = args?.ToString() ?? "";
		ShowSkeleton();
		_showing3D = false;
		_preview.Visible = true;
		_preview3D.Visible = false;
		_previewToggle.Visible = false;
		_cameraControls.Visible = false;
		_touches.Clear();
		_buy.Disabled = true;
		try
		{
			using JsonDocument response = await BVAPI.GetJson("/v3/marketplace/" + Uri.EscapeDataString(_itemId));
			_item = response.RootElement.GetProperty("item").Clone();
			string name = Read("name", "Marketplace item");
			int price = Number("price");
			bool owned = Bool("isOwned");
			GetNode<Label>("Layout/Title").Text = name;
			GetNode<Label>("Layout/Type").Text = Read("type", "Accessory").Replace('_', ' ') + (Bool("isFeatured") ? "  •  Featured" : "");
			GetNode<MobileMarkdown>("Layout/Description").SetMarkdown(Read("description", "No description provided."));
			GetNode<Label>("Layout/Stats").Text = $"{Number("sales"):N0} sold  •  {Number("favorites"):N0} favorites" + (Bool("isLimited") ? $"  •  {Number("remainingStock"):N0} remaining" : "");
			GetNode<Button>("Layout/Creator/Name").Text = "Created by " + Read("creatorName", "BrickVerse creator");
			GetNode<TextureRect>("Layout/Creator/Verified").Visible = Bool("creatorVerified");
			GetNode<Label>("Layout/Tags").Text = ReadTags();
			_buy.Text = owned ? "Owned" : Bool("isForSale") ? price == 0 ? "Get" : $"Buy for {price:N0} Cubes" : "Off sale";
			_buy.Disabled = owned || !Bool("isForSale");
			string url = await BVAPI.ResolveThumbnailUrl("MARKETPLACE_ITEM", _itemId);
			if (!string.IsNullOrWhiteSpace(url)) await LoadPreviewImage(url, version);
			HideSkeleton(version);
			using JsonDocument preview3d = await BVAPI.GetJson($"/v3/marketplace/{Uri.EscapeDataString(_itemId)}/3d");
			if (preview3d.RootElement.TryGetProperty("item", out JsonElement data3d)
				&& data3d.TryGetProperty("meshUrl", out JsonElement mesh) && mesh.ValueKind == JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(mesh.GetString())) await Load3DPreview(mesh.GetString()!);
		}
		catch (Exception exception) { HideSkeleton(version); GetNode<MobileMarkdown>("Layout/Description").SetMarkdown("This item could not be loaded."); BV.PrintErr(exception); }
	}

	private void OpenCreator()
	{
		string creatorId = Read("creatorId", "");
		if (string.IsNullOrWhiteSpace(creatorId)) return;
		if (Read("creatorType", "USER").Equals("GUILD", StringComparison.OrdinalIgnoreCase))
			MobileUI.Singleton.SwitchTo(MobileViewEnum.RecordDetail,
				new MobileRecordDetailArgs(Read("creatorName", "Guild"), "Marketplace creator", "View this guild and its creations in BrickVerse.", "", MobileViewEnum.Store, creatorId));
		else MobileUI.Singleton.SwitchTo(MobileViewEnum.Profile, creatorId);
	}

	private void ShowSkeleton()
	{
		_skeletonTween?.Kill();
		_layout.Visible = false;
		_loadingSkeleton.Visible = true;
		_loadingSkeleton.Modulate = Colors.White;
		_skeletonTween = CreateTween().SetLoops();
		_skeletonTween.TweenProperty(_loadingSkeleton, "modulate:a", 0.62f, 0.7).SetTrans(Tween.TransitionType.Sine);
		_skeletonTween.TweenProperty(_loadingSkeleton, "modulate:a", 1f, 0.7).SetTrans(Tween.TransitionType.Sine);
	}

	private async System.Threading.Tasks.Task LoadPreviewImage(string url, int version)
	{
		var loaded = new System.Threading.Tasks.TaskCompletionSource<bool>();
		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, resource =>
		{
			if (version == _loadVersion && IsInstanceValid(_preview)) _preview.Texture = (Texture2D)resource;
			loaded.TrySetResult(true);
		});
		await System.Threading.Tasks.Task.WhenAny(loaded.Task, System.Threading.Tasks.Task.Delay(3000));
	}

	private void HideSkeleton(int version)
	{
		if (version != _loadVersion || !IsInstanceValid(_loadingSkeleton)) return;
		_skeletonTween?.Kill();
		_layout.Visible = true;
		Tween fade = CreateTween();
		fade.TweenProperty(_loadingSkeleton, "modulate:a", 0f, 0.16);
		fade.TweenCallback(Callable.From(() => { if (IsInstanceValid(_loadingSkeleton)) _loadingSkeleton.Visible = false; }));
	}

	public override void _Process(double delta)
	{
		if (_showing3D && IsInstanceValid(_previewRoot)) _previewRoot.RotateY((float)delta * 0.55f);
	}

	private void TogglePreview()
	{
		_showing3D = !_showing3D;
		_preview.Visible = !_showing3D;
		_preview3D.Visible = _showing3D;
		_cameraControls.Visible = _showing3D;
		_previewToggle.Text = _showing3D ? "2D" : "3D";
	}

	private void HandlePreviewInput(InputEvent input)
	{
		if (!_showing3D) return;
		if (input is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left) _mouseDragging = mouseButton.Pressed;
			else if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelUp) ZoomBy(0.9f);
			else if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelDown) ZoomBy(1.1f);
		}
		else if (input is InputEventMouseMotion mouseMotion && _mouseDragging)
		{
			RotatePreview(mouseMotion.Relative);
		}
		else if (input is InputEventScreenTouch touch)
		{
			if (touch.Pressed) _touches[touch.Index] = touch.Position;
			else _touches.Remove(touch.Index);
		}
		else if (input is InputEventScreenDrag drag)
		{
			if (_touches.Count >= 2 && _touches.TryGetValue(drag.Index, out Vector2 previous))
			{
				int otherIndex = _touches.Keys.First(index => index != drag.Index);
				Vector2 other = _touches[otherIndex];
				float oldDistance = previous.DistanceTo(other);
				float newDistance = drag.Position.DistanceTo(other);
				if (oldDistance > 8f && newDistance > 8f) ZoomBy(oldDistance / newDistance);
			}
			else RotatePreview(drag.Relative);
			_touches[drag.Index] = drag.Position;
		}
		else if (input is InputEventMagnifyGesture magnify)
		{
			ZoomBy(1f / Mathf.Max(magnify.Factor, 0.1f));
		}
	}

	private void RotatePreview(Vector2 delta)
	{
		_previewRoot.RotateY(-delta.X * 0.009f);
		_cameraHeight = Mathf.Clamp(_cameraHeight + delta.Y * 0.006f * _cameraDistance, -_cameraDistance * 0.65f, _cameraDistance * 0.65f);
		ApplyCamera();
	}

	private void ZoomBy(float factor)
	{
		_cameraDistance = Mathf.Clamp(_cameraDistance * factor, 0.28f, 12f);
		_cameraHeight = Mathf.Clamp(_cameraHeight, -_cameraDistance * 0.65f, _cameraDistance * 0.65f);
		ApplyCamera();
	}

	private void ApplyCamera()
	{
		_previewCamera.Position = new Vector3(0f, _cameraHeight, _cameraDistance);
		_previewCamera.LookAt(Vector3.Zero);
	}

	private async System.Threading.Tasks.Task Load3DPreview(string meshUrl)
	{
		try
		{
			byte[] bytes = await PreviewClient.GetByteArrayAsync(meshUrl);
			GltfDocument document = new();
			GltfState state = new();
			if (document.AppendFromBuffer(bytes, "", state) != Error.Ok) return;
			Node scene = document.GenerateScene(state);
			foreach (Node child in _previewRoot.GetChildren()) child.QueueFree();
			_previewRoot.AddChild(scene);
			Aabb? bounds = FindBounds(scene);
			if (bounds.HasValue)
			{
				Vector3 center = bounds.Value.GetCenter();
				float radius = Mathf.Max(bounds.Value.Size.Length() * 0.65f, 0.5f);
				_previewRoot.Position = -center;
				_cameraDistance = Mathf.Clamp(radius * 2.4f, 0.28f, 12f);
				_cameraHeight = radius * 0.25f;
				ApplyCamera();
			}
			_previewToggle.Visible = true;
		}
		catch (Exception exception) { BV.PrintErr("Marketplace 3D preview failed: ", exception.Message); }
	}

	private static Aabb? FindBounds(Node node)
	{
		Aabb? result = null;
		foreach (Node candidate in node.FindChildren("*", "MeshInstance3D", true, false))
			if (candidate is MeshInstance3D mesh && mesh.Mesh != null)
			{
				Aabb bounds = mesh.GlobalTransform * mesh.GetAabb();
				result = result.HasValue ? result.Value.Merge(bounds) : bounds;
			}
		return result;
	}

	private async void Buy()
	{
		_buy.Disabled = true;
		try { using JsonDocument _ = await BVAPI.SendJson(HttpMethod.Post, $"/v3/marketplace/{Uri.EscapeDataString(_itemId)}/buy"); _buy.Text = "Owned"; }
		catch (Exception exception) { OS.Alert(exception.Message, "Purchase failed"); _buy.Disabled = false; }
	}
	private string Read(string key, string fallback) => _item.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
	private int Number(string key) => _item.TryGetProperty(key, out JsonElement value) && value.TryGetInt32(out int number) ? number : 0;
	private bool Bool(string key) => _item.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.True;
	private string ReadTags() => _item.TryGetProperty("tags", out JsonElement tags) && tags.ValueKind == JsonValueKind.Array ? string.Join("  •  ", tags.EnumerateArray().Select(tag => tag.GetString())) : "";
}
