// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class ProceduralSky : Sky
{
	private ProceduralSkyMaterial _mat = null!;

	private float _sunSize = 0.04f;
	private Color _skyTint = new(168 / 255f, 168 / 255f, 168 / 255f, 1);
	private Color _groundColor = new(185 / 255f, 185 / 255f, 185 / 255f, 1);
	private Color _horizonColor = new(185 / 255f, 185 / 255f, 185 / 255f, 1);
	private float _exposure = 1.2f;

	private const float SunSizeConversion = 750;

	[Editable, ScriptProperty]
	public float SunSize
	{
		get => _sunSize;
		set
		{
			_sunSize = value;
			_mat.SunAngleMax = _sunSize * SunSizeConversion;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color SkyTint
	{
		get => _skyTint;
		set
		{
			_skyTint = value;
			_mat.SkyTopColor = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color HorizonColor
	{
		get => _horizonColor;
		set
		{
			_horizonColor = value;
			_mat.SkyHorizonColor = value;
			_mat.GroundBottomColor = value;
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty]
	public Color GroundColor
	{
		get => _groundColor;
		set
		{
			_groundColor = value;
			_mat.GroundBottomColor = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Exposure
	{
		get => _exposure;
		set
		{
			_exposure = value;
			_mat.EnergyMultiplier = value;
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		_mat = new();
		SkyMaterial = _mat;

		base.Init();
	}
}
