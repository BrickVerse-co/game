// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using BrickVerse.Creator.Managers;
using BrickVerse.Creator.Utils;
using BrickVerse.Schemas.API;
using BrickVerse.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BrickVerse.Creator.UI.Splashes.Components;

public partial class RecentPlaceList : Control
{
	private const string CardPath = "res://scenes/creator/splash/components/recent_place_card.tscn";
	private sealed record CloudProject(CreatorPlaceItem World, string Owner, ProjectManager.RecentData Local, bool Downloaded);

	[Export] private bool _cloudOnly;
	[Export] private Control _loader = null!;
	[Export] private Control _noProjectsView = null!;
	[Export] private Label _summaryLabel = null!;
	[Export] private Button _refreshButton = null!;
	[Export] private LineEdit? _searchInput;
	[Export] private OptionButton? _ownerFilter;
	[Export] private OptionButton? _statusFilter;
	[Export] private OptionButton? _sortFilter;
	[Export] private Label? _emptyTitle;
	[Export] private Label? _emptyHint;
	private readonly List<CloudProject> _cloudProjects = [];
	private bool _loading;

	public override void _Ready()
	{
		CreatorAPI.UserAuthenticated += OnUserAuthenticated;
		CreatorAPI.AuthenticationCleared += Reload;
		_refreshButton.Pressed += Reload;
		if (_searchInput != null) _searchInput.TextChanged += _ => RenderCloudProjects();
		if (_ownerFilter != null) _ownerFilter.ItemSelected += _ => RenderCloudProjects();
		if (_statusFilter != null) _statusFilter.ItemSelected += _ => RenderCloudProjects();
		if (_sortFilter != null) _sortFilter.ItemSelected += _ => RenderCloudProjects();
		_ = LoadList();
	}

	public override void _ExitTree()
	{
		CreatorAPI.UserAuthenticated -= OnUserAuthenticated;
		CreatorAPI.AuthenticationCleared -= Reload;
	}

	private void OnUserAuthenticated(OpenIdUserInfoResponse _) => Reload();
	public void Reload() { if (!_loading) _ = LoadList(); }
	private void ClearCards() { foreach (Node item in GetChildren()) item.QueueFree(); }

	public async Task LoadList()
	{
		if (_loading) return;
		_loading = true;
		_loader.Visible = true;
		_refreshButton.Disabled = true;
		ClearCards();
		try
		{
			ProjectManager.RecentData[] recents = await ProjectManager.GetRecents();
			if (_cloudOnly) await LoadCloudProjects(recents); else RenderLocalProjects(recents);
		}
		catch (Exception ex)
		{
			BV.PrintWarn($"Could not load Creator projects: {ex.Message}");
			_summaryLabel.Text = "Projects unavailable · Select refresh to retry";
			_noProjectsView.Visible = true;
			SetEmptyCopy("Projects unavailable", "Check your connection and select Refresh to try again.");
		}
		finally
		{
			_loader.Visible = false;
			_refreshButton.Disabled = false;
			_loading = false;
		}
	}

	private void RenderLocalProjects(ProjectManager.RecentData[] recents)
	{
		foreach (ProjectManager.RecentData recent in recents) AddCard(recent);
		_noProjectsView.Visible = recents.Length == 0;
		_summaryLabel.Text = $"{recents.Length} local project{(recents.Length == 1 ? "" : "s")}";
		SetEmptyCopy("No recent projects", "Create a world or open a local project and it will appear here.");
	}

	private async Task LoadCloudProjects(ProjectManager.RecentData[] recents)
	{
		_cloudProjects.Clear();
		if (!CreatorAPI.IsUserAuthenticated)
		{
			ConfigureOwnerFilter([]);
			_summaryLabel.Text = "Sign in to browse cloud projects";
			_noProjectsView.Visible = true;
			SetEmptyCopy("Sign in to BrickVerse Cloud", "Sign in above to access worlds you own and guild worlds you can edit.");
			return;
		}
		Dictionary<long, ProjectManager.RecentData> localWorlds = recents.Where(x => x.WorldId is > 0)
			.GroupBy(x => x.WorldId!.Value).ToDictionary(x => x.Key, x => x.First());
		foreach ((CreatorPlaceItem world, string owner) in await FetchCloudWorlds())
		{
			long id = world.WorldId ?? world.Id;
			if (id <= 0) continue;
			localWorlds.TryGetValue(id, out ProjectManager.RecentData local);
			_cloudProjects.Add(new(world, owner, local, !string.IsNullOrWhiteSpace(local.FolderPath)));
		}
		ConfigureOwnerFilter(_cloudProjects.Select(x => x.Owner));
		RenderCloudProjects();
	}

	private void RenderCloudProjects()
	{
		if (!_cloudOnly) return;
		ClearCards();
		IEnumerable<CloudProject> filtered = _cloudProjects;
		string query = _searchInput?.Text.Trim() ?? "";
		if (query.Length > 0) filtered = filtered.Where(x => (x.World.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
			|| (x.World.Description ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
			|| x.Owner.Contains(query, StringComparison.OrdinalIgnoreCase));
		if (_ownerFilter is { Selected: > 0 })
		{
			string owner = _ownerFilter.GetItemText(_ownerFilter.Selected);
			filtered = filtered.Where(x => x.Owner == owner);
		}
		if (_statusFilter?.Selected == 1) filtered = filtered.Where(x => x.Downloaded);
		if (_statusFilter?.Selected == 2) filtered = filtered.Where(x => !x.Downloaded);
		filtered = _sortFilter?.Selected switch
		{
			1 => filtered.OrderBy(x => x.World.Name, StringComparer.OrdinalIgnoreCase),
			2 => filtered.OrderByDescending(x => x.World.CreatedAt),
			_ => filtered.OrderByDescending(x => x.World.UpdatedAt ?? x.World.CreatedAt),
		};
		CloudProject[] visible = filtered.ToArray();
		foreach (CloudProject project in visible) AddCard(project.Local, project.World, project.Owner, project.Downloaded);
		_noProjectsView.Visible = visible.Length == 0;
		int downloaded = _cloudProjects.Count(x => x.Downloaded);
		_summaryLabel.Text = visible.Length == _cloudProjects.Count
			? $"{_cloudProjects.Count} cloud project{(_cloudProjects.Count == 1 ? "" : "s")} · {downloaded} downloaded"
			: $"Showing {visible.Length} of {_cloudProjects.Count}";
		SetEmptyCopy(_cloudProjects.Count == 0 ? "No cloud projects" : "No matching projects",
			_cloudProjects.Count == 0 ? "Published and unpublished editable worlds will appear here." : "Try changing your search or filters.");
	}

	private void ConfigureOwnerFilter(IEnumerable<string> owners)
	{
		if (_ownerFilter == null) return;
		string selected = _ownerFilter.Selected >= 0 ? _ownerFilter.GetItemText(_ownerFilter.Selected) : "All owners";
		_ownerFilter.Clear();
		_ownerFilter.AddItem("All owners");
		foreach (string owner in owners.Distinct().OrderBy(x => x == "You" ? "" : x)) _ownerFilter.AddItem(owner);
		for (int i = 0; i < _ownerFilter.ItemCount; i++) if (_ownerFilter.GetItemText(i) == selected) _ownerFilter.Select(i);
	}

	private void SetEmptyCopy(string title, string hint)
	{
		if (_emptyTitle != null) _emptyTitle.Text = title;
		if (_emptyHint != null) _emptyHint.Text = hint;
	}

	private void AddCard(ProjectManager.RecentData recent, CreatorPlaceItem? cloud = null, string owner = "", bool downloaded = false)
	{
		RecentPlaceCard card = Globals.CreateInstanceFromScene<RecentPlaceCard>(CardPath);
		card.Data = recent; card.CloudWorld = cloud; card.CloudOwner = owner; card.IsDownloaded = downloaded; card.ListUI = this;
		AddChild(card);
	}

	private static async Task<List<(CreatorPlaceItem World, string Owner)>> FetchCloudWorlds()
	{
		List<(CreatorPlaceItem, string)> worlds = [];
		worlds.AddRange((await CreatorAPI.GetUserWorlds(CreatorAPI.UserID)).Select(x => (x, "You")));
		CreatorGuildItem[] guilds = await CreatorAPI.GetUserGuilds(limitToEditable: true);
		CreatorPlaceItem[][] guildWorlds = await Task.WhenAll(guilds.Select(x => CreatorAPI.GetGuildWorlds(x.Id)));
		for (int i = 0; i < guilds.Length; i++) worlds.AddRange(guildWorlds[i].Select(x => (x, guilds[i].Name)));
		return worlds.GroupBy(x => x.Item1.WorldId ?? x.Item1.Id).Select(x => x.First()).ToList();
	}
}
