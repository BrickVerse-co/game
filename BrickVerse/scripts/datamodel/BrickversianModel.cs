// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Networking;
using BrickVerse.Schemas.API;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using BrickVerse.Shared.Misc;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class BrickversianModel : CharacterModel
{
	private const double NetLookBlendUpdateInterval = 0.1;
	private double _lastNetUpdateTime = 0.0;

	private static readonly BoxShape3D _collisionBox = new() { Size = new(2f, 5.8f, 1f) };
	internal Node3D? CollisionPivot;
	internal CollisionShape3D? CollisionShape;
	private Physical? _oldPhyParent;

	internal MeshInstance3D HeadMeshInstance = null!;
	internal MeshInstance3D TorsoMeshInstance = null!;
	internal MeshInstance3D LeftArmMeshInstance = null!;
	internal MeshInstance3D RightArmMeshInstance = null!;
	internal MeshInstance3D LeftHandMeshInstance = null!;
	internal MeshInstance3D RightHandMeshInstance = null!;
	internal MeshInstance3D LeftLegMeshInstance = null!;
	internal MeshInstance3D RightLegMeshInstance = null!;
	internal Node3D Pivot = null!;

	private const float BlendSpeed = 5f;
	private const float LookBlendSpeed = 15f;
	private static readonly Color _defaultBodyColor = Colors.White;

	private const int ClothingWidth = 1024;
	private const int ClothingHeight = 1024;
	private const Image.Format ClothingFormat = Image.Format.Rgba8;
	private static readonly Rect2I _clothingRect = new(0, 0, ClothingWidth, ClothingHeight);

	private int _loadAppearanceCount = 0;

	internal Skeleton3D Skeleton = null!;
	internal AnimationTree AnimTree = null!;

	private static readonly Shader _limbShader = GD.Load<Shader>("res://resources/shaders/character/limb.gdshader");
	private static readonly Shader _transparentLimbShader = GD.Load<Shader>("res://resources/shaders/character/limb_transparent.gdshader");
	private static readonly Texture2D _defaultFace = GD.Load<Texture2D>("res://assets/textures/client/character/DefaultFace.png");
	private static readonly StringName _albedoParam = "albedo";
	private static readonly StringName _albedoTexParam = "albedo_texture";
	private static bool _loggedMissingRagdollNode = false;

	private ImageAsset? _faceImage;
	private MeshAsset? _bodyMesh;

	private readonly ShaderMaterial _headMat = new() { Shader = _limbShader };
	private Godot.Decal? _faceDecal;
	private BoneAttachment3D? _faceDecalAttachment;

	private static readonly Vector3 _faceDecalSize = new(1.65f, 1.65f, 0.15f);
	private static readonly Vector3 _faceDecalOffset = new(0f, 0f, -0.51f);

	private readonly ShaderMaterial _torsoMat = new() { Shader = _limbShader };
	private readonly ShaderMaterial _leftArmMat = new() { Shader = _limbShader };
	private readonly ShaderMaterial _rightArmMat = new() { Shader = _limbShader };
	private readonly ShaderMaterial _leftHandMat = new() { Shader = _limbShader };
	private readonly ShaderMaterial _rightHandMat = new() { Shader = _limbShader };
	private readonly ShaderMaterial _leftLegMat = new() { Shader = _limbShader };
	private readonly ShaderMaterial _rightLegMat = new() { Shader = _limbShader };

	private readonly ShaderMaterial _transparentTorsoMat = new() { Shader = _transparentLimbShader };
	private readonly ShaderMaterial _transparentLeftArmMat = new() { Shader = _transparentLimbShader };
	private readonly ShaderMaterial _transparentRightArmMat = new() { Shader = _transparentLimbShader };
	private readonly ShaderMaterial _transparentLeftHandMat = new() { Shader = _transparentLimbShader };
	private readonly ShaderMaterial _transparentRightHandMat = new() { Shader = _transparentLimbShader };
	private readonly ShaderMaterial _transparentLeftLegMat = new() { Shader = _transparentLimbShader };
	private readonly ShaderMaterial _transparentRightLegMat = new() { Shader = _transparentLimbShader };

	private PhysicalBoneSimulator3D? _ragdollBoneSim;
	private PhysicalBoneSimulator3D? _lastPhysicalBoneSim = null!;
	private readonly Dictionary<string, float> _blendTargets = [];
	private int _toBeLoadedCount = 0;
	private bool _faceLoaded = false;
	private float _lastLookBlendX = 0;
	private float _lastLookBlendY = 0;
	private bool _faceOverrided = false;
	private bool _bodyOverrided = false;
	private CharacterAnimHelper _helper = null!;
	private readonly Dictionary<CharacterAttachmentEnum, Dynamic> _attachmentEnumToDyn = [];
	private PackedScene? _bodyPkScene;
	private bool _updateClothDirty = false;

	public PhysicalBone3D? VelocityPhysicalBone;

	[Editable, ScriptProperty, Export, SyncVar]
	public Color HeadColor
	{
		get => MeshGetAlbedo(HeadMeshInstance);
		set
		{
			_headMat.Shader = (value.A == 1) ? _limbShader : _transparentLimbShader;
			HeadMeshInstance.SetInstanceShaderParameter(_albedoParam, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color TorsoColor
	{
		get => MeshGetAlbedo(TorsoMeshInstance);
		set
		{
			MeshSetAlbedo(TorsoMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color LeftArmColor
	{
		get => MeshGetAlbedo(LeftArmMeshInstance);
		set
		{
			MeshSetAlbedo(LeftArmMeshInstance, value);
			MeshSetAlbedo(LeftHandMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color RightArmColor
	{
		get => MeshGetAlbedo(RightArmMeshInstance);
		set
		{
			MeshSetAlbedo(RightArmMeshInstance, value);
			MeshSetAlbedo(RightHandMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color LeftLegColor
	{
		get => MeshGetAlbedo(LeftLegMeshInstance);
		set
		{
			MeshSetAlbedo(LeftLegMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color RightLegColor
	{
		get => MeshGetAlbedo(RightLegMeshInstance);
		set
		{
			MeshSetAlbedo(RightLegMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use FaceImage instead"), CloneIgnore]
	public string FaceID
	{
		get => (_faceImage is BVImageAsset polyImg) ? polyImg.ImageID : "0";
		set
		{
			if (value == "0") { FaceImage = null; return; }
			BVImageAsset imgAsset = new();
			FaceImage = imgAsset;
			imgAsset.ImageID = value.ToString();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public ImageAsset? FaceImage
	{
		get => _faceImage;
		set
		{
			if (_faceImage == value)
				return;

			if (_faceImage != null)
			{
				_faceImage.ResourceLoaded -= OnFaceLoaded;
				_faceImage.UnlinkFrom(this);
			}

			_faceImage = value;
			SetFaceTexture(null);

			if (_faceImage != null)
			{
				_faceOverrided = true;
				_faceLoaded = false;

				AddLoadCount();

				_faceImage.LinkTo(this);
				_faceImage.ResourceLoaded += OnFaceLoaded;

				if (_faceImage.IsResourceLoaded && _faceImage.Resource != null)
				{
					OnFaceLoaded(_faceImage.Resource);
				}
				else
				{
					_faceImage.QueueLoadResource();
				}
			}
			else
			{
				_faceOverrided = false;
				_faceLoaded = true;
				SetFaceTexture(_defaultFace);
			}

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public MeshAsset? BodyMesh
	{
		get => _bodyMesh;
		set
		{
			if (_bodyMesh != null && _bodyMesh != value)
			{
				_bodyMesh.ResourceLoaded -= OnBodyLoaded;
				_bodyMesh.UnlinkFrom(this);
			}
			OnBodyLoaded(null);
			_bodyMesh = value;
			if (_bodyMesh != null)
			{
				AddLoadCount();
				_bodyOverrided = true;
				_bodyMesh.LinkTo(this);
				_bodyMesh.ResourceLoaded += OnBodyLoaded;
				if (_bodyMesh.IsResourceLoaded && _bodyMesh.Resource != null)
				{
					OnBodyLoaded(_bodyMesh.Resource);
				}
				else
				{
					_bodyMesh.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[ScriptProperty] public bool Ragdolling { get; private set; } = false;
	[ScriptProperty] public Vector3 RagdollPosition => VelocityPhysicalBone == null ? Vector3.Zero : VelocityPhysicalBone.GlobalPosition;
	[ScriptProperty] public Vector3 RagdollRotation => VelocityPhysicalBone == null ? Vector3.Zero : VelocityPhysicalBone.GlobalRotationDegrees.FlipEuler();

	// These two's not reliable yet, as it doesn't wait for mesh to load. TODO: Come back and fix
	public bool IsAvatarLoaded { get; private set; } = false;
	public event Action? AvatarLoaded;

	[ScriptProperty] public BVSignal RagdollStarted { get; private set; } = new();
	[ScriptProperty] public BVSignal RagdollStopped { get; private set; } = new();

	public override void Init()
	{
		FaceImage = null;

		_helper = new() { Name = "CharacterHelper", Target = this };
		Globals.Singleton.AddChild(_helper, true);

		Skeleton = GetRequiredNodeCompat<Skeleton3D>(
			"Character/Poly/Skeleton3D"
		);
		Skeleton.ShowRestOnly = false;
		_ragdollBoneSim = GetNodeCompat<PhysicalBoneSimulator3D>(
			"Character/Poly/RagdollBone"
		);
		HeadMeshInstance = GetRequiredNodeCompat<MeshInstance3D>(
			"Character/Poly/Skeleton3D/Head"
		);
		TorsoMeshInstance = GetRequiredNodeCompat<MeshInstance3D>(
			"Character/Poly/Skeleton3D/Torso"
		);
		LeftArmMeshInstance = GetRequiredNodeCompat<MeshInstance3D>(
			"Character/Poly/Skeleton3D/LeftArm"
		);
		RightArmMeshInstance = GetRequiredNodeCompat<MeshInstance3D>(
			"Character/Poly/Skeleton3D/RightArm"
		);
		LeftHandMeshInstance = GetRequiredNodeCompat<MeshInstance3D>(
			"Character/Poly/Skeleton3D/LeftHand"
		);
		RightHandMeshInstance = GetRequiredNodeCompat<MeshInstance3D>(
			"Character/Poly/Skeleton3D/RightHand"
		);
		LeftLegMeshInstance = GetRequiredNodeCompat<MeshInstance3D>(
			"Character/Poly/Skeleton3D/LeftLeg"
		);
		RightLegMeshInstance = GetRequiredNodeCompat<MeshInstance3D>(
			"Character/Poly/Skeleton3D/RightLeg"
		);
		Pivot = GetRequiredNodeCompat<Node3D>("Character/Poly");

		if (_ragdollBoneSim == null)
		{
			if (!_loggedMissingRagdollNode)
			{
				_loggedMissingRagdollNode = true;
				BV.PrintErr("Ragdoll simulator node not found. Ragdoll features will be unavailable for this model scene.");
			}
		}

		Pivot.Scale = NodeSize;

		HeadMeshInstance.MaterialOverride = _headMat;
		TorsoMeshInstance.MaterialOverride = _torsoMat;
		LeftArmMeshInstance.MaterialOverride = _leftArmMat;
		RightArmMeshInstance.MaterialOverride = _rightArmMat;
		LeftHandMeshInstance.MaterialOverride = _leftHandMat;
		RightHandMeshInstance.MaterialOverride = _rightHandMat;
		LeftLegMeshInstance.MaterialOverride = _leftLegMat;
		RightLegMeshInstance.MaterialOverride = _rightLegMat;

		AnimTree = GDNode.GetNode<AnimationTree>("AnimationTree");
		AnimTree.Active = true;

		CreateFaceDecal();
		EnsureClothing();

		base.Init();
		SetProcess(true);
	}

	private void EnsureClothing()
	{
		// Ensure we have a shirt and pants clothing, if not create default ones.
		Clothing[] clothings = GetChildrenOfClass<Clothing>();

		bool hasShirt = false;
		bool hasPants = false;

		foreach (Clothing clothing in clothings)
		{
			if (clothing.Name.ToLower().Contains("shirt"))
			{
				hasShirt = true;
			}
			else if (clothing.Name.ToLower().Contains("pants"))
			{
				hasPants = true;
			}
		}

		// Create default clothing if missing
		if (!hasShirt)
		{
			Clothing defaultShirt = New<Clothing>();
			defaultShirt.Name = "DefaultShirt";
			defaultShirt.Type = Clothing.ClothingType.Shirt;
			BVImageAsset asset = New<BVImageAsset>();
			asset.ImageID = "338444747976736768";
			defaultShirt.Image = asset;
			defaultShirt.Parent = this;
		}

		if (!hasPants)
		{
			Clothing defaultPants = New<Clothing>();
			defaultPants.Name = "DefaultPants";
			defaultPants.Type = Clothing.ClothingType.Pants;
			BVImageAsset asset = New<BVImageAsset>();
			asset.ImageID = "338444747976736768";
			defaultPants.Image = asset;
			defaultPants.Parent = this;
		}

		if (!hasShirt || !hasPants)
		{
			// Update the cloth materials after adding default clothing
			QueueRenderCloth();
		}
	}

	private T? GetNodeCompat<T>(params string[] paths) where T : Node
	{
		foreach (string path in paths)
		{
			T? node = GDNode.GetNodeOrNull<T>(path);
			if (node != null)
			{
				return node;
			}
		}

		return null;
	}

	private T GetRequiredNodeCompat<T>(params string[] paths) where T : Node
	{
		T? node = GetNodeCompat<T>(paths);
		if (node != null)
		{
			return node;
		}

		throw new InvalidOperationException($"Missing required node {typeof(T).Name}. Tried paths: {string.Join(", ", paths)}");
	}

	public override void PreDelete()
	{
		// Free helper
		_helper?.QueueFree();

		// Free materials
		_headMat.Dispose();

		_torsoMat.Dispose();
		_leftArmMat.Dispose();
		_rightArmMat.Dispose();
		_leftHandMat.Dispose();
		_rightHandMat.Dispose();
		_leftLegMat.Dispose();
		_rightLegMat.Dispose();

		_transparentTorsoMat.Dispose();
		_transparentLeftArmMat.Dispose();
		_transparentRightArmMat.Dispose();
		_transparentLeftHandMat.Dispose();
		_transparentRightHandMat.Dispose();
		_transparentLeftLegMat.Dispose();
		_transparentRightLegMat.Dispose();

		// Free face
		if (Node.IsInstanceValid(_faceDecalAttachment))
		{
			_faceDecalAttachment!.QueueFree();
		}

		_faceDecal = null;
		_faceDecalAttachment = null;

		base.PreDelete();
	}

	public override Node CreateGDNode()
	{
		Node? scene = Globals.LoadNetworkedObjectScene(ClassName);
		scene ??= Globals.LoadNetworkedObjectScene("BrickversianModal");

		if (scene == null)
		{
			throw new InvalidOperationException(
				$"Unable to load character scene for {ClassName}. Tried scenes: {ClassName}.tscn, BrickversianModal.tscn"
			);
		}

		return scene;
	}

	public override void EnterTree()
	{
		if (Parent is Physical phy)
		{
			_oldPhyParent = phy;

			// Configure default collision shape for BrickversianModel
			CollisionPivot = new()
			{
				Scale = NodeSize
			};
			CollisionShape = new()
			{
				Shape = _collisionBox
			};
			Physical.SetRemoteLinkOffset(CollisionShape, new(0, 3f - 0.1f, 0));
			Physical.SetRemoteLinkTarget(CollisionShape, CollisionPivot);
			GDNode.AddChild(CollisionPivot);
			CollisionPivot.Position = new(0, -3f, 0);

			phy.GDNode.AddChild(CollisionShape);
			phy.AddCollisionShape(CollisionShape);
			phy.UpdateCollision();
		}
		base.EnterTree();
	}

	public override void ExitTree()
	{
		if (_oldPhyParent != null)
		{
			_oldPhyParent.RemoveCollisionShape(CollisionShape!);
			if (Node.IsInstanceValid(CollisionPivot))
			{
				CollisionPivot.QueueFree();
			}

			CollisionPivot = null;
			CollisionShape = null;
		}
		base.ExitTree();
	}

	public override async void Ready()
	{
		if (Root == null)
		{
			// Create default character on null root (eg. loading screens/mobile)
			Animator = New<Animator>();
			Animator.Name = "Animator";
			Animator.Parent = this;
		}

		Animator = await WaitChild<Animator>("Animator", 5);

		if (Animator == null) return;

		AnimTree.AdvanceExpressionBaseNode = _helper.GetPath();

		Animator.SetNetworkAuthority(NetworkAuthority);

		Animator.AnimationTree = AnimTree;
		Animator.AnimatorInit();
		Animator.ImportAnimationRaw("emote_dance", "Dance");
		Animator.ImportAnimationRaw("emote_helicopter", "Helicopter");
		Animator.ImportAnimationRaw("emote_sit", "Sit");
		Animator.ImportAnimationRaw("emote_dance2", "Dance2");

		Animator.ImportOneShotAnimationRaw("emote_wave", "Wave");
		Animator.ImportOneShotAnimationRaw("emote_point", "Point");
		Animator.ImportOneShotAnimationRaw("emote_disagree", "Disagree");
		Animator.ImportOneShotAnimationRaw("emote_agree", "Agree");
		Animator.ImportOneShotAnimationRaw("emote_scream", "Scream");
		Animator.ImportOneShotAnimationRaw("emote_disappointed", "Disappointed");

		/*
		Animator.ImportOneShotAnimationRaw("poly_welcome", "polytorian_2/welcome");
		Animator.ImportOneShotAnimationRaw("avataredit_pose1", "polytorian_2/pose1");
		Animator.ImportOneShotAnimationRaw("avataredit_pose2", "polytorian_2/pose2");
		Animator.ImportOneShotAnimationRaw("avataredit_pose3", "polytorian_2/pose3");
		*/

		Animator.ImportOneShotAnimationRaw("slash", "ToolSlash", true);
		Animator.ImportOneShotAnimationRaw("eat", "ToolEat", true);
		Animator.ImportOneShotAnimationRaw("drink", "ToolDrink", true);
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		Pivot?.Scale = newSize;
		CollisionPivot?.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	public override void Process(double delta)
	{
		base.Process(delta);

		if (_updateClothDirty)
		{
			_updateClothDirty = false;
			UpdateClothMaterials();
		}

		foreach (KeyValuePair<string, float> kvp in _blendTargets)
		{
			string propName = kvp.Key;
			float target = kvp.Value;
			float current = (float)AnimTree.Get(propName);

			float targetBlendSpeed = BlendSpeed;
			float newValue;

			if (propName.Contains("Look"))
			{
				targetBlendSpeed = LookBlendSpeed;

				newValue = Mathf.Lerp(current, target, MathUtils.ExpDecay((float)delta, targetBlendSpeed));
			}
			else
			{
				newValue = Mathf.MoveToward(current, target, (float)delta * targetBlendSpeed);
			}

			AnimTree.Set(propName, newValue);
		}
	}

	private void UpdateClothMaterials()
	{
		Clothing[] clothings = GetChildrenOfClass<Clothing>();

		// Explicit layer order:
		// Pants   = 1, bottom
		// Shirt   = 2
		// T-Shirt = 3, top
		ImageTexture? pantsTexture = BuildClothingComposite(
			clothings,
			Clothing.ClothingType.Pants
		);

		ImageTexture? shirtTexture = BuildClothingComposite(
			clothings,
			Clothing.ClothingType.Shirt
		);

		ImageTexture? torsoTexture = BuildClothingComposite(
			clothings,
			Clothing.ClothingType.Pants,
			Clothing.ClothingType.Shirt,
			Clothing.ClothingType.TShirt
		);

		// T-Shirt: front torso only.
		// Shirt: torso and arms, excluding hands.
		// Pants: torso and legs.
		SetClothingTexture(_torsoMat, _transparentTorsoMat, torsoTexture);
		SetClothingTexture(_leftArmMat, _transparentLeftArmMat, shirtTexture);
		SetClothingTexture(_rightArmMat, _transparentRightArmMat, shirtTexture);
		SetClothingTexture(_leftLegMat, _transparentLeftLegMat, pantsTexture);
		SetClothingTexture(_rightLegMat, _transparentRightLegMat, pantsTexture);

		// Hands never receive shirt textures.
		SetClothingTexture(_leftHandMat, _transparentLeftHandMat, null);
		SetClothingTexture(_rightHandMat, _transparentRightHandMat, null);
	}

	private static ImageTexture? BuildClothingComposite(
		Clothing[] clothings,
		params Clothing.ClothingType[] layers
	)
	{
		Image result = Image.CreateEmpty(
			ClothingWidth,
			ClothingHeight,
			false,
			ClothingFormat
		);

		bool hasTexture = false;

		foreach (Clothing.ClothingType layer in layers)
		{
			foreach (Clothing clothing in clothings)
			{
				if (clothing.Type != layer || clothing.ClothTexture == null)
					continue;

				Image image = clothing.ClothTexture.GetImage();
				image.Convert(ClothingFormat);
				image.Resize(ClothingWidth, ClothingHeight);

				result.BlendRect(image, _clothingRect, Vector2I.Zero);
				hasTexture = true;
			}
		}

		return hasTexture
			? ImageTexture.CreateFromImage(result)
			: null;
	}

	private static void SetClothingTexture(
		ShaderMaterial opaqueMaterial,
		ShaderMaterial transparentMaterial,
		Texture2D? texture
	)
	{
		opaqueMaterial.SetShaderParameter(_albedoTexParam, texture);
		transparentMaterial.SetShaderParameter(_albedoTexParam, texture);
	}

	private void CreateFaceDecal()
	{
		_faceDecalAttachment = new BoneAttachment3D
		{
			Name = "FaceDecalAttachment",
			BoneName = "Head",
		};

		Skeleton.AddChild(_faceDecalAttachment);

		_faceDecal = new Godot.Decal
		{
			Name = "FaceDecal",
			Size = new Vector3(1.65f, 1.65f, 0.15f),
			CullMask = HeadMeshInstance.Layers,
			UpperFade = 0f,
			LowerFade = 0f,
			DistanceFadeEnabled = false,
		};

		_faceDecalAttachment.AddChild(_faceDecal);

		// Godot decals project along their local -Y axis.
		// Rotate toward the front-facing -Z side of the head.
		_faceDecal.RotationDegrees = new Vector3(90f, 0f, 0f);
		_faceDecal.Position = new Vector3(0f, 0f, -0.51f);

		SetFaceTexture(
			_faceImage?.Resource as Texture2D ?? _defaultFace
		);
	}

	private void SetFaceTexture(Texture2D? texture)
	{
		if (_faceDecal == null)
			return;

		_faceDecal.TextureAlbedo = texture;
		_faceDecal.Visible = texture != null;
	}

	private void OnFaceLoaded(Resource resource)
	{
		if (resource is not Texture2D texture)
			return;

		SetFaceTexture(texture);

		if (!_faceLoaded)
		{
			_faceLoaded = true;
			AssetLoadCheckout();
		}
	}

	private void AddLoadCount()
	{
		IsAvatarLoaded = false;
		_toBeLoadedCount++;
	}

	private void AssetLoadCheckout()
	{
		_toBeLoadedCount--;
		if (_toBeLoadedCount < 0)
		{
			_toBeLoadedCount = 0;
		}
		if (!IsAvatarLoaded && _toBeLoadedCount == 0)
		{
			IsAvatarLoaded = true;
			AvatarLoaded?.Invoke();
		}
	}

	private void OnBodyLoaded(Resource? resource)
	{
		if (resource is PackedScene scene)
		{
			if (_bodyPkScene == scene) return;
			_bodyPkScene = scene;

			Node n = scene.Instantiate();

			ApplyBodyPart(n, HeadMeshInstance, "Head");
			ApplyBodyPart(n, LeftArmMeshInstance, "LeftArm");
			ApplyBodyPart(n, RightArmMeshInstance, "RightArm");
			ApplyBodyPart(n, LeftLegMeshInstance, "LeftLeg");
			ApplyBodyPart(n, RightLegMeshInstance, "RightLeg");
			ApplyBodyPart(n, TorsoMeshInstance, "Torso");

			n.QueueFree();
		}
		else if (resource == null)
		{
			_bodyPkScene = null;
			ApplyDefaultBodyPart(HeadMeshInstance, "Head");
			ApplyDefaultBodyPart(LeftArmMeshInstance, "LeftArm");
			ApplyDefaultBodyPart(RightArmMeshInstance, "RightArm");
			ApplyDefaultBodyPart(LeftLegMeshInstance, "LeftLeg");
			ApplyDefaultBodyPart(RightLegMeshInstance, "RightLeg");
			ApplyDefaultBodyPart(TorsoMeshInstance, "Torso");
		}
	}

	private static void ApplyDefaultBodyPart(MeshInstance3D m3d, string k)
	{
		m3d.Mesh = GD.Load<Godot.Mesh>($"res://assets/models/bodyparts/default/{k}.tres");
	}

	private static void ApplyBodyPart(Node source, MeshInstance3D target, string sourceName)
	{
		if (source.GetNodeOrNull($"Poly/Skeleton3D/{sourceName}") is MeshInstance3D m3d)
		{
			target.Mesh = m3d.Mesh;
		}
		else
		{
			throw new Exception("Invalid Body Mesh");
		}
	}

	[ScriptMethod]
	public void StartRagdoll(Vector3? force = null)
	{
		force ??= Vector3.Zero;
		Rpc(nameof(NetStartRagdoll), force.Value);
	}

	[ScriptMethod]
	public void StopRagdoll()
	{
		Rpc(nameof(NetStopRagdoll));
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private async void NetStartRagdoll(Vector3 force)
	{
		if (_ragdollBoneSim == null) return;

		if (_lastPhysicalBoneSim != null) return;

		// need duplicates cuz godot won't adapt dynamically to bones
		PhysicalBoneSimulator3D s = (PhysicalBoneSimulator3D)_ragdollBoneSim.Duplicate();

		VelocityPhysicalBone = s.GetNode<PhysicalBone3D>("Physical Bone UpperTorso");

		Skeleton.AddChild(s);

		s.Active = true;
		s.PhysicalBonesStartSimulation();

		_lastPhysicalBoneSim = s;

		VelocityPhysicalBone.LinearVelocity = force / VelocityPhysicalBone.GravityScale;
		Ragdolling = true;
		RagdollStarted.Invoke();
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private void NetStopRagdoll()
	{
		if (_lastPhysicalBoneSim == null) return;

		_lastPhysicalBoneSim.PhysicalBonesStopSimulation();
		_lastPhysicalBoneSim.Active = false;
		_lastPhysicalBoneSim.QueueFree();
		_lastPhysicalBoneSim = null;

		Ragdolling = false;
		RagdollStopped.Invoke();
	}

	[ScriptMethod]
	public override Dynamic GetAttachment(CharacterAttachmentEnum attachmentEnum)
	{
		if (!_attachmentEnumToDyn.TryGetValue(attachmentEnum, out Dynamic? dyn))
		{
			Node3D a = GetNode3DAttachment(attachmentEnum);
			dyn = New<Dynamic>();
			dyn.OverrideGDNode(a);
			_attachmentEnumToDyn[attachmentEnum] = dyn;
		}

		return dyn;
	}

	public Node3D GetNode3DAttachment(CharacterAttachmentEnum attachmentEnum)
	{
		Node3D result = attachmentEnum switch
		{
			CharacterAttachmentEnum.Head => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_Head/HeadAttachment",
				"Character/Poly/Skeleton3D/Head_2/HeadAttachment"
			),
			CharacterAttachmentEnum.UpperTorso => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_UpperTorso/UpperTorsoAttachment",
				"Character/Poly/Skeleton3D/UpperTorso/UpperTorsoAttachment"
			),
			CharacterAttachmentEnum.LowerTorso => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_LowerTorso/LowerTorsoAttachment",
				"Character/Poly/Skeleton3D/LowerTorso/LowerTorsoAttachment"
			),
			CharacterAttachmentEnum.ShoulderLeft => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_UpperArm_L/ShoulderLeftAttachment",
				"Character/Poly/Skeleton3D/UpperArm_L/LeftShoulderAttachment"
			),
			CharacterAttachmentEnum.ShoulderRight => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_UpperArm_R/RightShoulderAttachment",
				"Character/Poly/Skeleton3D/UpperArm_R/RightShoulderAttachment"
			),
			CharacterAttachmentEnum.ElbowLeft => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_LowerArm_L/LeftElbowAttachment",
				"Character/Poly/Skeleton3D/LowerArm_L/LeftElbowAttachment"
			),
			CharacterAttachmentEnum.ElbowRight => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_LowerArm_R/RightElbowAttachment",
				"Character/Poly/Skeleton3D/LowerArm_R/RightElbowAttachment"
			),
			CharacterAttachmentEnum.HandLeft => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_Hand_L/LeftHandAttachment",
				"Character/Poly/Skeleton3D/Hand_L/LeftHandAttachment"
			),
			CharacterAttachmentEnum.HandRight => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_Hand_R/RightHandAttachment",
				"Character/Poly/Skeleton3D/Hand_R/RightHandAttachment"
			),
			CharacterAttachmentEnum.LegLeft => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_UpperLeg_L/LeftLegAttachment",
				"Character/Poly/Skeleton3D/UpperLeg_L/LeftLegAttachment"
			),
			CharacterAttachmentEnum.LegRight => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_UpperLeg_R/RightLegAttachment",
				"Character/Poly/Skeleton3D/UpperLeg_R/RightLegAttachment"
			),
			CharacterAttachmentEnum.KneeLeft => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_LowerLeg_L/LeftKneeAttachment",
				"Character/Poly/Skeleton3D/LowerLeg_L/LeftKneeAttachment"
			),
			CharacterAttachmentEnum.KneeRight => GetRequiredNodeCompat<Node3D>(
				"Character/Poly/Skeleton3D/O_LowerLeg_R/RightKneeAttachment",
				"Character/Poly/Skeleton3D/LowerLeg_R/RightKneeAttachment"
			),
			_ => throw new NotImplementedException(),
		};

		return result;
	}

	public override void RecvBlendValue(CharacterModelBlendEnum blendName, float blendValue)
	{
		string propName = "";
		switch (blendName)
		{
			case CharacterModelBlendEnum.Sitting:
				propName = "parameters/Sit/blend_amount";
				break;
			case CharacterModelBlendEnum.ToolHoldLeft:
				propName = "parameters/GearHold_L/blend_amount";
				break;
			case CharacterModelBlendEnum.ToolHoldRight:
				propName = "parameters/GearHold_R/blend_amount";
				break;
			case CharacterModelBlendEnum.LookX:
				propName = "parameters/LookXAdd/add_amount";
				break;
			case CharacterModelBlendEnum.LookY:
				propName = "parameters/LookYAdd/add_amount";
				break;
		}

		if (propName != "")
		{
			_blendTargets[propName] = blendValue;
		}
	}

	public override void RecvSpeedValue(float speedValue)
	{
		if (AnimTree == null) return;
		AnimTree.Set("parameters/TimeScale/scale", speedValue);
	}

	public override void ApplyCameraModifier(Camera camera)
	{
		Camera3D cam3D = camera.Camera3D;
		Transform3D camTransform = cam3D.GlobalTransform;
		Transform3D charTransform = GetGlobalTransform();

		Vector3 camForward = -camTransform.Basis.Z.Normalized();

		Vector3 localForward = charTransform.Basis.Inverse() * camForward;
		localForward = localForward.Normalized();

		float lookY = Mathf.Clamp(localForward.Y, -1f, 1f);
		float lookX = -localForward.X;

		if (lookX != _lastLookBlendX)
		{
			_lastLookBlendX = lookX;
		}

		if (lookY != _lastLookBlendY)
		{
			_lastLookBlendY = lookY;
		}

		NetRecvLookBlend(lookY, lookX);

		if (Time.GetTicksMsec() / 1000.0 >= _lastNetUpdateTime + NetLookBlendUpdateInterval)
		{
			_lastNetUpdateTime = Time.GetTicksMsec() / 1000.0;
			Rpc(nameof(NetRecvLookBlend), lookY, lookX);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetRecvLookBlend(float lookYBlend, float lookXBlend)
	{
		RecvBlendValue(CharacterModelBlendEnum.LookX, lookXBlend);
		RecvBlendValue(CharacterModelBlendEnum.LookY, lookYBlend);
	}

	[ScriptMethod]
	public void LoadAppearance(string userID, bool loadTool = true)
	{
		ClearAppearance();
		_ = InternalLoadAppearance(userID, loadTool);
	}

	[ScriptMethod]
	public void ClearAppearance()
	{
		HeadColor = _defaultBodyColor;
		TorsoColor = _defaultBodyColor;
		LeftArmColor = _defaultBodyColor;
		RightArmColor = _defaultBodyColor;
		LeftLegColor = _defaultBodyColor;
		RightLegColor = _defaultBodyColor;
		FaceImage = null;
		_faceOverrided = false;
		_bodyOverrided = false;

		foreach (Instance item in GetChildren())
		{
			if (item is Accessory or Clothing)
			{
				item.Delete();
			}
		}
	}

	private void MeshSetAlbedo(GeometryInstance3D mesh, Color albedo)
	{
		(ShaderMaterial opaque, ShaderMaterial transparent) = mesh switch
		{
			_ when mesh == TorsoMeshInstance =>
				(_torsoMat, _transparentTorsoMat),

			_ when mesh == LeftArmMeshInstance =>
				(_leftArmMat, _transparentLeftArmMat),

			_ when mesh == RightArmMeshInstance =>
				(_rightArmMat, _transparentRightArmMat),

			_ when mesh == LeftHandMeshInstance =>
				(_leftHandMat, _transparentLeftHandMat),

			_ when mesh == RightHandMeshInstance =>
				(_rightHandMat, _transparentRightHandMat),

			_ when mesh == LeftLegMeshInstance =>
				(_leftLegMat, _transparentLeftLegMat),

			_ when mesh == RightLegMeshInstance =>
				(_rightLegMat, _transparentRightLegMat),

			_ => throw new ArgumentOutOfRangeException(
				nameof(mesh),
				"Unknown character body mesh."
			),
		};

		mesh.MaterialOverride = albedo.A == 1f ? opaque : transparent;
		mesh.SetInstanceShaderParameter(_albedoParam, albedo);
	}

	private static Color MeshGetAlbedo(GeometryInstance3D mesh) => (Color)mesh.GetInstanceShaderParameter(_albedoParam);

	internal async Task<AvatarLoadResponse> InternalLoadAppearance(string userID, bool loadTool = false, bool loadToolNpc = false)
	{
		_loadAppearanceCount++;

		// Prevent reloading
		int myCount = _loadAppearanceCount;

		APIAvatarResponse avatarData = await BVAPI.GetUserAvatarFromID(userID);
		if (myCount != _loadAppearanceCount) throw new OperationCanceledException("The avatar is cancelled");

		if (IsDeleted)
		{
			throw new OperationCanceledException("The avatar is deleted");
		}

		// Apply body color
		HeadColor = Color.FromString(avatarData.Colors.Head, _defaultBodyColor);
		TorsoColor = Color.FromString(avatarData.Colors.Torso, _defaultBodyColor);
		LeftArmColor = Color.FromString(avatarData.Colors.LeftArm, _defaultBodyColor);
		RightArmColor = Color.FromString(avatarData.Colors.RightArm, _defaultBodyColor);
		LeftLegColor = Color.FromString(avatarData.Colors.LeftLeg, _defaultBodyColor);
		RightLegColor = Color.FromString(avatarData.Colors.RightLeg, _defaultBodyColor);

		bool hasTool = false;

		foreach (APIAvatarAsset asset in avatarData.Assets)
		{
			if (asset.Type == "clothing")
			{
				BVImageAsset txt = New<BVImageAsset>();
				txt.ImageID = asset.ID.ToString();
				Clothing c = New<Clothing>();
				c.Name = asset.Name;
				c.Image = txt;
				c.Parent = this;
			}
			else if (asset.Type == "face")
			{
				if (_faceOverrided) continue;
				BVImageAsset face = New<BVImageAsset>();
				face.ImageID = asset.ID.ToString();
				FaceImage = face;
			}
			else if (asset.Type == "hat")
			{
				try
				{
					Accessory? accessory = await Root.Insert.AccessoryAsync(asset.ID);
					if (myCount != _loadAppearanceCount) { accessory?.Delete(); throw new OperationCanceledException("The avatar is cancelled"); }
					if (IsDeleted)
					{
						accessory?.Delete();
						throw new OperationCanceledException("The avatar is deleted");
					}
					accessory?.Parent = this;
				}
				catch (Exception ex)
				{
					BV.PrintErr(ex);
				}
			}
			else if (asset.Type == "tool")
			{
				if (Parent is Player plr && loadTool)
				{
					hasTool = true;
					try
					{
						Tool? tool = await Root.Insert.ToolAsync(asset.ID);
						if (myCount != _loadAppearanceCount) { tool?.Delete(); throw new OperationCanceledException("The avatar is cancelled"); }
						if (IsDeleted)
						{
							tool?.Delete();
							throw new OperationCanceledException("The avatar is deleted");
						}
						tool?.Parent = plr.Inventory;
					}
					catch (Exception ex)
					{
						BV.PrintErr(ex);
					}
				}
				else if (Parent is NPC npc && loadToolNpc)
				{
					hasTool = true;
					try
					{
						Tool? tool = await Root.Insert.ToolAsync(asset.ID);
						if (myCount != _loadAppearanceCount) { tool?.Delete(); throw new OperationCanceledException("The avatar is cancelled"); }
						if (IsDeleted)
						{
							tool?.Delete();
							throw new OperationCanceledException("The avatar is deleted");
						}
						if (tool != null)
							npc.EquipTool(tool);
					}
					catch (Exception ex)
					{
						BV.PrintErr(ex);
					}
				}
			}
		}

		AssetLoadCheckout();

		return new() { HasTool = hasTool };
	}

	internal async Task WaitForAppearanceLoad()
	{
		if (FaceImage != null && !FaceImage.IsResourceLoaded)
		{
			await FaceImage.ResourceLoadedInternal.Wait();
		}
		if (BodyMesh != null && !BodyMesh.IsResourceLoaded)
		{
			await BodyMesh.ResourceLoadedInternal.Wait();
		}

		Instance checkOn = this;

		// Check on NPC for loading tools
		if (Parent is NPC)
		{
			checkOn = Parent;
		}

		foreach (var item in checkOn.GetDescendants())
		{
			if (item is Mesh m)
			{
				if (m.Loading)
				{
					await m.Loaded.Wait();
				}
			}
			else if (item is Clothing c)
			{
				if (c.Image != null && !c.Image.IsResourceLoaded)
				{
					await c.Image.ResourceLoadedInternal.Wait();
				}
			}
		}
	}

	internal void QueueRenderCloth()
	{
		_updateClothDirty = true;
	}

	public void SetAnimationOverrideTo(bool to)
	{
		AnimTree.Active = !to;
	}

	internal struct AvatarLoadResponse()
	{
		public bool HasTool = false;
	}
}
