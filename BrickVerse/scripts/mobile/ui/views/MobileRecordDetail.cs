// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;
using Godot;

namespace BrickVerse.Mobile.UI;

public sealed record MobileRecordDetailArgs(string Title, string Meta, string Description, string ImageUrl, MobileViewEnum ReturnView);

public partial class MobileRecordDetail : MobileViewBase
{
	private Label _title = null!;
	private Label _meta = null!;
	private Label _description = null!;
	private TextureRect _image = null!;
	private MobileViewEnum _returnView = MobileViewEnum.Dev;

	public override void _Ready()
	{
		_title = GetNode<Label>("Layout/Title");
		_meta = GetNode<Label>("Layout/Meta");
		_description = GetNode<Label>("Layout/Scroll/Description");
		_image = GetNode<TextureRect>("Layout/Image");
		GetNode<Button>("Layout/Header/Back").Pressed += () => MobileUI.Singleton.SwitchTo(_returnView, _returnView);
	}

	public override void ShowView(object? args)
	{
		if (args is not MobileRecordDetailArgs detail) return;
		_returnView = detail.ReturnView;
		_title.Text = detail.Title;
		_meta.Text = detail.Meta;
		_description.Text = string.IsNullOrWhiteSpace(detail.Description) ? "No description provided." : detail.Description;
		_image.Visible = !string.IsNullOrWhiteSpace(detail.ImageUrl);
		if (_image.Visible) _ = LoadImage(detail.ImageUrl);
	}

	private async System.Threading.Tasks.Task LoadImage(string url)
	{
		const string marker = "/v3/thumbnails/asset/";
		int index = url.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
		if (index >= 0) url = await BVAPI.ResolveThumbnailUrl("ASSET", url[(index + marker.Length)..]);
		if (string.IsNullOrWhiteSpace(url)) return;
		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, resource => { if (IsInstanceValid(_image)) _image.Texture = (Texture2D)resource; });
	}
}
