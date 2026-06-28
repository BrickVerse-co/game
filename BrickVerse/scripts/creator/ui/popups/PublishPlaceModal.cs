// (c) 2026 Meta Games LLC. All Rights Reserved.

using Godot;
using System.Collections.Generic;
using System;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using BrickVerse.Creator.UI;
using BrickVerse.Creator.Settings;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Formats;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BrickVerse.Creator.UI.Popups;

public partial class PublishPlaceModal : PopupWindowBase
{
	public enum PublishOwnerType
	{
		User,
		Guild
	}

	public sealed class PublishGuildOption
	{
		public string Id { get; init; } = "";
		public string Name { get; init; } = "";
	}

	public sealed class PublishPlaceRequest
	{
		public int? WorldId { get; init; }
		public int? UniverseId { get; init; }
		public string WorldName { get; init; } = "";
		public string UniverseName { get; init; } = "";
		public string UniverseDescription { get; init; } = "";
		public PublishOwnerType OwnerType { get; init; }
		public string? GuildId { get; init; }
	}

	public event Action<PublishPlaceRequest>? PublishRequested;
	public event Action? Closed;

	[Export] private Button _closeButton = null!;
	[Export] private LineEdit _universeNameInput = null!;
	[Export] private LineEdit _placeNameInput = null!;
	[Export] private TextEdit _descriptionInput = null!;
	[Export] private Button _ownerOption = null!;
	[Export] private Button _guildOption = null!;
	[Export] private OptionButton _guildDropdown = null!;
	[Export] private Button _publishButton = null!;
	[Export] private Button _cancelButton = null!;
	[Export] private Label _errorLabel = null!;

	private World? world = null;

	private readonly List<PublishGuildOption> _guilds = [];
	private bool _isBusy;

	public override void _Ready()
	{
		_closeButton.Pressed += Close;
		_cancelButton.Pressed += Close;
		_publishButton.Pressed += Submit;

		_ownerOption.Pressed += () => SetOwnerType(PublishOwnerType.User);
		_guildOption.Pressed += () => SetOwnerType(PublishOwnerType.Guild);

		SetOwnerType(PublishOwnerType.User);
		SetBusy(false);
		HideError();

		PublishRequested += async (request) =>
		{
			if (this.world == null)
			{
				ShowPublishError("Failed to publish: world data is missing.");
				return;
			}

			string projectPath = this.world.LinkedSession.ProjectFolderPath;

			if (!Directory.Exists(projectPath))
			{
				ShowPublishError("Failed to publish: project folder does not exist.");
				return;
			}

			var loadOverlay = CreatorService.Interface.LoadOverlay;

			try
			{
				var metadata = PackedFormat.ReadProjectMetadata(File.ReadAllText(projectPath.PathJoin(Globals.ProjectMetaFileName)));
				var packed = await PackedFormat.PackProject(projectPath, loadOverlay.CreateProgressReporter("Publishing world"));

				loadOverlay?.SetStatus("Uploading now...");
				CreatorPublishResponse publishRes = await CreatorAPI.UploadWorld(packed, request.UniverseId, request.WorldId);

				if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Creator.OpenWebAfterPublish))
					OS.ShellOpen(publishRes.Link);
				CreatorService.Interface.StatusBar?.SetStatus("World published");
				loadOverlay?.Hide();
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
				CreatorService.Interface.PopupAlert(ex.Message);
				ShowPublishError("Failed to publish: " + ex.Message);
				loadOverlay?.Hide();
			}
		};
	}

	public async void Open(World world, bool publishAs = false)
	{
		// Load default world info
		_placeNameInput.Text = world.WorldName ?? "";
		_universeNameInput.Text = world.UniverseName ?? "";
		_descriptionInput.Text = world.UniverseDescription ?? "";
		this.world = world;

		// Fetch authenticated user's guilds and set the dropdown
		CreatorGuildItem[] creatorGuildItems = await CreatorAPI.GetUserGuilds(limitToEditable: true);
		SetGuilds(
			creatorGuildItems
				.Select(g => new PublishGuildOption
				{
					Id = g.Id,
					Name = g.Name
				})
		);

		// Reset UI
		HideError();
		SetBusy(false);
		SetOwnerType(PublishOwnerType.User);

		Show();
		_placeNameInput.GrabFocus();
	}

	public void Close()
	{
		if (_isBusy)
			return;

		Hide();
		Closed?.Invoke();
		QueueFree();
	}

	public void SetGuilds(IEnumerable<PublishGuildOption> guilds)
	{
		_guilds.Clear();
		_guildDropdown.Clear();

		foreach (PublishGuildOption guild in guilds)
		{
			if (string.IsNullOrWhiteSpace(guild.Id))
				continue;

			_guilds.Add(guild);
			_guildDropdown.AddItem(guild.Name);
		}

		if (_guilds.Count > 0)
			_guildDropdown.Select(0);

		RefreshGuildState();
	}

	public void SetBusy(bool busy)
	{
		_isBusy = busy;
		_publishButton.Disabled = busy;
		_cancelButton.Disabled = busy;
		_closeButton.Disabled = busy;

		_placeNameInput.Editable = !busy;
		_universeNameInput.Editable = !busy;
		_descriptionInput.Editable = !busy;
		_ownerOption.Disabled = busy;
		_guildOption.Disabled = busy;

		RefreshGuildState();
	}

	public void ShowError(string message)
	{
		_errorLabel.Text = message;
		_errorLabel.Visible = true;
	}

	public void ShowPublishError(string message)
	{
		SetBusy(false);
		ShowError(message);
	}

	public void HideError()
	{
		_errorLabel.Text = "";
		_errorLabel.Visible = false;
	}

	private void SetOwnerType(PublishOwnerType ownerType)
	{
		bool useGuild = ownerType == PublishOwnerType.Guild;

		_ownerOption.ButtonPressed = !useGuild;
		_guildOption.ButtonPressed = useGuild;

		RefreshGuildState();
	}

	private void RefreshGuildState()
	{
		bool useGuild = _guildOption.ButtonPressed;

		_guildDropdown.Visible = useGuild;
		_guildDropdown.Disabled = _isBusy || _guilds.Count == 0;

		if (useGuild && _guilds.Count == 0)
		{
			ShowError("You do not have publish permission in any guilds.");
			return;
		}

		if (_errorLabel.Text == "You do not have publish permission in any guilds.")
			HideError();
	}

	private void Submit()
	{
		if (_isBusy)
			return;

		HideError();

		string worldName = _placeNameInput.Text.Trim();
		string universeName = _universeNameInput.Text.Trim();
		string universeDescription = _descriptionInput.Text.Trim();

		if (string.IsNullOrWhiteSpace(worldName))
		{
			ShowError("World name is required.");
			_placeNameInput.GrabFocus();
			return;
		}

		if (string.IsNullOrWhiteSpace(universeName))
		{
			ShowError("Universe name is required.");
			_universeNameInput.GrabFocus();
			return;
		}

		if (worldName.Length > 64)
		{
			ShowError("World name must be 64 characters or less.");
			_placeNameInput.GrabFocus();
			return;
		}

		if (universeName.Length > 64)
		{
			ShowError("Universe name must be 64 characters or less.");
			_universeNameInput.GrabFocus();
			return;
		}

		bool publishToGuild = _guildOption.ButtonPressed;
		string? guildId = null;

		if (publishToGuild)
		{
			int selected = _guildDropdown.Selected;

			if (_guilds.Count == 0 || selected < 0 || selected >= _guilds.Count)
			{
				ShowError("Select a valid guild.");
				return;
			}

			guildId = _guilds[selected].Id;
		}

		var request = new PublishPlaceRequest
		{
			WorldName = worldName,
			UniverseName = universeName,
			UniverseDescription = universeDescription,
			OwnerType = publishToGuild ? PublishOwnerType.Guild : PublishOwnerType.User,
			GuildId = guildId,
			UniverseId = this.world?.UniverseID ?? 0,
			WorldId = this.world?.WorldID ?? 0
		};

		PublishRequested?.Invoke(request);
	}
}