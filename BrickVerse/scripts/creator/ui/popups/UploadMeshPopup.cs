// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Creator.Utils;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class UploadMeshPopup : PopupWindowBase
{
	public enum MeshOwnerType
	{
		User,
		Guild
	}

	public sealed class GuildOption
	{
		public string Id { get; init; } = "";
		public string Name { get; init; } = "";
	}

	public sealed class MeshUploadRequest
	{
		public string Name { get; init; } = "";
		public string Description { get; init; } = "";
		public string SourceFileName { get; init; } = "";
		public string SourceExtension { get; init; } = "";
		public MeshOwnerType OwnerType { get; init; }
		public string OwnerId { get; init; } = "";
		public string? GuildId { get; init; }
		public int SurfaceCount { get; init; }
		public long VertexCount { get; init; }
		public long TriangleCount { get; init; }
		public byte[] SerializedMesh { get; init; } = [];
	}

	public event Action<MeshUploadRequest>? UploadRequested;
	public event Action? Closed;

	[Export] private Button _closeButton = null!;
	[Export] private Button _chooseFileButton = null!;
	[Export] private Button _replaceFileButton = null!;
	[Export] private Button _personalOwnerButton = null!;
	[Export] private Button _guildOwnerButton = null!;
	[Export] private OptionButton _guildDropdown = null!;
	[Export] private LineEdit _nameInput = null!;
	[Export] private TextEdit _descriptionInput = null!;
	[Export] private Button _uploadButton = null!;
	[Export] private Button _cancelButton = null!;
	[Export] private Label _fileNameLabel = null!;
	[Export] private Label _fileTypeLabel = null!;
	[Export] private Label _surfaceCountLabel = null!;
	[Export] private Label _vertexCountLabel = null!;
	[Export] private Label _triangleCountLabel = null!;
	[Export] private Label _emptyPreviewLabel = null!;
	[Export] private Label _errorLabel = null!;
	[Export] private Label _busyLabel = null!;
	[Export] private SubViewport _previewViewport = null!;
	[Export] private Node3D _previewRoot = null!;
	[Export] private Camera3D _previewCamera = null!;
	[Export] private FileDialog _fileDialog = null!;

	private readonly List<GuildOption> _guilds = [];
	private ArrayMesh? _mesh;
	private string _sourcePath = "";
	private long _vertexCount;
	private long _triangleCount;
	private bool _isBusy;
	private bool _draggingPreview;
	private Vector2 _lastMousePosition;
	private float _previewYaw = -0.55f;
	private float _previewPitch = -0.25f;
	private float _previewDistance = 4.0f;

	public override void _Ready()
	{
		base._Ready();
		ResolveNodeReferences();

		CloseRequested += Close;
		_closeButton.Pressed += Close;
		_cancelButton.Pressed += Close;
		_chooseFileButton.Pressed += OpenFilePicker;
		_replaceFileButton.Pressed += OpenFilePicker;
		_uploadButton.Pressed += Submit;
		_personalOwnerButton.Pressed += () => SetOwnerType(MeshOwnerType.User);
		_guildOwnerButton.Pressed += () => SetOwnerType(MeshOwnerType.Guild);
		_fileDialog.FileSelected += LoadSelectedFile;
		_previewViewport.GuiEmbedSubwindows = true;

		ResetForm();
	}

	public async void Open()
	{
		Show();
		ResetForm();

		try
		{
			CreatorGuildItem[] creatorGuildItems = await CreatorAPI.GetUserGuilds(limitToEditable: true);
			SetGuilds(creatorGuildItems.Select(g => new GuildOption { Id = g.Id, Name = g.Name }));
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to load upload guilds: {ex.Message}");
			SetGuilds([]);
		}

		OpenFilePicker();
	}

	public void Close()
	{
		if (_isBusy)
			return;

		Hide();
		Closed?.Invoke();
	}

	public override void _Process(double delta)
	{
		if (_mesh == null)
			return;

		if (!_draggingPreview)
			_previewYaw += (float)delta * 0.18f;

		UpdatePreviewCamera();
	}

	public override void _UnhandledInput(InputEvent inputEvent)
	{
		if (!Visible || _mesh == null)
			return;

		if (inputEvent is InputEventMouseButton button)
		{
			if (button.ButtonIndex == MouseButton.Left)
			{
				_draggingPreview = button.Pressed;
				_lastMousePosition = button.Position;
			}
			else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
			{
				_previewDistance = Mathf.Max(1.1f, _previewDistance - 0.3f);
			}
			else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
			{
				_previewDistance = Mathf.Min(20.0f, _previewDistance + 0.3f);
			}
		}
		else if (inputEvent is InputEventMouseMotion motion && _draggingPreview)
		{
			Vector2 delta = motion.Position - _lastMousePosition;
			_lastMousePosition = motion.Position;
			_previewYaw -= delta.X * 0.01f;
			_previewPitch = Mathf.Clamp(_previewPitch - delta.Y * 0.01f, -1.25f, 1.25f);
		}
	}

	public void SetGuilds(IEnumerable<GuildOption> guilds)
	{
		_guilds.Clear();
		_guildDropdown.Clear();

		foreach (GuildOption guild in guilds)
		{
			if (string.IsNullOrWhiteSpace(guild.Id))
				continue;

			_guilds.Add(guild);
			_guildDropdown.AddItem(guild.Name);
		}

		if (_guilds.Count > 0)
			_guildDropdown.Select(0);

		RefreshOwnerState();
	}

	private void OpenFilePicker()
	{
		if (_isBusy)
			return;

		_fileDialog.PopupCenteredRatio(0.72f);
	}

	private void LoadSelectedFile(string path)
	{
		HideError();
		SetBusy(true, "Importing mesh...");

		try
		{
			ArrayMesh imported = ImportMesh(path);
			SetMesh(imported, path);
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to import mesh '{path}': {ex}");
			ShowError("The selected model could not be imported. " + ex.Message);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private static ArrayMesh ImportMesh(string path)
	{
		string extension = Path.GetExtension(path).ToLowerInvariant();
		if (extension is not ".obj" and not ".fbx" and not ".glb" and not ".gltf")
			throw new InvalidOperationException("Choose an OBJ, FBX, GLB, or GLTF model.");
		if (extension == ".obj")
			return WavefrontObjImporter.Import(path);

		Resource? loaded = ResourceLoader.Load(path, cacheMode: ResourceLoader.CacheMode.Ignore);
		if (loaded is Godot.Mesh directMesh)
			return CopyMesh(directMesh);

		if (loaded is PackedScene packedScene)
		{
			Node root = packedScene.Instantiate();
			try
			{
				return CombineSceneMeshes(root);
			}
			finally
			{
				root.Free();
			}
		}

		throw new InvalidOperationException("Godot did not return a mesh or model scene. Ensure the runtime build includes the required model importer.");
	}

	private static ArrayMesh CopyMesh(Godot.Mesh source)
	{
		ArrayMesh result = new();
		for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
		{
			Godot.Collections.Array arrays = source.SurfaceGetArrays(surface);
			result.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
			Material? material = source.SurfaceGetMaterial(surface);
			if (material != null)
				result.SurfaceSetMaterial(result.GetSurfaceCount() - 1, material);
		}
		return result;
	}

	private static ArrayMesh CombineSceneMeshes(Node root)
	{
		ArrayMesh result = new();
		AppendMeshesRecursive(root, Transform3D.Identity, result);

		if (result.GetSurfaceCount() == 0)
			throw new InvalidOperationException("No mesh surfaces were found in the selected model.");

		return result;
	}

	private static void AppendMeshesRecursive(Node node, Transform3D parentTransform, ArrayMesh destination)
	{
		Transform3D currentTransform = parentTransform;
		if (node is Node3D node3D)
			currentTransform = parentTransform * node3D.Transform;

		if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
		{
			Godot.Mesh source = meshInstance.Mesh;
			for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
			{
				Godot.Collections.Array arrays = source.SurfaceGetArrays(surface);
				TransformSurfaceArrays(arrays, currentTransform);
				destination.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);

				Material? material = meshInstance.GetSurfaceOverrideMaterial(surface) ?? source.SurfaceGetMaterial(surface);
				if (material != null)
					destination.SurfaceSetMaterial(destination.GetSurfaceCount() - 1, material);
			}
		}

		foreach (Node child in node.GetChildren())
			AppendMeshesRecursive(child, currentTransform, destination);
	}

	private static void TransformSurfaceArrays(Godot.Collections.Array arrays, Transform3D transform)
	{
		if (arrays.Count <= (int)Godot.Mesh.ArrayType.Vertex)
			return;

		Variant verticesVariant = arrays[(int)Godot.Mesh.ArrayType.Vertex];
		if (verticesVariant.VariantType == Variant.Type.PackedVector3Array)
		{
			Vector3[] vertices = verticesVariant.AsVector3Array();
			for (int i = 0; i < vertices.Length; i++)
				vertices[i] = transform * vertices[i];
			arrays[(int)Godot.Mesh.ArrayType.Vertex] = vertices;
		}

		if (arrays.Count > (int)Godot.Mesh.ArrayType.Normal)
		{
			Variant normalsVariant = arrays[(int)Godot.Mesh.ArrayType.Normal];
			if (normalsVariant.VariantType == Variant.Type.PackedVector3Array)
			{
				Vector3[] normals = normalsVariant.AsVector3Array();
				Basis normalBasis = transform.Basis.Inverse().Transposed();
				for (int i = 0; i < normals.Length; i++)
					normals[i] = (normalBasis * normals[i]).Normalized();
				arrays[(int)Godot.Mesh.ArrayType.Normal] = normals;
			}
		}
	}

	private void SetMesh(ArrayMesh mesh, string sourcePath)
	{
		ClearPreview();
		_mesh = mesh;
		_sourcePath = sourcePath;
		(_vertexCount, _triangleCount) = CalculateMeshStats(mesh);

		MeshInstance3D previewMesh = new()
		{
			Name = "UploadedMeshPreview",
			Mesh = mesh
		};
		_previewRoot.AddChild(previewMesh);

		Aabb bounds = mesh.GetAabb();
		Vector3 center = bounds.GetCenter();
		previewMesh.Position = -center;
		float largestAxis = Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z));
		_previewDistance = Mathf.Clamp(largestAxis * 1.8f, 2.2f, 18.0f);
		_previewYaw = -0.55f;
		_previewPitch = -0.25f;
		UpdatePreviewCamera();

		string fileName = Path.GetFileName(sourcePath);
		_fileNameLabel.Text = fileName;
		_fileTypeLabel.Text = Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant();
		_surfaceCountLabel.Text = mesh.GetSurfaceCount().ToString("N0");
		_vertexCountLabel.Text = _vertexCount.ToString("N0");
		_triangleCountLabel.Text = _triangleCount.ToString("N0");
		_emptyPreviewLabel.Visible = false;
		_replaceFileButton.Visible = true;

		if (string.IsNullOrWhiteSpace(_nameInput.Text))
			_nameInput.Text = Path.GetFileNameWithoutExtension(sourcePath);

		RefreshSubmitState();
	}

	private static (long Vertices, long Triangles) CalculateMeshStats(Godot.Mesh mesh)
	{
		long vertices = 0;
		long triangles = 0;

		for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
		{
			Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
			Vector3[] surfaceVertices = arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();
			int[] indices = arrays[(int)Godot.Mesh.ArrayType.Index].AsInt32Array();
			vertices += surfaceVertices.LongLength;

			triangles += indices.Length > 0 ? indices.LongLength / 3 : surfaceVertices.LongLength / 3;
		}

		return (vertices, triangles);
	}

	private void UpdatePreviewCamera()
	{
		Vector3 direction = new(
			Mathf.Cos(_previewPitch) * Mathf.Sin(_previewYaw),
			Mathf.Sin(_previewPitch),
			Mathf.Cos(_previewPitch) * Mathf.Cos(_previewYaw)
		);
		_previewCamera.Position = direction * _previewDistance;
		_previewCamera.LookAt(Vector3.Zero, Vector3.Up);
	}

	private void ClearPreview()
	{
		foreach (Node child in _previewRoot.GetChildren())
			child.QueueFree();
	}

	private void Submit()
	{
		if (_isBusy || _mesh == null)
			return;

		HideError();
		string meshName = _nameInput.Text.Trim();
		string description = _descriptionInput.Text.Trim();

		if (string.IsNullOrWhiteSpace(meshName))
		{
			ShowError("Mesh name is required.");
			_nameInput.GrabFocus();
			return;
		}
		if (meshName.Length > 64)
		{
			ShowError("Mesh name must be 64 characters or less.");
			_nameInput.GrabFocus();
			return;
		}
		if (description.Length > 1000)
		{
			ShowError("Description must be 1,000 characters or less.");
			_descriptionInput.GrabFocus();
			return;
		}

		bool useGuild = _guildOwnerButton.ButtonPressed;
		string? guildId = null;
		if (useGuild)
		{
			int selected = _guildDropdown.Selected;
			if (selected < 0 || selected >= _guilds.Count)
			{
				ShowError("Select a valid guild.");
				return;
			}
			guildId = _guilds[selected].Id;
		}

		SetBusy(true, "Serializing mesh...");
		try
		{
			byte[] serializedMesh = SerializeMesh(_mesh);
			UploadRequested?.Invoke(new MeshUploadRequest
			{
				Name = meshName,
				Description = description,
				SourceFileName = Path.GetFileName(_sourcePath),
				SourceExtension = Path.GetExtension(_sourcePath).TrimStart('.').ToLowerInvariant(),
				OwnerType = useGuild ? MeshOwnerType.Guild : MeshOwnerType.User,
				OwnerId = useGuild ? _guilds[_guildDropdown.Selected].Id : CreatorAPI.UserID,
				GuildId = guildId,
				SurfaceCount = _mesh.GetSurfaceCount(),
				VertexCount = _vertexCount,
				TriangleCount = _triangleCount,
				SerializedMesh = serializedMesh
			});
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to serialize mesh: {ex}");
			ShowError("Failed to serialize the mesh. " + ex.Message);
			SetBusy(false);
		}
	}

	private static byte[] SerializeMesh(ArrayMesh mesh)
	{
		string temporaryPath = $"user://mesh-upload-{Guid.NewGuid():N}.res";
		Error saveError = ResourceSaver.Save(mesh, temporaryPath);
		if (saveError != Error.Ok)
			throw new IOException($"ResourceSaver returned {saveError}.");

		string globalPath = ProjectSettings.GlobalizePath(temporaryPath);
		try
		{
			return File.ReadAllBytes(globalPath);
		}
		finally
		{
			if (File.Exists(globalPath))
				File.Delete(globalPath);
		}
	}

	private void SetOwnerType(MeshOwnerType ownerType)
	{
		bool useGuild = ownerType == MeshOwnerType.Guild;
		_personalOwnerButton.ButtonPressed = !useGuild;
		_guildOwnerButton.ButtonPressed = useGuild;
		RefreshOwnerState();
	}

	private void RefreshOwnerState()
	{
		bool useGuild = _guildOwnerButton.ButtonPressed;
		_guildDropdown.Visible = useGuild;
		_guildDropdown.Disabled = _isBusy || _guilds.Count == 0;

		if (useGuild && _guilds.Count == 0)
			ShowError("You do not have upload permission in any guilds.");
		else if (_errorLabel.Text == "You do not have upload permission in any guilds.")
			HideError();
	}

	public void SetBusy(bool busy, string message = "Preparing upload...")
	{
		_isBusy = busy;
		_busyLabel.Text = message;
		_busyLabel.Visible = busy;
		_closeButton.Disabled = busy;
		_chooseFileButton.Disabled = busy;
		_replaceFileButton.Disabled = busy;
		_cancelButton.Disabled = busy;
		_nameInput.Editable = !busy;
		_descriptionInput.Editable = !busy;
		_personalOwnerButton.Disabled = busy;
		_guildOwnerButton.Disabled = busy;
		RefreshOwnerState();
		RefreshSubmitState();
	}

	private void RefreshSubmitState()
	{
		_uploadButton.Disabled = _isBusy || _mesh == null;
	}

	private void ShowError(string message)
	{
		_errorLabel.Text = message;
		_errorLabel.Visible = true;
	}

	private void HideError()
	{
		_errorLabel.Text = "";
		_errorLabel.Visible = false;
	}

	private void ResetForm()
	{
		_mesh = null;
		_sourcePath = "";
		_vertexCount = 0;
		_triangleCount = 0;
		_nameInput.Text = "";
		_descriptionInput.Text = "";
		_fileNameLabel.Text = "No model selected";
		_fileTypeLabel.Text = "—";
		_surfaceCountLabel.Text = "—";
		_vertexCountLabel.Text = "—";
		_triangleCountLabel.Text = "—";
		_emptyPreviewLabel.Visible = true;
		_replaceFileButton.Visible = false;
		ClearPreview();
		HideError();
		SetBusy(false);
		SetOwnerType(MeshOwnerType.User);
	}

	private void ResolveNodeReferences()
	{
		_closeButton ??= GetNode<Button>("Modal/Layout/Header/Row/Close");
		_chooseFileButton ??= GetNode<Button>("Modal/Layout/Body/Left/FileCard/Row/ChooseFile");
		_replaceFileButton ??= GetNode<Button>("Modal/Layout/Body/Left/FileCard/Row/ReplaceFile");
		_personalOwnerButton ??= GetNode<Button>("Modal/Layout/Body/Right/Form/Ownership/Personal");
		_guildOwnerButton ??= GetNode<Button>("Modal/Layout/Body/Right/Form/Ownership/Guild");
		_guildDropdown ??= GetNode<OptionButton>("Modal/Layout/Body/Right/Form/GuildDropdown");
		_nameInput ??= GetNode<LineEdit>("Modal/Layout/Body/Right/Form/Name");
		_descriptionInput ??= GetNode<TextEdit>("Modal/Layout/Body/Right/Form/Description");
		_uploadButton ??= GetNode<Button>("Modal/Layout/Footer/Row/Upload");
		_cancelButton ??= GetNode<Button>("Modal/Layout/Footer/Row/Cancel");
		_fileNameLabel ??= GetNode<Label>("Modal/Layout/Body/Left/FileCard/Row/FileName");
		_fileTypeLabel ??= GetNode<Label>("Modal/Layout/Body/Left/Stats/FileType/Stack/Value");
		_surfaceCountLabel ??= GetNode<Label>("Modal/Layout/Body/Left/Stats/Surfaces/Stack/Value");
		_vertexCountLabel ??= GetNode<Label>("Modal/Layout/Body/Left/Stats/Vertices/Stack/Value");
		_triangleCountLabel ??= GetNode<Label>("Modal/Layout/Body/Left/Stats/Triangles/Stack/Value");
		_emptyPreviewLabel ??= GetNode<Label>("Modal/Layout/Body/Left/Preview/EmptyPreview");
		_errorLabel ??= GetNode<Label>("Modal/Layout/Body/Right/Form/Error");
		_busyLabel ??= GetNode<Label>("Modal/Layout/Footer/Row/Busy");
		_previewViewport ??= GetNode<SubViewport>("Modal/Layout/Body/Left/Preview/SubViewportContainer/SubViewport");
		_previewRoot ??= GetNode<Node3D>("Modal/Layout/Body/Left/Preview/SubViewportContainer/SubViewport/PreviewRoot");
		_previewCamera ??= GetNode<Camera3D>("Modal/Layout/Body/Left/Preview/SubViewportContainer/SubViewport/Camera3D");
		_fileDialog ??= GetNode<FileDialog>("FileDialog");
	}
}
