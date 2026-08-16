// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Resources;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class Decal : Dynamic
{
	private ImageAsset? _asset;
	private Godot.Decal _decal = null!;

	private Color _color = new(1, 1, 1);
	private float _energy;
	private PlacementModeEnum _placementMode;
	private FaceEnum _face = FaceEnum.Front;
	private float _faceOffset = 0.002f;
	private Vector2 _faceScale = Vector2.One;

	[Editable, ScriptProperty, DefaultValue(PlacementModeEnum.Free)]
	public PlacementModeEnum PlacementMode
	{
		get => _placementMode;
		set
		{
			_placementMode = value;
			SetProcess(value == PlacementModeEnum.Face);
			if (value == PlacementModeEnum.Face) { _decal.Scale = Vector3.One; UpdateFacePlacement(); }
			else { _decal.Size = Vector3.One; _decal.Position = Vector3.Zero; _decal.Rotation = Vector3.Zero; _decal.Scale = NodeSize; }
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(FaceEnum.Front)]
	public FaceEnum Face { get => _face; set { _face = value; UpdateFacePlacement(); OnPropertyChanged(); } }

	[Editable, ScriptProperty, DefaultValue(0.002f)]
	public float FaceOffset { get => _faceOffset; set { _faceOffset = value; UpdateFacePlacement(); OnPropertyChanged(); } }

	[Editable, ScriptProperty]
	public Vector2 FaceScale { get => _faceScale; set { _faceScale = new(Mathf.Max(0.001f, value.X), Mathf.Max(0.001f, value.Y)); UpdateFacePlacement(); OnPropertyChanged(); } }

	[Editable, ScriptProperty]
	public ImageAsset? Image
	{
		get => _asset;
		set
		{
			if (_asset != null && _asset != value)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.UnlinkFrom(this);
			}
			_asset = value;
			OnResourceLoaded(null);
			if (_asset != null)
			{
				_asset.LinkTo(this);
				_asset.ResourceLoaded += OnResourceLoaded;
				if (_asset.IsResourceLoaded && _asset.Resource != null)
				{
					OnResourceLoaded(_asset.Resource);
				}
				else
				{
					_asset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty, DefaultValue(1)]
	public float Energy
	{
		get => _energy;
		set
		{
			_energy = value;
			_decal.EmissionEnergy = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color Color
	{
		get => _color;
		set
		{
			_color = value;
			_decal.Modulate = value;
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		_decal = new()
		{
			Size = Vector3.One,
			CullMask = 1
		};
		GDNode.AddChild(_decal, @internal: Node.InternalMode.Back);
		Energy = 1;

		base.Init();
	}

	public override void Process(double delta)
	{
		if (_placementMode == PlacementModeEnum.Face) UpdateFacePlacement();
		base.Process(delta);
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		if (_placementMode == PlacementModeEnum.Free) _decal.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	private void OnResourceLoaded(Resource? tex)
	{
		_decal.TextureAlbedo = (Texture2D?)tex ?? null;
	}

	private void UpdateFacePlacement()
	{
		if (_decal == null || _placementMode != PlacementModeEnum.Face || Parent is not Part part) return;
		Vector3 half = part.Size * 0.5f;
		(Vector3 position, Vector3 rotation, Vector3 size) = _face switch
		{
			FaceEnum.Top => (new Vector3(0, half.Y + _faceOffset, 0), Vector3.Zero, new Vector3(part.Size.X * _faceScale.X, 0.02f, part.Size.Z * _faceScale.Y)),
			FaceEnum.Bottom => (new Vector3(0, -half.Y - _faceOffset, 0), new Vector3(Mathf.Pi, 0, 0), new Vector3(part.Size.X * _faceScale.X, 0.02f, part.Size.Z * _faceScale.Y)),
			FaceEnum.Left => (new Vector3(-half.X - _faceOffset, 0, 0), new Vector3(0, 0, -Mathf.Pi / 2), new Vector3(part.Size.Y * _faceScale.X, 0.02f, part.Size.Z * _faceScale.Y)),
			FaceEnum.Right => (new Vector3(half.X + _faceOffset, 0, 0), new Vector3(0, 0, Mathf.Pi / 2), new Vector3(part.Size.Y * _faceScale.X, 0.02f, part.Size.Z * _faceScale.Y)),
			FaceEnum.Back => (new Vector3(0, 0, -half.Z - _faceOffset), new Vector3(-Mathf.Pi / 2, 0, 0), new Vector3(part.Size.X * _faceScale.X, 0.02f, part.Size.Y * _faceScale.Y)),
			_ => (new Vector3(0, 0, half.Z + _faceOffset), new Vector3(Mathf.Pi / 2, 0, 0), new Vector3(part.Size.X * _faceScale.X, 0.02f, part.Size.Y * _faceScale.Y)),
		};
		_decal.Position = position;
		_decal.Rotation = rotation;
		_decal.Size = size;
	}

	[ScriptEnum("DecalPlacementMode")]
	public enum PlacementModeEnum { Free, Face }

	[ScriptEnum("NormalId")]
	public enum FaceEnum { Front, Back, Left, Right, Top, Bottom }
}
