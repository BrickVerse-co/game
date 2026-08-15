using Godot;
using BrickVerse.Shared;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using System;

namespace BrickVerse.Creator.UI.Popups;

public sealed partial class BugReportPopup : Window
{
	private const string NewIssueUrl = "https://github.com/BrickVerse-co/Bug-Tracker/issues/new";
	[Export] private LineEdit _title = null!;
	[Export] private OptionButton _area = null!;
	[Export] private TextEdit _description = null!;
	[Export] private TextEdit _steps = null!;
	[Export] private LineEdit _expected = null!;
	[Export] private LineEdit _actual = null!;
	[Export] private CheckBox _includeSystem = null!;
	[Export] private Label _validation = null!;
	[Export] private Button _cancel = null!;
	[Export] private Button _continue = null!;

	public override void _Ready()
	{
		foreach (string area in new[] { "Creator UI", "World editing", "Code editor", "Play testing", "Publishing/assets", "Team Create", "Terrain", "Animation", "Other" }) _area.AddItem(area);
		_cancel.Pressed += QueueFree;
		_continue.Pressed += OpenGitHubDraft;
		CloseRequested += QueueFree;
	}

	public static void Open()
	{
		BugReportPopup popup = GD.Load<PackedScene>("res://scenes/creator/popups/bug_report.tscn").Instantiate<BugReportPopup>();
		CreatorService.Interface.PopupWindow(popup);
	}

	private void OpenGitHubDraft()
	{
		string title = _title.Text.Trim();
		if (title.Length < 5 || string.IsNullOrWhiteSpace(_description.Text) || string.IsNullOrWhiteSpace(_steps.Text))
		{
			_validation.Text = "Add a clear title, description, and reproduction steps before continuing.";
			_validation.Visible = true;
			return;
		}

		string body = $"""
## Description
{_description.Text.Trim()}

## Steps to reproduce
{_steps.Text.Trim()}

## Expected behavior
{ValueOrPlaceholder(_expected.Text)}

## Actual behavior
{ValueOrPlaceholder(_actual.Text)}

## Area
{_area.GetItemText(_area.Selected)}
""";
		if (_includeSystem.ButtonPressed) body += "\n\n## Creator environment\n" + BuildEnvironment();
		body += "\n\n---\nSubmitted from the BrickVerse Creator bug report form.";
		string url = $"{NewIssueUrl}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}&labels=bug%2Ccreator";
		OS.ShellOpen(url);
		QueueFree();
	}

	private static string ValueOrPlaceholder(string value) => string.IsNullOrWhiteSpace(value) ? "Not provided." : value.Trim();

	private static string BuildEnvironment()
	{
		string world = World.Current?.Name ?? "No world open";
		return $"- Creator: {Globals.AppVersion}\n- Build: {ResolveBuildNumber()}"
			+ $"\n- Channel: {ResolveBuildChannel()}"
			+ $"\n- OS: {OS.GetName()} {OS.GetVersion()}\n- CPU: {OS.GetProcessorName()}\n- Renderer: {RenderingServer.GetVideoAdapterName()}\n- World: {world}";
	}

	private static string ResolveBuildNumber()
	{
		string configuredBuild = ProjectSettings.GetSetting("brickverse/build/version", "").AsString().Trim();
		if (!string.IsNullOrWhiteSpace(configuredBuild)) return configuredBuild;

		return string.IsNullOrWhiteSpace(Globals.ShortBuildCommit)
			? $"local-{Globals.AppVersion}"
			: $"local-{Globals.ShortBuildCommit}";
	}

	private static string ResolveBuildChannel()
	{
		string configuredChannel = ProjectSettings.GetSetting("brickverse/build/channel", "").AsString().Trim().ToLowerInvariant();
		if (!string.IsNullOrWhiteSpace(configuredChannel)) return configuredChannel;
		if (OS.IsDebugBuild()) return "debug";
		return Globals.IsBetaBuild ? "beta" : "prod";
	}
}
