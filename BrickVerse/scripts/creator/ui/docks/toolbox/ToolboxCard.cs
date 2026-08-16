using Godot;
using System;
using System.Threading.Tasks;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Shared.AssetLoaders;
using BrickVerse.Utils;
using Mesh = BrickVerse.Datamodel.Mesh;

namespace BrickVerse.Creator.UI;

public partial class ToolboxCard : Button
{
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Label _nameLabel = null!;
	[Export] private Label _byLabel = null!;
	[Export] private Control _audioPreView = null!;
	[Export] private BaseButton _playButton = null!;
	private AudioStreamPlayer? _previewSound;
	private int _clickSerial;
	private bool _suppressNextPressed;
	public APILibraryItem ItemData;
	public LibraryQueryTypeEnum ItemType;
	public Toolbox ToolboxParent = null!;

	public override void _Ready()
	{
		if (ItemType == LibraryQueryTypeEnum.Audio) { _audioPreView.Visible = true; _thumbnailRect.Texture = GD.Load<Texture2D>("res://assets/textures/creator/toolbox/Sound.svg"); }
		else { _audioPreView.Visible = false; WebAssetLoader.Singleton.GetResource(new() { URL = ItemData.ThumbnailUrl }, r => _thumbnailRect.Texture = (Texture2D)r); }
		_nameLabel.Text = ItemData.Name; _byLabel.Text = "By " + ItemData.CreatorName; _playButton.Toggled += OnPlayToggled;
		base._Ready();
	}

	public override void _GuiInput(InputEvent input)
	{
		if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
		{
			ToolboxItemContextMenu menu = new() { ItemData = ItemData, ItemType = ItemType, ParentCard = this }; AddChild(menu); menu.PopupAtCursor();
		}
		if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left, DoubleClick: true })
		{
			_clickSerial++; _suppressNextPressed = true; _ = InsertAssetAsync(ItemData, ItemType); AcceptEvent();
		}
		base._GuiInput(input);
	}

	public override Variant _GetDragData(Vector2 atPosition)
	{
		TextureRect preview = new() { Texture = _thumbnailRect.Texture, CustomMinimumSize = new Vector2(96, 96), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
		SetDragPreview(preview);
		return new ToolboxAssetDragData { DragType = DragType.ToolboxAsset, AssetID = ItemData.ID, AssetName = ItemData.Name, AssetType = (int)ItemType }.Serialize();
	}

	public override async void _Pressed()
	{
		if (_suppressNextPressed) { _suppressNextPressed = false; return; }
		int click = ++_clickSerial;
		await ToSignal(GetTree().CreateTimer(0.24), SceneTreeTimer.SignalName.Timeout);
		if (click == _clickSerial) ToolboxAssetDetailsWindow.Open(ItemData, ItemType);
		base._Pressed();
	}

	private void OnPlayToggled(bool on)
	{
		if (!on) { _previewSound?.Stop(); return; }
		_previewSound ??= new AudioStreamPlayer();
		if (_previewSound.GetParent() == null) { _previewSound.Finished += () => _playButton.SetPressedNoSignal(false); AddChild(_previewSound); }
		AssetLoader.Singleton.GetResource(new() { ID = ItemData.ID, Type = ResourceType.Sound }, r =>
		{
			if (ToolboxParent.SoundPreviewingCard is { } other && IsInstanceValid(other) && other != this) other.StopSoundPreview();
			ToolboxParent.SoundPreviewingCard = this; _previewSound.Stream = (AudioStream)r; _previewSound.Play();
		});
	}

	public void StopSoundPreview() { _playButton.SetPressedNoSignal(false); ToolboxParent.SoundPreviewingCard = null; _previewSound?.Stop(); }

	public static async Task<Instance?> InsertAssetAsync(APILibraryItem item, LibraryQueryTypeEnum type, Instance? targetParent = null)
	{
		if (World.Current == null) { CreatorService.Interface.StatusBar?.SetStatus("Open a game before inserting assets"); return null; }
		World root = World.Current; Instance parent = targetParent ?? root.Environment; string name = item.Name.ToPascalCase().RemoveSymbols(); Instance? inserted = null;
		try
		{
			CreatorService.Interface.StatusBar?.SetStatus($"Inserting {item.Name}...");
			switch (type)
			{
				case LibraryQueryTypeEnum.Model:
					inserted = await root.Insert.CreatorImportWebModel(item.ID, name); if (inserted != null) inserted.Parent = parent; break;
				case LibraryQueryTypeEnum.Mesh:
					Mesh mesh = root.New<Mesh>(parent); mesh.Name = name; BVMeshAsset meshAsset = root.New<BVMeshAsset>(); meshAsset.AssetID = item.ID; mesh.Asset = meshAsset; inserted = mesh; break;
				case LibraryQueryTypeEnum.Audio:
					Sound sound = root.New<Sound>(parent); sound.Name = name; BVAudioAsset audio = root.New<BVAudioAsset>(); audio.AudioID = item.ID; sound.Audio = audio; inserted = sound; break;
				case LibraryQueryTypeEnum.Image:
					Image3D image = root.New<Image3D>(parent); image.Name = name; BVImageAsset texture = root.New<BVImageAsset>(); texture.ImageID = item.ID; image.Image = texture; inserted = image; break;
				case LibraryQueryTypeEnum.Font:
					BVFontAsset font = root.New<BVFontAsset>(); font.FontID = item.ID; UILabel label = root.New<UILabel>(parent); label.Name = name; label.Text = name; label.FontAsset = font; inserted = label; break;
			}
			if (inserted is Dynamic dynamic && parent == root.Environment) dynamic.Position = root.CreatorContext.Freelook.GetPlacementPosition();
			if (inserted != null) { root.CreatorContext.Selections.SelectOnly(inserted); root.LinkedSession?.RescanFolder(); CreatorService.Interface.StatusBar?.SetStatus($"Inserted {item.Name} under {parent.Name}"); }
		}
		catch (Exception error) { BV.PrintErr($"Failed to insert toolbox asset {item.ID}: ", error); CreatorService.Interface.PopupAlert(error.Message, "Could not insert asset"); }
		return inserted;
	}
}
