// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.UI;
using BrickVerse.Creator.Utils;
using BrickVerse.Creator.Settings;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Formats;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace BrickVerse.Creator.Managers;

public static partial class PublishManager
{
	public static async Task PublishModel(
		Instance target,
		long modelID = 0,
		string? publishName = null,
		string? publishDescription = null
	)
	{
		var loadOverlay = CreatorService.Interface.LoadOverlay;
		try
		{
			loadOverlay?.SetTitle("Publishing model...");
			byte[]? preview = null;
			if (await ConfirmIncludePreview())
			{
				try
				{
					// A thumbnail is an enhancement, never a requirement for publishing.
					preview = await CaptureModelPreview(target);
				}
				catch (Exception previewException)
				{
					BV.PrintWarn("Model preview was skipped: ", previewException.Message);
					if (!await ConfirmPublishWithoutPreview(previewException.Message)) return;
					CreatorService.Interface.StatusBar?.SetStatus("Preview skipped; publishing model without a thumbnail");
				}
			}

			loadOverlay?.Show();
			loadOverlay?.SetStatus("Packing model...");
			byte[] packed = await PackedFormat.PackModel(target, loadOverlay.CreateProgressReporter("Publishing model"));

			loadOverlay?.SetStatus("Uploading now...");

			CreatorPublishResponse publishRes = await CreatorAPI.UploadAsset(
				packed,
				modelID,
				"PREFAB",
				"model.bvxm",
				string.IsNullOrWhiteSpace(publishName) ? target.Name : publishName.Trim(),
				publishDescription?.Trim() ?? "",
				previewData: preview
			);

			if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
				OS.ShellOpen(publishRes.Link);
			CreatorService.Interface.StatusBar?.SetStatus("Model published");
		}
		catch (Exception ex)
		{
			BV.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message);
		}
		finally
		{
			loadOverlay?.Hide();
		}
	}

	private static async Task<bool> ConfirmIncludePreview()
	{
		ConfirmationDialog dialog = new()
		{
			Title = "Prefab preview",
			DialogText = "Create a thumbnail for this prefab? You can position the preview camera before publishing.",
			OkButtonText = "Create preview",
			CancelButtonText = "Skip preview",
		};
		TaskCompletionSource<bool> completion = new();
		dialog.Confirmed += () => completion.TrySetResult(true);
		dialog.Canceled += () => completion.TrySetResult(false);
		dialog.CloseRequested += () => completion.TrySetResult(false);
		CreatorService.Interface.AddChild(dialog);
		dialog.PopupCentered(new Vector2I(470, 180));
		bool result = await completion.Task;
		dialog.QueueFree();
		return result;
	}

	private static async Task<bool> ConfirmPublishWithoutPreview(string reason)
	{
		ConfirmationDialog dialog = new()
		{
			Title = "Preview unavailable",
			DialogText = $"The preview could not be created:\n\n{reason}\n\nPublish this prefab without a preview?",
			OkButtonText = "Publish without preview",
			CancelButtonText = "Cancel publish",
		};
		TaskCompletionSource<bool> completion = new();
		dialog.Confirmed += () => completion.TrySetResult(true);
		dialog.Canceled += () => completion.TrySetResult(false);
		dialog.CloseRequested += () => completion.TrySetResult(false);
		CreatorService.Interface.AddChild(dialog);
		dialog.PopupCentered(new Vector2I(520, 220));
		bool result = await completion.Task;
		dialog.QueueFree();
		return result;
	}

	private static async Task<byte[]?> CaptureModelPreview(Instance target)
	{
		if (target is not Dynamic dynamic || !GodotObject.IsInstanceValid(dynamic.GDNode3D))
			throw new InvalidOperationException("This model is not loaded into the 3D world, so it cannot be framed for a preview yet.");

		SubViewport viewport = new()
		{
			Name = "PrefabPublishPreview",
			// Toolbox and marketplace prefab cards use square artwork. Render at a
			// high enough resolution to remain crisp in expanded/card-detail views.
			Size = new Vector2I(1024, 1024),
			OwnWorld3D = true,
			TransparentBg = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		try
		{
			Godot.Environment environment = new()
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				// Preserve alpha in the generated PNG instead of baking the old
				// blue/black preview backdrop into every uploaded prefab thumbnail.
				BackgroundColor = new Color(0f, 0f, 0f, 0f),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color("dbe9f5"),
				AmbientLightEnergy = 0.7f,
			};
			viewport.AddChild(new WorldEnvironment { Environment = environment });

			Node3D previewRoot = new() { Name = "Model" };
			viewport.AddChild(previewRoot);

			Transform3D rootInverse = dynamic.GDNode3D.GlobalTransform.AffineInverse();
			bool hasBounds = false;
			Vector3 boundsMin = Vector3.Zero;
			Vector3 boundsMax = Vector3.Zero;
			ClonePreviewMeshes(dynamic.GDNode3D, previewRoot, rootInverse, ref hasBounds, ref boundsMin, ref boundsMax);
			foreach (Part part in new[] { target }.Concat(target.GetDescendants()).OfType<Part>())
			{
				// Most anchored Parts are rendered through DatamodelBridge MultiMeshes,
				// so there is no MeshInstance3D below their datamodel node to clone.
				if (!part.IsMeshSeparated && !part.IsHidden)
					CloneBatchedPart(part, previewRoot, rootInverse, ref hasBounds, ref boundsMin, ref boundsMax);
			}
			if (!hasBounds)
			{
				foreach (Dynamic child in target.GetDescendants().OfType<Dynamic>())
				{
					if (GodotObject.IsInstanceValid(child.GDNode3D))
						ClonePreviewMeshes(child.GDNode3D, previewRoot, rootInverse, ref hasBounds, ref boundsMin, ref boundsMax);
				}
			}
			if (!hasBounds)
				throw new InvalidOperationException("The model has no loaded, renderable geometry for a preview. The prefab was not uploaded yet.");

			Vector3 center = (boundsMin + boundsMax) * 0.5f;
			Vector3 size = boundsMax - boundsMin;
			float extent = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
			float distance = Mathf.Max(2.5f, extent * 1.65f);

			Camera3D camera = new()
			{
				Current = true,
				Fov = 38f,
				Position = center + new Vector3(distance * 0.75f, distance * 0.5f, distance),
			};
			viewport.AddChild(camera);
			camera.LookAt(center, Vector3.Up);

			DirectionalLight3D keyLight = new()
			{
				LightColor = new Color("fff4df"),
				LightEnergy = 1.15f,
				RotationDegrees = new Vector3(-45f, -35f, 0f),
				ShadowEnabled = true,
			};
			viewport.AddChild(keyLight);

			OmniLight3D fillLight = new()
			{
				Position = center + new Vector3(-distance, distance * 0.65f, distance * 0.4f),
				OmniRange = distance * 3f,
				LightEnergy = 0.8f,
			};
			viewport.AddChild(fillLight);

			bool capturePreview = await ConfirmPreviewCamera(viewport, camera, center, distance);
			if (!capturePreview) return null;

			// The preview container temporarily sizes the viewport for the on-screen
			// camera editor. Restore the full export resolution after it is detached.
			viewport.Size = new Vector2I(1024, 1024);

			await CreatorService.Interface.ToSignal(
				CreatorService.Interface.GetTree(),
				SceneTree.SignalName.ProcessFrame
			);
			await CreatorService.Interface.ToSignal(
				CreatorService.Interface.GetTree(),
				SceneTree.SignalName.ProcessFrame
			);

			Image image = viewport.GetTexture().GetImage();
			if (image.IsEmpty())
				throw new InvalidOperationException("The model preview could not be rendered.");
			return image.SavePngToBuffer();
		}
		finally
		{
			viewport.QueueFree();
		}
	}

	private static async Task<bool> ConfirmPreviewCamera(
		SubViewport viewport,
		Camera3D camera,
		Vector3 center,
		float distance
	)
	{
		ModelPreviewCameraPopup popup = new(viewport, camera, center, distance);
		CreatorService.Interface.AddChild(popup);
		return await popup.ShowAndWait();
	}

	private sealed partial class ModelPreviewCameraPopup : Window
	{
		private readonly SubViewport _viewport;
		private readonly Camera3D _camera;
		private readonly Vector3 _initialTarget;
		private Vector3 _target;
		private readonly float _defaultDistance;
		private readonly TaskCompletionSource<bool> _completion = new();
		private float _yaw = -0.65f;
		private float _pitch = -0.28f;
		private float _distance;
		private bool _dragging;
		private bool _panning;

		public ModelPreviewCameraPopup(SubViewport viewport, Camera3D camera, Vector3 target, float distance)
		{
			_viewport = viewport;
			_camera = camera;
			_initialTarget = target;
			_target = target;
			_defaultDistance = distance;
			_distance = distance;
			Title = "Frame Model Preview";
			Size = new Vector2I(820, 900);
			MinSize = new Vector2I(760, 840);
			Exclusive = true;
			Transient = true;
			CloseRequested += () => Finish(false);
			BuildUI();
		}

		public async Task<bool> ShowAndWait()
		{
			PopupCentered();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			UpdateCamera();
			return await _completion.Task;
		}

		private void BuildUI()
		{
			MarginContainer margin = new();
			margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			margin.AddThemeConstantOverride("margin_left", 18);
			margin.AddThemeConstantOverride("margin_top", 16);
			margin.AddThemeConstantOverride("margin_right", 18);
			margin.AddThemeConstantOverride("margin_bottom", 16);
			AddChild(margin);
			VBoxContainer root = new();
			root.AddThemeConstantOverride("separation", 10);
			margin.AddChild(root);

			Label heading = new()
			{
				Text = "Choose the thumbnail view for this model",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			heading.AddThemeFontSizeOverride("font_size", 20);
			root.AddChild(heading);
			root.AddChild(new Label { Text = "Left/right drag: look  •  Middle drag or WASD/QE: move  •  Wheel: zoom  •  Shift: faster" });

			SubViewportContainer preview = new()
			{
				CustomMinimumSize = new Vector2(720, 720),
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
				Stretch = true,
			};
			preview.AddChild(_viewport);
			preview.GuiInput += OnPreviewInput;
			preview.FocusMode = Control.FocusModeEnum.All;
			preview.MouseEntered += preview.GrabFocus;
			root.AddChild(preview);

			HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.End };
			actions.AddThemeConstantOverride("separation", 8);
			Button reset = new() { Text = "Reset view" };
			reset.Pressed += ResetCamera;
			actions.AddChild(reset);
			Button skip = new() { Text = "Publish without preview" };
			skip.Pressed += () => Finish(false);
			actions.AddChild(skip);
			Button confirm = new() { Text = "Use this preview" };
			confirm.Pressed += () => Finish(true);
			actions.AddChild(confirm);
			root.AddChild(actions);
		}

		private void OnPreviewInput(InputEvent @event)
		{
			if (@event is InputEventMouseButton button)
			{
				if (button.ButtonIndex is MouseButton.Left or MouseButton.Right) _dragging = button.Pressed;
				if (button.ButtonIndex == MouseButton.Middle) _panning = button.Pressed;
				if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp) _distance = Mathf.Max(0.5f, _distance * 0.88f);
				if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown) _distance = Mathf.Min(100f, _distance * 1.14f);
				UpdateCamera();
			}
			else if (_dragging && @event is InputEventMouseMotion motion)
			{
				_yaw -= motion.Relative.X * 0.01f;
				_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.01f, -1.35f, 1.35f);
				UpdateCamera();
			}
			else if (_panning && @event is InputEventMouseMotion pan)
			{
				float speed = _distance * 0.0018f;
				_target += _camera.GlobalTransform.Basis.X * -pan.Relative.X * speed;
				_target += _camera.GlobalTransform.Basis.Y * pan.Relative.Y * speed;
				UpdateCamera();
			}
		}

		public override void _Process(double delta)
		{
			Vector3 input = Vector3.Zero;
			if (Input.IsKeyPressed(Key.W)) input.Z -= 1;
			if (Input.IsKeyPressed(Key.S)) input.Z += 1;
			if (Input.IsKeyPressed(Key.A)) input.X -= 1;
			if (Input.IsKeyPressed(Key.D)) input.X += 1;
			if (Input.IsKeyPressed(Key.Q)) input.Y -= 1;
			if (Input.IsKeyPressed(Key.E)) input.Y += 1;
			if (input == Vector3.Zero) return;
			float speed = Mathf.Max(0.5f, _distance * 0.8f)
				* (Input.IsKeyPressed(Key.Shift) ? 3f : 1f) * (float)delta;
			Vector3 flatForward = -_camera.GlobalTransform.Basis.Z;
			flatForward.Y = 0;
			flatForward = flatForward.Normalized();
			Vector3 flatRight = _camera.GlobalTransform.Basis.X;
			flatRight.Y = 0;
			flatRight = flatRight.Normalized();
			_target += (flatRight * input.X + Vector3.Up * input.Y + flatForward * -input.Z) * speed;
			UpdateCamera();
		}

		private void ResetCamera()
		{
			_target = _initialTarget;
			_yaw = -0.65f;
			_pitch = -0.28f;
			_distance = _defaultDistance;
			UpdateCamera();
		}

		private void UpdateCamera()
		{
			Vector3 direction = new(
				Mathf.Cos(_pitch) * Mathf.Sin(_yaw),
				Mathf.Sin(_pitch),
				Mathf.Cos(_pitch) * Mathf.Cos(_yaw)
			);
			_camera.Position = _target + direction * _distance;
			_camera.LookAt(_target, Vector3.Up);
		}

		private void Finish(bool capture)
		{
			if (!_completion.TrySetResult(capture)) return;
			// The capture continues after the dialog closes, so keep its viewport
			// alive rather than freeing it with the popup tree.
			_viewport.Reparent(CreatorService.Interface);
			QueueFree();
		}
	}

	private static void CloneBatchedPart(
		Part part,
		Node3D previewRoot,
		Transform3D rootInverse,
		ref bool hasBounds,
		ref Vector3 boundsMin,
		ref Vector3 boundsMax
	)
	{
		(Godot.Mesh mesh, _) = Globals.LoadShape(part.Shape.ToString());
		Transform3D transform = rootInverse * part.GetGlobalTransform();
		MeshInstance3D clone = new()
		{
			Mesh = mesh,
			Transform = transform,
			MaterialOverride = Globals.LoadMaterial(part.Material, part.Color.A),
			CastShadow = part.CastShadows
				? GeometryInstance3D.ShadowCastingSetting.On
				: GeometryInstance3D.ShadowCastingSetting.Off,
		};
		clone.SetInstanceShaderParameter("color", part.Color);
		previewRoot.AddChild(clone);
		ExpandBounds(mesh.GetAabb(), transform, ref hasBounds, ref boundsMin, ref boundsMax);
	}

	private static void ClonePreviewMeshes(
		Node node,
		Node3D previewRoot,
		Transform3D rootInverse,
		ref bool hasBounds,
		ref Vector3 boundsMin,
		ref Vector3 boundsMax
	)
	{
		if (node is MeshInstance3D source && source.Mesh != null && source.Visible)
		{
			Transform3D relativeTransform = rootInverse * source.GlobalTransform;
			MeshInstance3D clone = new()
			{
				Mesh = source.Mesh,
				Transform = relativeTransform,
				CastShadow = source.CastShadow,
			};
			for (int surface = 0; surface < source.GetSurfaceOverrideMaterialCount(); surface++)
				clone.SetSurfaceOverrideMaterial(surface, source.GetSurfaceOverrideMaterial(surface));
			previewRoot.AddChild(clone);
			ExpandBounds(source.GetAabb(), relativeTransform, ref hasBounds, ref boundsMin, ref boundsMax);
		}

		// BrickVerse renderers intentionally keep generated Part meshes, imported
		// mesh containers, and other implementation nodes internal. Godot excludes
		// those from GetChildren() unless includeInternal is explicitly enabled.
		foreach (Node child in node.GetChildren(true))
			ClonePreviewMeshes(child, previewRoot, rootInverse, ref hasBounds, ref boundsMin, ref boundsMax);
	}

	private static void ExpandBounds(
		Aabb bounds,
		Transform3D transform,
		ref bool hasBounds,
		ref Vector3 boundsMin,
		ref Vector3 boundsMax
	)
	{
		for (int x = 0; x <= 1; x++)
			for (int y = 0; y <= 1; y++)
				for (int z = 0; z <= 1; z++)
				{
					Vector3 point = transform * (bounds.Position + new Vector3(
						bounds.Size.X * x,
						bounds.Size.Y * y,
						bounds.Size.Z * z
					));
					if (!hasBounds)
					{
						boundsMin = point;
						boundsMax = point;
						hasBounds = true;
					}
					else
					{
						boundsMin = new Vector3(
							Mathf.Min(boundsMin.X, point.X),
							Mathf.Min(boundsMin.Y, point.Y),
							Mathf.Min(boundsMin.Z, point.Z)
						);
						boundsMax = new Vector3(
							Mathf.Max(boundsMax.X, point.X),
							Mathf.Max(boundsMax.Y, point.Y),
							Mathf.Max(boundsMax.Z, point.Z)
						);
					}
				}
	}

	public static async Task PublishAddon(
		ServerScript target,
		long addonID = 0,
		string? publishName = null,
		string? publishDescription = null
	)
	{
		var loadOverlay = CreatorService.Interface.LoadOverlay;
		try
		{
			loadOverlay?.SetTitle("Publishing addon...");
			loadOverlay?.SetStatus("Packing addon...");
			loadOverlay?.Show();

			ModuleScript? metaModule = target.FindChild<ModuleScript>("AddonMetadata");
			if (metaModule == null)
			{
				metaModule = new ModuleScript
				{
					Name = "AddonMetadata",
					Source = @"{
	""Name"": """ + target.Name + @""",
	""Version"": ""1.0.0"",
	""Description"": ""A new addon"",
	""Author"": ""Your Name""
}"
				};
				metaModule.Parent = target;
			}

			AddonsManager.AddonMetadata metadata = AddonsManager.AddonMetadata.FromJson(metaModule.Source);
			if (string.IsNullOrWhiteSpace(metadata.Name) || string.IsNullOrWhiteSpace(metadata.Version))
				throw new InvalidOperationException("Addon metadata must include a name and version.");

			byte[] packed = await PackedFormat.PackAddon(
				target,
				metadata,
				loadOverlay.CreateProgressReporter("Publishing addon")
			);

			loadOverlay?.SetStatus("Uploading now...");
			CreatorPublishResponse publishRes = await CreatorAPI.UploadAsset(
				packed,
				addonID,
				"PLUGIN",
				"addon.bvaddon",
				string.IsNullOrWhiteSpace(publishName) ? metadata.Name : publishName.Trim(),
				publishDescription?.Trim() ?? metadata.Description
			);

			if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
				OS.ShellOpen(publishRes.Link);
			CreatorService.Interface.StatusBar?.SetStatus("Addon published");
		}
		catch (Exception ex)
		{
			BV.PrintErr(ex);
			CreatorService.Interface.PopupAlert(ex.Message);
		}
		finally
		{
			loadOverlay?.Hide();
		}
	}


}
