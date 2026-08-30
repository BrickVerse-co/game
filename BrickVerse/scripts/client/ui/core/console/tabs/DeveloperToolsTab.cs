// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Datamodel;
using BrickVerse.Scripting;
using BrickVerse.Shared.AssetLoaders;
using Godot;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DatamodelScript = BrickVerse.Datamodel.Script;

namespace BrickVerse.Client.UI;

public sealed partial class DeveloperToolsTab : MarginContainer
{
	public enum ToolMode { Performance, Scripts, Memory, Network, Executor }
	public ToolMode Mode { get; init; }
	private RichTextLabel _output = null!;
	private CodeEdit? _editor;
	private Button? _runButton;
	private double _refreshRemaining;
	private ClientScript? _executorScript;

	public override void _Ready()
	{
		AddThemeConstantOverride("margin_left", 16);
		AddThemeConstantOverride("margin_top", 14);
		AddThemeConstantOverride("margin_right", 16);
		AddThemeConstantOverride("margin_bottom", 16);
		VBoxContainer layout = new();
		layout.AddThemeConstantOverride("separation", 10);
		AddChild(layout);

		HBoxContainer toolbar = new();
		layout.AddChild(toolbar);
		VBoxContainer heading = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		Label title = new() { Text = Mode.ToString() };
		title.AddThemeFontSizeOverride("font_size", 20);
		Label subtitle = new() { Text = ModeDescription(), Modulate = new Color(0.68f, 0.72f, 0.76f) };
		heading.AddChild(title);
		heading.AddChild(subtitle);
		toolbar.AddChild(heading);
		Button refresh = new() { Text = "Refresh", CustomMinimumSize = new Vector2(92, 34) };
		refresh.Pressed += Refresh;
		toolbar.AddChild(refresh);

		if (Mode == ToolMode.Scripts)
		{
			Button reset = new() { Text = "Reset Profiler" };
			reset.Pressed += () => { LuauProfiler.Reset(); Refresh(); };
			toolbar.AddChild(reset);
		}

		if (Mode == ToolMode.Executor)
		{
			_editor = new CodeEdit
			{
				PlaceholderText = "-- Execute client Luau (local tests and creator/admin sessions only)",
				CustomMinimumSize = new Vector2(0, 180),
				SizeFlagsVertical = SizeFlags.ExpandFill,
				GuttersDrawLineNumbers = true,
			};
			layout.AddChild(_editor);
			_runButton = new Button { Text = "Run", CustomMinimumSize = new Vector2(90, 32) };
			_runButton.Pressed += async () => await ExecuteAsync();
			layout.AddChild(_runButton);
		}

		_output = new RichTextLabel
		{
			BbcodeEnabled = true,
			SelectionEnabled = true,
			FitContent = false,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			ScrollActive = true,
		};
		_output.AddThemeStyleboxOverride("normal", new StyleBoxFlat
		{
			BgColor = new Color(0.035f, 0.04f, 0.05f, 0.92f),
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
			ContentMarginLeft = 12,
			ContentMarginTop = 10,
			ContentMarginRight = 12,
			ContentMarginBottom = 10,
		});
		layout.AddChild(_output);
		Refresh();
	}

	private string ModeDescription() => Mode switch
	{
		ToolMode.Performance => "Live rendering and engine timing",
		ToolMode.Scripts => "Luau execution and scheduler profiling",
		ToolMode.Memory => "Runtime allocations and asset cache usage",
		ToolMode.Network => "Connection and session diagnostics",
		_ => "Run client-side Luau for development and testing",
	};

	public override void _Process(double delta)
	{
		if (!IsVisibleInTree() || Mode == ToolMode.Executor) return;
		_refreshRemaining -= delta;
		if (_refreshRemaining <= 0) Refresh();
	}

	private void Refresh()
	{
		_refreshRemaining = 1;
		if (_output == null) return;
		World root = CoreUIRoot.Singleton.Root;
		_output.Text = Mode switch
		{
			ToolMode.Performance => PerformanceText(root),
			ToolMode.Scripts => ScriptsText(root),
			ToolMode.Memory => MemoryText(root),
			ToolMode.Network => NetworkText(root),
			_ => ExecutorHelp(root),
		};
	}

	private static string PerformanceText(World root) =>
		$"[table=2][cell]Metric[/cell][cell]Value[/cell]" +
		$"[cell]FPS[/cell][cell]{Engine.GetFramesPerSecond():0}[/cell]" +
		$"[cell]Frame process[/cell][cell]{Performance.Singleton.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.00} ms[/cell]" +
		$"[cell]Physics process[/cell][cell]{Performance.Singleton.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:0.00} ms[/cell]" +
		$"[cell]Instances[/cell][cell]{root.InstanceCount:N0}[/cell]" +
		$"[cell]Nodes[/cell][cell]{Performance.Singleton.GetMonitor(Performance.Monitor.ObjectNodeCount):N0}[/cell]" +
		$"[cell]Orphan nodes[/cell][cell]{Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount):N0}[/cell]" +
		$"[cell]Resources[/cell][cell]{Performance.Singleton.GetMonitor(Performance.Monitor.ObjectResourceCount):N0}[/cell][/table]";

	private static string ScriptsText(World root)
	{
		var samples = LuauProfiler.Snapshot();
		StringBuilder text = new("[table=6][cell]Script[/cell][cell]Calls[/cell][cell]Total ms[/cell][cell]Avg ms[/cell][cell]Max ms[/cell][cell]Last ms[/cell]");
		foreach (var sample in samples)
			text.Append($"[cell]{Escape(sample.Path)}[/cell][cell]{sample.Calls:N0}[/cell][cell]{sample.TotalMilliseconds:0.000}[/cell][cell]{sample.AverageMilliseconds:0.000}[/cell][cell]{sample.MaximumMilliseconds:0.000}[/cell][cell]{sample.LastMilliseconds:0.000}[/cell]");
		text.Append("[/table]\n").Append(samples.Length).Append(" profiled scripts; ")
			.Append(root.GetDescendants().OfType<DatamodelScript>().Count()).Append(" scripts in DataModel.\n")
			.Append("Parallel Luau: ").Append(ParallelLuauScheduler.DomainCount).Append(" Actor domains, ")
			.Append(ParallelLuauScheduler.WorkerLimit).Append(" workers, ")
			.Append(ParallelLuauScheduler.CompletedCount).Append('/').Append(ParallelLuauScheduler.ScheduledCount)
			.Append(" work items completed.");
		return text.ToString();
	}

	private static string MemoryText(World root)
	{
		long managed = GC.GetTotalMemory(false);
		return $"[table=2][cell]Category[/cell][cell]Usage[/cell]" +
			$"[cell].NET managed heap[/cell][cell]{Bytes(managed)}[/cell]" +
			$"[cell]Asset cache[/cell][cell]{Bytes(AssetLoader.Singleton.AssetSizeBytes)}[/cell]" +
			$"[cell]Cached assets[/cell][cell]{AssetLoader.Singleton.AssetCacheCount:N0}[/cell]" +
			$"[cell]Pending assets[/cell][cell]{AssetLoader.Singleton.PendingAssetsCount:N0}[/cell]" +
			$"[cell]Godot resources[/cell][cell]{Performance.Singleton.GetMonitor(Performance.Monitor.ObjectResourceCount):N0}[/cell]" +
			$"[cell]DataModel instances[/cell][cell]{root.InstanceCount:N0}[/cell][/table]";
	}

	private static string NetworkText(World root)
	{
		var player = root.Players.LocalPlayer;
		return $"[table=2][cell]Metric[/cell][cell]Value[/cell]" +
			$"[cell]Ping[/cell][cell]{player?.NetworkPing ?? 0} ms[/cell]" +
			$"[cell]Session[/cell][cell]{root.SessionType}[/cell]" +
			$"[cell]World ID[/cell][cell]{root.WorldID}[/cell]" +
			$"[cell]Universe ID[/cell][cell]{root.UniverseID}[/cell]" +
			$"[cell]Server ID[/cell][cell]{Escape(root.ServerID)}[/cell]" +
			$"[cell]Server clock[/cell][cell]{root.ServerTime:0.000}[/cell]" +
			$"[cell]Players[/cell][cell]{root.Players.PlayersCount}[/cell][/table]";
	}

	private static string ExecutorHelp(World root) => CanExecute(root)
		? "Run client-side Luau in this session. Output appears in the Console tab. Scheduled work is stopped after 10 seconds."
		: "[color=#ffbc58]Executor disabled: available only in local tests or to the place creator/admin.[/color]";

	private async Task ExecuteAsync()
	{
		World root = CoreUIRoot.Singleton.Root;
		if (_editor == null || _runButton == null || !CanExecute(root)) { Refresh(); return; }
		string source = _editor.Text.Trim();
		if (source.Length == 0) return;
		_runButton.Disabled = true;
		try
		{
			_executorScript?.Destroy();
			_executorScript = root.New<ClientScript>(root.Environment);
			_executorScript.Name = "DeveloperConsoleExecutor";
			_executorScript.Source = source;
			_executorScript.Run();
			_output.Text = "[color=#73daca]Executed. See Console for output.[/color]";
			await ToSignal(GetTree().CreateTimer(10), SceneTreeTimer.SignalName.Timeout);
			_executorScript?.Destroy();
			_executorScript = null;
		}
		catch (Exception exception) { _output.Text = $"[color=#f95d5d]{Escape(exception.Message)}[/color]"; }
		finally { if (IsInstanceValid(_runButton)) _runButton.Disabled = false; }
	}

	public override void _ExitTree()
	{
		_executorScript?.Destroy();
		base._ExitTree();
	}

	private static bool CanExecute(World root) => root.IsLocalTest || root.Players.LocalPlayer is { IsCreator: true } or { IsAdmin: true };
	private static string Bytes(long value) => value >= 1L << 30 ? $"{value / (double)(1L << 30):0.00} GiB" : value >= 1L << 20 ? $"{value / (double)(1L << 20):0.00} MiB" : $"{value / 1024.0:0.00} KiB";
	private static string Escape(string value) => value.Replace("[", "[lb]");
}
