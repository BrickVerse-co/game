// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Resources;
using Godot;

namespace BrickVerse.Creator.Properties;

/// <summary>
/// Shows the actual resolved Font resource for a FontAsset in the Creator
/// Properties panel. This is deliberately driven by ResourceLoaded so the
/// sample also reflects changes to a built-in font's family, weight, or style.
/// </summary>
public sealed partial class FontAssetSubview : Control, IPropertySubview
{
	private const string PreviewText = "The quick brown fox jumps over the lazy dog.";

	public NetworkedObject TargetObject { get; set; } = null!;

	private FontAsset _fontAsset = null!;
	private Label _sample = null!;
	private Label _status = null!;

	public override void _Ready()
	{
		_fontAsset = (FontAsset)TargetObject;
		_sample = GetNode<Label>("Layout/Sample");
		_status = GetNode<Label>("Layout/Status");
		_fontAsset.ResourceLoaded += OnResourceLoaded;

		if (_fontAsset.Resource is Font font)
		{
			ShowFont(font);
		}
		else
		{
			ShowLoadingState();
		}
	}

	private void OnResourceLoaded(Resource resource)
	{
		if (resource is Font font)
		{
			ShowFont(font);
		}
		else
		{
			ShowLoadingState();
		}
	}

	private void ShowFont(Font font)
	{
		_sample.Text = PreviewText;
		_sample.AddThemeFontOverride("font", font);
		_status.Text = "Live font preview";
	}

	private void ShowLoadingState()
	{
		_sample.Text = "Loading font preview…";
		_sample.RemoveThemeFontOverride("font");
		_status.Text = "Font resource is loading";
	}

	public override void _ExitTree()
	{
		if (_fontAsset != null)
		{
			_fontAsset.ResourceLoaded -= OnResourceLoaded;
		}
	}
}
