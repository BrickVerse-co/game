// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrickVerse.Creator.Utils;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;

namespace BrickVerse.Creator.UI.Popups;

/// <summary>Scene-backed upload form shared by texture and sound assets.</summary>
public sealed partial class MediaUploadPopup : PopupWindowBase
{
	public enum MediaKind { Texture, Sound }
	private enum OwnerType { User, Guild }
	private sealed record GuildOption(string Id, string Name);

	private static readonly Dictionary<string, string> TextureMimes = new(StringComparer.OrdinalIgnoreCase)
	{
		[".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
	};
	private static readonly Dictionary<string, string> SoundMimes = new(StringComparer.OrdinalIgnoreCase)
	{
		[".ogg"] = "audio/ogg", [".wav"] = "audio/wav", [".mp3"] = "audio/mpeg",
	};

	[Export] private Label _titleLabel = null!;
	[Export] private Label _subtitleLabel = null!;
	[Export] private Button _closeButton = null!;
	[Export] private FileDialog _fileDialog = null!;
	[Export] private TextureRect _preview = null!;
	[Export] private TextureRect _kindIcon = null!;
	[Export] private Label _previewMessage = null!;
	[Export] private Label _fileName = null!;
	[Export] private Label _fileDetails = null!;
	[Export] private Label _error = null!;
	[Export] private Label _busy = null!;
	[Export] private LineEdit _name = null!;
	[Export] private TextEdit _description = null!;
	[Export] private Button _personal = null!;
	[Export] private Button _guild = null!;
	[Export] private OptionButton _guildDropdown = null!;
	[Export] private Button _choose = null!;
	[Export] private Button _replace = null!;
	[Export] private Button _upload = null!;
	[Export] private Button _cancel = null!;

	private readonly List<GuildOption> _guilds = [];
	private MediaKind _kind;
	private byte[]? _data;
	private string _sourcePath = "";
	private string _mime = "";
	private bool _isBusy;

	public void Configure(MediaKind kind) => _kind = kind;

	public override void _Ready()
	{
		base._Ready();
		ConfigureUi();
		_closeButton.Pressed += Close;
		_cancel.Pressed += Close;
		_choose.Pressed += OpenFilePicker;
		_replace.Pressed += OpenFilePicker;
		_personal.Pressed += RefreshOwner;
		_guild.Pressed += RefreshOwner;
		_fileDialog.FileSelected += LoadFile;
		_upload.Pressed += Submit;
		ResetForm();
	}

	public async void Open()
	{
		Show();
		ResetForm();
		_guilds.Clear();
		_guildDropdown.Clear();
		try
		{
			CreatorGuildItem[] guilds = await CreatorAPI.GetUserGuilds(limitToEditable: true);
			if (!IsInstanceValid(this)) return;
			foreach (CreatorGuildItem guild in guilds)
			{
				_guilds.Add(new GuildOption(guild.Id, guild.Name));
				_guildDropdown.AddItem(guild.Name);
			}
			if (_guilds.Count > 0) _guildDropdown.Select(0);
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to load upload guilds: {ex.Message}");
		}
		if (!IsInstanceValid(this)) return;
		RefreshOwner();
		OpenFilePicker();
	}

	private void ConfigureUi()
	{
		bool texture = _kind == MediaKind.Texture;
		string noun = texture ? "texture" : "sound";
		Title = $"Upload {noun}";
		_titleLabel.Text = $"Upload {noun}";
		_subtitleLabel.Text = texture
			? "Import, preview, and publish a PNG or JPEG image."
			: "Import, inspect, and publish an OGG, WAV, or MP3 audio file.";
		_kindIcon.Texture = Globals.LoadUIIcon(texture ? "mountain" : "archive");
		_name.PlaceholderText = texture ? "Texture name" : "Sound name";
		_upload.Text = $"Upload {noun}";
		_fileDialog.OkButtonText = texture ? "Open image" : "Open audio";
		_fileDialog.Filters = texture
			? ["*.png ; PNG image", "*.jpg,*.jpeg ; JPEG image"]
			: ["*.ogg ; Ogg audio", "*.wav ; Wave audio", "*.mp3 ; MP3 audio"];
	}

	private void ResetForm()
	{
		_data = null;
		_sourcePath = "";
		_mime = "";
		_name.Text = "";
		_description.Text = "";
		_preview.Texture = null;
		_previewMessage.Text = _kind == MediaKind.Texture
			? "Choose an image to preview it here."
			: "Choose an audio file to inspect it here.";
		_previewMessage.Visible = true;
		_kindIcon.Visible = true;
		_fileName.Text = _kind == MediaKind.Texture ? "No image selected" : "No audio selected";
		_fileDetails.Text = _kind == MediaKind.Texture ? "PNG or JPEG" : "OGG, WAV, or MP3";
		_choose.Visible = true;
		_replace.Visible = false;
		_personal.ButtonPressed = true;
		HideError();
		SetBusy(false, "");
	}

	private void OpenFilePicker()
	{
		if (!_isBusy) _fileDialog.PopupCenteredRatio(0.72f);
	}

	private void Close()
	{
		if (!_isBusy) QueueFree();
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
			if (bytes.Length == 0) throw new InvalidDataException("The selected file is empty.");
			if (!MatchesSignature(bytes, extension)) throw new InvalidDataException("The file contents do not match its extension.");

			_data = bytes;
			_sourcePath = path;
			_mime = mime;
			_fileName.Text = Path.GetFileName(path);
			_fileDetails.Text = $"{mime}  •  {FormatBytes(bytes.LongLength)}";
			if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = Path.GetFileNameWithoutExtension(path);

			if (_kind == MediaKind.Texture)
			{
				Image image = Image.LoadFromFile(path);
				if (image.IsEmpty()) throw new InvalidDataException("Godot could not decode the selected image.");
				_preview.Texture = ImageTexture.CreateFromImage(image);
				_previewMessage.Visible = false;
				_kindIcon.Visible = false;
				_fileDetails.Text += $"  •  {image.GetWidth()}×{image.GetHeight()}";
			}
			else
			{
				_preview.Texture = null;
				_previewMessage.Text = $"{extension.TrimStart('.').ToUpperInvariant()} audio ready to upload";
				_previewMessage.Visible = true;
				_kindIcon.Visible = true;
			}
			_choose.Visible = false;
			_replace.Visible = true;
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
		if (_isBusy || _data == null) return;
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
			if (IsInstanceValid(this)) QueueFree();
		}
		catch (Exception ex)
		{
			BV.PrintErr($"Failed to upload {_kind}: {ex}");
			if (!IsInstanceValid(this)) return;
			ShowError($"Upload failed: {ex.Message}");
			SetBusy(false, "");
		}
	}

	private void RefreshOwner()
	{
		bool guild = _guild.ButtonPressed;
		_guildDropdown.Visible = guild;
		_guildDropdown.Disabled = _isBusy || _guilds.Count == 0;
		if (guild && _guilds.Count == 0) ShowError("You do not have upload permission in any guilds.");
	}

	private void SetBusy(bool busy, string message)
	{
		_isBusy = busy;
		_busy.Text = message;
		_busy.Visible = busy;
		_choose.Disabled = busy;
		_replace.Disabled = busy;
		_cancel.Disabled = busy;
		_closeButton.Disabled = busy;
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

	private static bool MatchesSignature(byte[] data, string extension) => extension.ToLowerInvariant() switch
	{
		".png" => data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
		".jpg" or ".jpeg" => data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff,
		".wav" => data.Length >= 12 && System.Text.Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(data, 8, 4) == "WAVE",
		".ogg" => data.Length >= 4 && System.Text.Encoding.ASCII.GetString(data, 0, 4) == "OggS",
		".mp3" => data.Length >= 3 && (System.Text.Encoding.ASCII.GetString(data, 0, 3) == "ID3" || (data[0] == 0xff && (data[1] & 0xe0) == 0xe0)),
		_ => false,
	};
}
