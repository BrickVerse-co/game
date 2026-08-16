using Godot;
using BrickVerse.Schemas.API;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;
using System;
using System.Text.Json;

namespace BrickVerse.Creator.UI;

/// <summary>Detached Toolbox details and preview window.</summary>
public sealed partial class ToolboxAssetDetailsWindow : Window
{
	private readonly APILibraryItem _item;
	private readonly LibraryQueryTypeEnum _type;
	private Label _description = null!;
	private Label _stats = null!;
	private Button _primary = null!;
	private VBoxContainer _comments = null!;
	private SubViewport _viewport = null!;
	private Node3D _previewRoot = null!;

	private ToolboxAssetDetailsWindow(APILibraryItem item, LibraryQueryTypeEnum type) { _item = item; _type = type; }

	public static void Open(APILibraryItem item, LibraryQueryTypeEnum type)
	{
		ToolboxAssetDetailsWindow window = new(item, type);
		CreatorService.Interface.PopupWindow(window);
		window.PopupCentered(new Vector2I(900, 620));
	}

	public override void _Ready()
	{
		Title = _item.Name; MinSize = new Vector2I(700, 500); CloseRequested += QueueFree;
		VBoxContainer root = new(); AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.AddThemeConstantOverride("separation", 12);
		HBoxContainer body = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; root.AddChild(body);
		Control preview = CreatePreview(); preview.CustomMinimumSize = new Vector2(440, 390); preview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; body.AddChild(preview);
		VBoxContainer info = new() { CustomMinimumSize = new Vector2(320, 0) }; body.AddChild(info);
		Label title = new() { Text = _item.Name }; title.AddThemeFontSizeOverride("font_size", 24); info.AddChild(title);
		info.AddChild(new Label { Text = $"By {_item.CreatorName}  •  {_type}", Modulate = new Color(0.72f, 0.76f, 0.82f) });
		_description = new Label { Text = "Loading details...", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; info.AddChild(_description);
		_stats = new Label { Text = "" }; info.AddChild(_stats);
		info.AddChild(new Label { Text = "Recent comments & reviews" });
		_comments = new VBoxContainer(); info.AddChild(_comments);
		_comments.AddChild(new Label { Text = "Loading comments...", Modulate = new Color(0.7f, 0.72f, 0.76f) });
		Button reviews = new() { Text = "View all comments" }; reviews.Pressed += () => OS.ShellOpen(Globals.MainEndpoint.PathJoin("/assets/" + _item.ID + "#comments")); info.AddChild(reviews);
		HBoxContainer actions = new(); root.AddChild(actions);
		Button report = new() { Text = "Report" }; report.Pressed += () => OS.ShellOpen(Globals.MainEndpoint.PathJoin($"/report?type=asset&id={_item.ID}")); actions.AddChild(report);
		Button web = new() { Text = "View on BrickVerse" }; web.Pressed += () => OS.ShellOpen(Globals.MainEndpoint.PathJoin("/assets/" + _item.ID)); actions.AddChild(web);
		Control spacer = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; actions.AddChild(spacer);
		_primary = new Button { Text = "Insert", CustomMinimumSize = new Vector2(130, 38) }; _primary.Pressed += OnPrimary; actions.AddChild(_primary);
		_ = LoadDetails(); _ = LoadComments();
	}

	private Control CreatePreview()
	{
		SubViewportContainer container = new() { Stretch = true };
		_viewport = new SubViewport { Size = new Vector2I(640, 480), TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
		container.AddChild(_viewport); _previewRoot = new Node3D(); _viewport.AddChild(_previewRoot);
		_viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.055f, 0.065f, 0.08f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 1.2f } });
		_viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -35, 0), ShadowEnabled = true });
		_viewport.AddChild(new Camera3D { Position = new Vector3(0, 1.5f, 5), Current = true });
		TextureRect fallback = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
		container.AddChild(fallback); fallback.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		WebAssetLoader.Singleton.GetResource(new() { URL = _item.ThumbnailUrl }, resource => fallback.Texture = resource as Texture2D);
		if (_type is LibraryQueryTypeEnum.Mesh or LibraryQueryTypeEnum.Model) _ = Load3DPreview(fallback);
		return container;
	}

	private async System.Threading.Tasks.Task Load3DPreview(TextureRect fallback)
	{
		try
		{
			APIMarketplace3DResponse data = await BVAPI.GetMarketplace3D(_item.ID);
			string? meshId = data.Item.MeshId;
			if (string.IsNullOrWhiteSpace(meshId)) return;
			AssetLoader.Singleton.GetResource(new() { ID = meshId, Type = ResourceType.Mesh }, resource =>
			{
				if (!IsInstanceValid(this) || resource is not Godot.Mesh mesh) return;
				MeshInstance3D instance = new() { Mesh = mesh }; _previewRoot.AddChild(instance); fallback.Visible = false;
				Aabb bounds = mesh.GetAabb(); float radius = Mathf.Max(bounds.Size.Length() * 0.65f, 1f);
				Camera3D camera = _viewport.GetCamera3D()!; camera.Position = bounds.GetCenter() + new Vector3(radius, radius * 0.55f, radius); camera.LookAt(bounds.GetCenter());
			});
		}
		catch (Exception error) { BV.PrintErr("Toolbox 3D preview unavailable: ", error.Message); }
	}

	private async System.Threading.Tasks.Task LoadDetails()
	{
		try
		{
			APIStoreItem detail = await BVAPI.GetStoreItem(_item.ID);
			if (!IsInstanceValid(this)) return;
			_description.Text = string.IsNullOrWhiteSpace(detail.Description) ? "No description provided." : detail.Description;
			_stats.Text = $"{detail.Sales ?? 0:N0} sales  •  {detail.Favorites ?? 0:N0} favorites  •  {(detail.Price is > 0 ? detail.Price + " Cubes" : "Free")}";
			if (detail.Price is > 0) _primary.Text = $"Buy • {detail.Price} Cubes";
		}
		catch (Exception error) { _description.Text = "Details could not be loaded."; BV.PrintErr(error); }
	}

	private async System.Threading.Tasks.Task LoadComments()
	{
		try
		{
			using JsonDocument response = await BVAPI.GetJson($"/v3/comments/{Uri.EscapeDataString(_item.ID)}?limit=3&itemType=DEVELOPER_ASSET");
			if (!IsInstanceValid(this)) return;
			foreach (Node child in _comments.GetChildren()) child.QueueFree();
			JsonElement comments = response.RootElement.GetProperty("comments");
			if (comments.GetArrayLength() == 0) { _comments.AddChild(new Label { Text = "No comments yet." }); return; }
			foreach (JsonElement comment in comments.EnumerateArray())
			{
				string username = comment.GetProperty("user").GetProperty("username").GetString() ?? "User";
				string content = comment.GetProperty("content").GetString() ?? "";
				_comments.AddChild(new Label { Text = $"{username}: {content}", AutowrapMode = TextServer.AutowrapMode.WordSmart, TooltipText = content });
			}
		}
		catch { if (IsInstanceValid(this)) { foreach (Node child in _comments.GetChildren()) child.QueueFree(); _comments.AddChild(new Label { Text = "Comments unavailable." }); } }
	}

	private async void OnPrimary()
	{
		if (_primary.Text.StartsWith("Buy", StringComparison.Ordinal)) { OS.ShellOpen(Globals.MainEndpoint.PathJoin("/assets/" + _item.ID)); return; }
		_primary.Disabled = true; await ToolboxCard.InsertAssetAsync(_item, _type); if (IsInstanceValid(this)) QueueFree();
	}
}
