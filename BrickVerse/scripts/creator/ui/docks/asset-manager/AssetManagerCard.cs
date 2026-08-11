using Godot;
using BrickVerse.Schemas.API;
using BrickVerse.Shared.AssetLoaders;
using System;

namespace BrickVerse.Creator.UI;

public sealed partial class AssetManagerCard : PanelContainer
{
	[Export] private TextureButton _preview = null!;
	[Export] private Label _name = null!;
	[Export] private Label _type = null!;
	[Export] private Button _menu = null!;

	public void Setup(CreatorAssetItem item, Action<CreatorAssetItem> insert, Action<CreatorAssetItem> menu)
	{
		_name.Text = item.Name;
		_name.TooltipText = string.IsNullOrWhiteSpace(item.Description) ? item.Name : item.Description;
		_type.Text = item.Type.Replace('_', ' ');
		_preview.Pressed += () => insert(item);
		_preview.TooltipText = $"Insert {item.Name}";
		_preview.GuiInput += e =>
		{
			if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) menu(item);
		};
		_menu.Pressed += () => menu(item);
		if (!string.IsNullOrWhiteSpace(item.IconUrl))
			WebAssetLoader.Singleton.GetResource(new() { URL = item.IconUrl }, resource =>
			{
				if (GodotObject.IsInstanceValid(_preview)) _preview.TextureNormal = resource as Texture2D;
			});
	}
}
