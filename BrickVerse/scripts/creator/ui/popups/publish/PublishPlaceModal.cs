// (c) 2026 Meta Games LLC. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.Utils;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Formats;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using Godot;

namespace BrickVerse.Creator.UI.Popups;

public partial class PublishPlaceModal : PopupWindowBase
{
	public enum PublishOwnerType
	{
		User,
		Guild,
	}

	public sealed class PublishGuildOption
	{
		public string Id { get; init; } = "";
		public string Name { get; init; } = "";
	}

	public sealed class PublishPlaceRequest
	{
		public long? WorldId { get; init; }
		public long? UniverseId { get; init; }
		public string WorldName { get; init; } = "";
		public string UniverseName { get; init; } = "";
		public string UniverseDescription { get; init; } = "";
		public PublishOwnerType OwnerType { get; init; }
		public string OwnerId { get; init; } = "";
		public string? GuildId { get; init; }
	}

	public event Action<PublishPlaceRequest>? PublishRequested;
	public event Action? Closed;

	[Export]
	private Button _closeButton = null!;

	[Export]
	private LineEdit _universeNameInput = null!;

	[Export]
	private LineEdit _placeNameInput = null!;

	[Export]
	private TextEdit _descriptionInput = null!;

	[Export]
	private Button _ownerOption = null!;

	[Export]
	private Button _guildOption = null!;

	[Export]
	private OptionButton _guildDropdown = null!;

	[Export]
	private Button _publishButton = null!;

	[Export]
	private Button _cancelButton = null!;

	[Export]
	private Label _errorLabel = null!;

	[Export]
	private Label? _loaderLabel;

	private World? world;
	private readonly List<PublishGuildOption> _guilds = [];
	private bool _isBusy;

	public override void _Ready()
	{
		base._Ready();
		ResolveNodeReferences();

		CloseRequested += Close;

		_closeButton.Pressed += Close;
		_cancelButton.Pressed += Close;
		_publishButton.Pressed += Submit;

		_ownerOption.Pressed += () => SetOwnerType(PublishOwnerType.User);
		_guildOption.Pressed += () => SetOwnerType(PublishOwnerType.Guild);

		PublishRequested += async request =>
		{
			if (world == null)
			{
				ShowPublishError("Failed to publish: world data is missing.");
				return;
			}

			string projectPath = world.LinkedSession.ProjectFolderPath;

			if (!Directory.Exists(projectPath))
			{
				ShowPublishError("Failed to publish: project folder does not exist.");
				return;
			}

			var loadOverlay = CreatorService.Interface.LoadOverlay;

			try
			{
				PackedFormat.ReadProjectMetadata(
					File.ReadAllText(projectPath.PathJoin(Globals.ProjectMetaFileName))
				);
				var packed = await PackedFormat.PackProject(
					projectPath,
					loadOverlay.CreateProgressReporter("Publishing world")
				);

				loadOverlay?.SetStatus("Uploading now...");
				CreatorPublishResponse publishRes = await CreatorAPI.UploadWorld(
					packed,
					request.UniverseId,
					request.WorldId,
					true,
					request.OwnerId,
					request.OwnerType == PublishOwnerType.Guild ? "guild" : "user"
				);

				if (
					CreatorSettingsService.Instance.Get<bool>(
						CreatorSettingKeys.Creator.OpenWebAfterPublish
					)
				)
					OS.ShellOpen(publishRes.Link);

				this.world.UniverseID = publishRes.UniverseId;
				this.world.WorldID = publishRes.WorldId;

				CreatorService.Interface.StatusBar?.SetStatus("World published");
				loadOverlay?.Hide();
				SetBusy(false);
				Close();
				CreatorService.Interface.PopupAlert(
					"World published successfully! You can now share it with others using the link: "
						+ publishRes.Link
				);
			}
			catch (Exception ex)
			{
				BV.PrintErr($"Failed to publish world: {ex.Message}");
				CreatorService.Interface.PopupAlert(ex.Message);
				ShowPublishError("Failed to publish: " + ex.Message);
				loadOverlay?.Hide();
			}
		};
	}

	public async void Open(World world)
	{
		if (world == null || world.WorldID == 0 || world.UniverseID == 0)
		{
			CreatorService.Interface.PopupAlert(
				"This experience hasn't been published yet.\nTo publish it, first use Publish As to create a new experience or overwrite an existing one. Once it has been published, you'll be able to use Publish to save future changes."
			);
			Close();
			return;
		}

		this.world = world;

		_placeNameInput.Text = world.WorldName ?? "A cool planet";
		_universeNameInput.Text = world.UniverseName ?? "The Universe";
		_descriptionInput.Text = world.UniverseDescription ?? "A description of the universe.";

		CreatorGuildItem[] creatorGuildItems = await CreatorAPI.GetUserGuilds(
			limitToEditable: true
		);
		SetGuilds(creatorGuildItems);

		HideError();
		SetBusy(false);
		SetOwnerType(PublishOwnerType.User);

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

	public void SetGuilds(IEnumerable<CreatorGuildItem> guilds)
	{
		_guilds.Clear();
		_guildDropdown.Clear();

		foreach (CreatorGuildItem guild in guilds)
		{
			if (string.IsNullOrWhiteSpace(guild.Id))
				continue;

			_guilds.Add(new PublishGuildOption { Id = guild.Id, Name = guild.Name });
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

		if (_loaderLabel != null)
			_loaderLabel.Visible = busy;

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

		SetBusy(true);

		PublishRequested?.Invoke(
			new PublishPlaceRequest
			{
				WorldName = worldName,
				UniverseName = universeName,
				UniverseDescription = universeDescription,
				OwnerType = publishToGuild ? PublishOwnerType.Guild : PublishOwnerType.User,
				OwnerId = publishToGuild ? _guilds[_guildDropdown.Selected].Id : CreatorAPI.UserID,
				GuildId = guildId,
				UniverseId = world?.UniverseID ?? 0,
				WorldId = world?.WorldID ?? 0,
			}
		);
	}

	private void ResolveNodeReferences()
	{
		_closeButton ??= GetNode<Button>("Modal/Header/Close");
		_universeNameInput ??= GetNode<LineEdit>("Modal/Body/UniverseName");
		_placeNameInput ??= GetNode<LineEdit>("Modal/Body/PlaceName");
		_descriptionInput ??= GetNode<TextEdit>("Modal/Body/Description");
		_ownerOption ??= GetNode<Button>("Modal/Body/Ownership/Personal");
		_guildOption ??= GetNode<Button>("Modal/Body/Ownership/Guild");
		_guildDropdown ??= GetNode<OptionButton>("Modal/Body/GuildDropdown");
		_publishButton ??= GetNode<Button>("Modal/Footer/Publish");
		_cancelButton ??= GetNode<Button>("Modal/Footer/Cancel");
		_errorLabel ??= GetNode<Label>("Modal/Body/ErrorLabel");
		_loaderLabel ??= GetNodeOrNull<Label>("Modal/Body/Loader");
	}
}
