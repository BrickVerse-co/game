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

namespace BrickVerse.Creator.Managers;

public static partial class PublishManager
{
	public static async Task PublishModel(Instance target, long modelID = 0)
	{
		var loadOverlay = CreatorService.Interface.LoadOverlay;
		try
		{
			loadOverlay?.SetTitle("Publishing model...");
			byte[]? preview = null;
			try
			{
				// A thumbnail is an enhancement, never a requirement for publishing.
				preview = await CaptureModelPreview(target);
			}
			catch (Exception previewException)
			{
				BV.PrintWarn("Model preview was skipped: ", previewException.Message);
				CreatorService.Interface.StatusBar?.SetStatus("Preview skipped; publishing model without a thumbnail");
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
				target.Name,
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

	private static async Task<byte[]?> CaptureModelPreview(Instance target)
	{
		if (target is not Dynamic dynamic || !GodotObject.IsInstanceValid(dynamic.GDNode3D))
			return null;

		SubViewport viewport = new()
		{
			Name = "PrefabPublishPreview",
			Size = new Vector2I(768, 432),
			OwnWorld3D = true,
			TransparentBg = false,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		try
		{
			Godot.Environment environment = new()
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color("182431"),
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
			if (!hasBounds)
				return null;

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
		private readonly Vector3 _target;
		private readonly float _defaultDistance;
		private readonly TaskCompletionSource<bool> _completion = new();
		private float _yaw = -0.65f;
		private float _pitch = -0.28f;
		private float _distance;
		private bool _dragging;

		public ModelPreviewCameraPopup(SubViewport viewport, Camera3D camera, Vector3 target, float distance)
		{
			_viewport = viewport;
			_camera = camera;
			_target = target;
			_defaultDistance = distance;
			_distance = distance;
			Title = "Frame Model Preview";
			Size = new Vector2I(900, 620);
			MinSize = new Vector2I(640, 480);
			Exclusive = true;
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
			VBoxContainer root = new();
			root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			root.AddThemeConstantOverride("separation", 10);
			AddChild(root);

			Label heading = new()
			{
				Text = "Choose the thumbnail view for this model",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			heading.AddThemeFontSizeOverride("font_size", 20);
			root.AddChild(heading);
			root.AddChild(new Label { Text = "Drag to orbit • Mouse wheel to zoom • This does not modify the model." });

			SubViewportContainer preview = new()
			{
				CustomMinimumSize = new Vector2(640, 420),
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
				Stretch = true,
			};
			preview.AddChild(_viewport);
			preview.GuiInput += OnPreviewInput;
			root.AddChild(preview);

			HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.End };
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
				if (button.ButtonIndex == MouseButton.Left) _dragging = button.Pressed;
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
		}

		private void ResetCamera()
		{
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

		foreach (Node child in node.GetChildren())
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

	public static async Task PublishAddon(ServerScript target, long addonID = 0)
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
				metadata.Name,
				metadata.Description
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
