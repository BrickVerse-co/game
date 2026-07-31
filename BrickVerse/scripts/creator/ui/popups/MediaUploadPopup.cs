// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BrickVerse.Creator.Utils;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>Upload form shared by raw texture and sound assets.</summary>
public sealed partial class MediaUploadPopup : PopupWindowBase
{
	public enum MediaKind { Texture, Sound }
	private enum OwnerType { User, Guild }
	private sealed record GuildOption(string Id, string Name);

	private static readonly Dictionary<string, string> TextureMimes = new(StringComparer.OrdinalIgnoreCase)
	{
		[".png"] = "image/png",
		[".jpg"] = "image/jpeg",
		[".jpeg"] = "image/jpeg"
	};
	private static readonly Dictionary<string, string> SoundMimes = new(StringComparer.OrdinalIgnoreCase)
	{
		[".ogg"] = "audio/ogg",
		[".wav"] = "audio/wav",
		[".mp3"] = "audio/mpeg"
	};

	private readonly MediaKind _kind;
	private readonly List<GuildOption> _guilds = [];
	private FileDialog _fileDialog = null!;
	private TextureRect _preview = null!;
	private Label _previewMessage = null!;
	private Label _fileName = null!;
	private Label _fileDetails = null!;
	private Label _error = null!;
	private Label _busy = null!;
	private LineEdit _name = null!;
	private TextEdit _description = null!;
	private Button _personal = null!;
	private Button _guild = null!;
	private OptionButton _guildDropdown = null!;
	private Button _choose = null!;
	private Button _upload = null!;
	private Button _cancel = null!;
	private byte[]? _data;
	private string _sourcePath = "";
	private string _mime = "";
	private bool _isBusy;

	public MediaUploadPopup(MediaKind kind)
	{
		_kind = kind;
	}

	public override void _Ready()
	{
		BuildUi();
		base._Ready();
	}

	public async void Open()
	{
		Show();
		try
		{
			CreatorGuildItem[] guilds = await CreatorAPI.GetUserGuilds(limitToEditable: true);
			foreach (CreatorGuildItem guild in guilds)
			{
				_guilds.Add(new GuildOption(guild.Id, guild.Name));
				_guildDropdown.AddItem(guild.Name);
			}
			if (_guilds.Count > 0)
				_guildDropdown.Select(0);
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to load upload guilds: {ex.Message}");
		}
		RefreshOwner();
		_fileDialog.PopupCenteredRatio(0.72f);
	}

	private void BuildUi()
	{
		string noun = _kind == MediaKind.Texture ? "texture" : "sound";
		Title = $"Upload {noun}";
		Size = new Vector2I(780, 530);
		MinSize = new Vector2I(680, 480);
		Transient = true;
		Exclusive = true;

		VBoxContainer root = new();
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize);
		root.AddThemeConstantOverride("separation", 14);
		AddChild(root);

		Label title = new() { Text = $"Upload {noun}", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 22);
		root.AddChild(title);
		root.AddChild(new Label
		{
			Text = _kind == MediaKind.Texture
				? "Choose a PNG or JPEG image, review it, then publish it to your assets."
				: "Choose an OGG, WAV, or MP3 audio file, review it, then publish it to your assets.",
			HorizontalAlignment = HorizontalAlignment.Center
		});

		HBoxContainer body = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 18);
		root.AddChild(body);

		VBoxContainer left = new() { CustomMinimumSize = new Vector2(330, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		body.AddChild(left);
		PanelContainer previewPanel = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		left.AddChild(previewPanel);
		_preview = new()
		{
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
		};
		previewPanel.AddChild(_preview);
		_previewMessage = new()
		{
			Text = _kind == MediaKind.Texture ? "Image preview" : "♪\nAudio preview",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		_previewMessage.AddThemeFontSizeOverride("font_size", _kind == MediaKind.Sound ? 26 : 18);
		previewPanel.AddChild(_previewMessage);

		HBoxContainer fileRow = new();
		left.AddChild(fileRow);
		_fileName = new() { Text = $"No {noun} selected", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
		fileRow.AddChild(_fileName);
		_choose = new() { Text = "Choose file" };
		fileRow.AddChild(_choose);
		_fileDetails = new() { Text = "No file selected" };
		left.AddChild(_fileDetails);

		VBoxContainer form = new() { CustomMinimumSize = new Vector2(340, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		form.AddThemeConstantOverride("separation", 8);
		body.AddChild(form);
		form.AddChild(new Label { Text = "Name" });
		_name = new() { PlaceholderText = $"My {noun}", MaxLength = 64 };
		form.AddChild(_name);
		form.AddChild(new Label { Text = "Description" });
		_description = new() { PlaceholderText = "Optional description", CustomMinimumSize = new Vector2(0, 100) };
		form.AddChild(_description);
		form.AddChild(new Label { Text = "Owner" });
		HBoxContainer owners = new();
		form.AddChild(owners);
		ButtonGroup ownerGroup = new();
		_personal = new() { Text = "Personal", ToggleMode = true, ButtonPressed = true, ButtonGroup = ownerGroup, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_guild = new() { Text = "Guild", ToggleMode = true, ButtonGroup = ownerGroup, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		owners.AddChild(_personal);
		owners.AddChild(_guild);
		_guildDropdown = new() { Visible = false };
		form.AddChild(_guildDropdown);
		_error = new() { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_error.AddThemeColorOverride("font_color", new Color(1.0f, 0.38f, 0.38f));
		form.AddChild(_error);

		HBoxContainer footer = new();
		root.AddChild(footer);
		_busy = new() { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		footer.AddChild(_busy);
		_cancel = new() { Text = "Cancel" };
		footer.AddChild(_cancel);
		_upload = new() { Text = $"Upload {noun}", Disabled = true };
		footer.AddChild(_upload);

		_fileDialog = new()
		{
			Access = FileDialog.AccessEnum.Filesystem,
			FileMode = FileDialog.FileModeEnum.OpenFile,
			UseNativeDialog = true,
			Filters = _kind == MediaKind.Texture
				? ["*.png;PNG image", "*.jpg,*.jpeg;JPEG image"]
				: ["*.ogg;Ogg audio", "*.wav;Wave audio", "*.mp3;MP3 audio"]
		};
		AddChild(_fileDialog);

		_choose.Pressed += () => _fileDialog.PopupCenteredRatio(0.72f);
		_cancel.Pressed += QueueFree;
		_personal.Pressed += RefreshOwner;
		_guild.Pressed += RefreshOwner;
		_fileDialog.FileSelected += LoadFile;
		_upload.Pressed += Submit;
	}

	private void LoadFile(string path)
	{
		HideError();
		try
		{
			Dictionary<string, string> allowed = _kind == MediaKind.Texture ? TextureMimes : SoundMimes;
			string extension = Path.GetExtension(path);
			if (!allowed.TryGetValue(extension, out string? mime))
				throw new InvalidDataException($"Unsupported file type. Choose {string.Join(", ", allowed.Keys.Select(x => x.TrimStart('.').ToUpperInvariant()))}.");

			byte[] bytes = File.ReadAllBytes(path);
			if (bytes.Length == 0)
				throw new InvalidDataException("The selected file is empty.");
			if (!MatchesSignature(bytes, extension))
				throw new InvalidDataException("The file contents do not match its extension.");

			_data = bytes;
			_sourcePath = path;
			_mime = mime;
			_fileName.Text = Path.GetFileName(path);
			_fileDetails.Text = $"{mime}  •  {FormatBytes(bytes.LongLength)}";
			if (string.IsNullOrWhiteSpace(_name.Text))
				_name.Text = Path.GetFileNameWithoutExtension(path);

			if (_kind == MediaKind.Texture)
			{
				Image image = Image.LoadFromFile(path);
				if (image.IsEmpty())
					throw new InvalidDataException("Godot could not decode the selected image.");
				_preview.Texture = ImageTexture.CreateFromImage(image);
				_previewMessage.Visible = false;
				_fileDetails.Text += $"  •  {image.GetWidth()}×{image.GetHeight()}";
			}
			else
			{
				_preview.Texture = null;
				_previewMessage.Text = $"♪\n{extension.TrimStart('.').ToUpperInvariant()} audio";
				_previewMessage.Visible = true;
			}
			_upload.Disabled = false;
		}
		catch (Exception ex)
		{
			_data = null;
			_upload.Disabled = true;
			ShowError(ex.Message);
		}
	}

	private async void Submit()
	{
		if (_isBusy || _data == null)
			return;
		string assetName = _name.Text.Trim();
		string description = _description.Text.Trim();
		if (assetName.Length == 0) { ShowError("Name is required."); _name.GrabFocus(); return; }
		if (assetName.Length > 64) { ShowError("Name must be 64 characters or less."); return; }
		if (description.Length > 1000) { ShowError("Description must be 1,000 characters or less."); return; }
		if (_guild.ButtonPressed && (_guildDropdown.Selected < 0 || _guildDropdown.Selected >= _guilds.Count))
		{
			ShowError("Select a valid guild.");
			return;
		}

		SetBusy(true, $"Uploading {_kind.ToString().ToLowerInvariant()}...");
		try
		{
			bool toGuild = _guild.ButtonPressed;
			string ownerId = toGuild ? _guilds[_guildDropdown.Selected].Id : CreatorAPI.UserID;
			CreatorPublishResponse response = await CreatorAPI.UploadAsset(
				_data, 0, _kind == MediaKind.Texture ? "TEXTURE" : "SOUND",
				Path.GetFileName(_sourcePath), assetName, description, ownerId,
				toGuild ? OwnerType.Guild.ToString() : OwnerType.User.ToString(), _mime);
			BV.Print($"{_kind} uploaded successfully. Asset ID: {response.Link}");
			QueueFree();
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to upload {_kind}: {ex}");
			ShowError($"Upload failed: {ex.Message}");
			SetBusy(false, "");
		}
	}

	private void RefreshOwner()
	{
		bool guild = _guild.ButtonPressed;
		_guildDropdown.Visible = guild;
		_guildDropdown.Disabled = _isBusy || _guilds.Count == 0;
		if (guild && _guilds.Count == 0)
			ShowError("You do not have upload permission in any guilds.");
	}

	private void SetBusy(bool busy, string message)
	{
		_isBusy = busy;
		_busy.Text = message;
		_busy.Visible = busy;
		_choose.Disabled = busy;
		_cancel.Disabled = busy;
		_upload.Disabled = busy || _data == null;
		_name.Editable = !busy;
		_description.Editable = !busy;
		_personal.Disabled = busy;
		_guild.Disabled = busy;
		RefreshOwner();
	}

	private void ShowError(string message) { _error.Text = message; _error.Visible = true; }
	private void HideError() { _error.Text = ""; _error.Visible = false; }

	private static string FormatBytes(long bytes)
	{
		string[] units = ["B", "KB", "MB", "GB"];
		double size = bytes;
		int unit = 0;
		while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
		return $"{size:0.##} {units[unit]}";
	}

	private static bool MatchesSignature(byte[] data, string extension)
	{
		return extension.ToLowerInvariant() switch
		{
			".png" => data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
			".jpg" or ".jpeg" => data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff,
			".wav" => data.Length >= 12 && System.Text.Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(data, 8, 4) == "WAVE",
			".ogg" => data.Length >= 4 && System.Text.Encoding.ASCII.GetString(data, 0, 4) == "OggS",
			".mp3" => data.Length >= 3 && (System.Text.Encoding.ASCII.GetString(data, 0, 3) == "ID3" || (data[0] == 0xff && (data[1] & 0xe0) == 0xe0)),
			_ => false
		};
	}
}
