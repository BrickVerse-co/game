// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileListCard : Button
{
	private TextureRect _image = null!;
	private Label _title = null!;
	private Label _meta = null!;
	private Label _detail = null!;
	private TextureRect? _currencyIcon;

	public override void _Ready()
	{
		MobileMotion.Bind(this);
		_image = GetNode<TextureRect>("Content/Image");
		_title = GetNode<Label>("Content/Copy/Title");
		_meta = GetNodeOrNull<Label>("Content/Copy/Price/Meta") ?? GetNode<Label>("Content/Copy/Meta");
		_detail = GetNode<Label>("Content/Copy/Detail");
		_currencyIcon = GetNodeOrNull<TextureRect>("Content/Copy/Price/CurrencyIcon") ?? GetNodeOrNull<TextureRect>("Content/Copy/CurrencyIcon");
	}

	public void Configure(string title, string meta = "", string detail = "", string imageUrl = "")
	{
		_title.Text = title;
		_meta.Text = meta;
		_meta.Visible = !string.IsNullOrWhiteSpace(meta);
		if (_currencyIcon != null) _currencyIcon.Visible = meta.EndsWith(" Cubes", System.StringComparison.Ordinal);
		_detail.Text = detail;
		_detail.Visible = !string.IsNullOrWhiteSpace(detail);
		_image.Visible = !string.IsNullOrWhiteSpace(imageUrl);
		if (_image.Visible) _ = LoadImage(imageUrl);
	}

	private async System.Threading.Tasks.Task LoadImage(string imageUrl)
	{
		const string marker = "/v3/thumbnails/asset/";
		int markerIndex = imageUrl.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
		if (markerIndex >= 0) imageUrl = await BVAPI.ResolveThumbnailUrl("ASSET", imageUrl[(markerIndex + marker.Length)..]);
		if (string.IsNullOrWhiteSpace(imageUrl)) return;
		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = imageUrl }, resource => { if (IsInstanceValid(_image)) _image.Texture = (Texture2D)resource; });
	}
}
