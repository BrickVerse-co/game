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
	private TextureRect? _verified;

	public override void _Ready()
	{
		MobileMotion.Bind(this);
		_image = GetNodeOrNull<TextureRect>("Content/ImagePanel/Image") ?? GetNode<TextureRect>("Content/Image");
		_title = GetNode<Label>("Content/Copy/Title");
		_meta = GetNodeOrNull<Label>("Content/Copy/Price/Meta") ?? GetNode<Label>("Content/Copy/Meta");
		_detail = GetNodeOrNull<Label>("Content/Copy/DetailRow/Detail") ?? GetNode<Label>("Content/Copy/Detail");
		_currencyIcon = GetNodeOrNull<TextureRect>("Content/Copy/Price/CurrencyIcon") ?? GetNodeOrNull<TextureRect>("Content/Copy/CurrencyIcon");
		_verified = GetNodeOrNull<TextureRect>("Content/Copy/DetailRow/Verified") ?? GetNodeOrNull<TextureRect>("Verified");
	}

	public void SetVerified(bool verified) { if (_verified != null) _verified.Visible = verified; }

	public void Configure(string title, string meta = "", string detail = "", string imageUrl = "")
	{
		_title.Text = title;
		_meta.Text = meta;
		_meta.Visible = !string.IsNullOrWhiteSpace(meta);
		if (_currencyIcon != null) _currencyIcon.Visible = meta.EndsWith(" Cubes", System.StringComparison.Ordinal);
		_detail.Text = detail;
		_detail.Visible = !string.IsNullOrWhiteSpace(detail);
		_image.Visible = !string.IsNullOrWhiteSpace(imageUrl);
		if (_image.GetParent() is PanelContainer imagePanel) imagePanel.Visible = _image.Visible;
		if (_image.Visible) _ = LoadImage(imageUrl);
	}

	public void SetDetail(string detail)
	{
		_detail.Text = detail;
		_detail.Visible = !string.IsNullOrWhiteSpace(detail);
	}

	private async System.Threading.Tasks.Task LoadImage(string imageUrl)
	{
		const string marketplaceMarker = "marketplace-item://";
		if (imageUrl.StartsWith(marketplaceMarker, System.StringComparison.Ordinal)) imageUrl = await BVAPI.ResolveThumbnailUrl("MARKETPLACE_ITEM", imageUrl[marketplaceMarker.Length..]);
		const string marker = "/v3/thumbnails/asset/";
		int markerIndex = imageUrl.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
		if (markerIndex >= 0) imageUrl = await BVAPI.ResolveThumbnailUrl("ASSET", imageUrl[(markerIndex + marker.Length)..]);
		if (string.IsNullOrWhiteSpace(imageUrl)) return;
		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = imageUrl }, resource => { if (IsInstanceValid(_image)) _image.Texture = (Texture2D)resource; });
	}
}
