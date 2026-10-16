// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BrickVerse.Shared;

public enum CrashApplication
{
	Client,
	Creator,
}

public sealed class CrashReport
{
	public string ReportId { get; set; } = Guid.NewGuid().ToString("N");
	public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
	public string Application { get; set; } = "Client";
	public string Context { get; set; } = "Unknown";
	public bool Fatal { get; set; }
	public string AppVersion { get; set; } = "Unknown";
	public string BuildCommit { get; set; } = "Unknown";
	public string OperatingSystem { get; set; } = "Unknown";
	public string Processor { get; set; } = "Unknown";
	public int ProcessorCount { get; set; }
	public long ManagedMemoryBytes { get; set; }
	public string Renderer { get; set; } = "Unknown";
	public string CommandLine { get; set; } = "";
	public string ExceptionType { get; set; } = "Unknown";
	public string ExceptionMessage { get; set; } = "Unknown error";
	public string Exception { get; set; } = "";
	public string RecentLogs { get; set; } = "";
	public string UserDescription { get; set; } = "";
}

public static partial class CrashReporter
{
	private const string ReportsFolder = "user://crash-reports";
	private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
	};
	private static readonly object InitLock = new();
	private static WeakReference<Node>? _host;
	private static CrashApplication _application;
	private static bool _initialized;
	private static int _handling;

	public static void Initialize(Node host, CrashApplication application)
	{
		lock (InitLock)
		{
			_host = new WeakReference<Node>(host);
			_application = application;
			if (_initialized) return;
			_initialized = true;
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		}
		_ = UploadPendingReportsAsync();
	}

	public static CrashReport Capture(Exception exception, string context, bool fatal, bool showDialog = true)
	{
		CrashReport report = CreateReport(exception, context, fatal);
		Save(report, pending: true);
		BV.PrintErr($"Crash report {report.ReportId} saved for {context}: ", exception);
		if (showDialog) Present(report);
		return report;
	}

	private static CrashReport CreateReport(Exception exception, string context, bool fatal)
	{
		string renderer = Safe(() => RenderingServer.GetVideoAdapterName(), "Unavailable");
		return new CrashReport
		{
			Application = _application.ToString(),
			Context = context,
			Fatal = fatal,
			AppVersion = Globals.AppVersion,
			BuildCommit = string.IsNullOrWhiteSpace(Globals.ShortBuildCommit) ? "local" : Globals.ShortBuildCommit,
			OperatingSystem = Safe(() => $"{OS.GetName()} {OS.GetVersion()}", System.Environment.OSVersion.ToString()),
			Processor = Safe(OS.GetProcessorName, "Unknown"),
			ProcessorCount = System.Environment.ProcessorCount,
			ManagedMemoryBytes = GC.GetTotalMemory(false),
			Renderer = renderer,
			CommandLine = RedactCommandLine(OS.GetCmdlineArgs()),
			ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
			ExceptionMessage = exception.Message,
			Exception = exception.ToString(),
			RecentLogs = BV.GetRecentLogs(),
		};
	}

	private static string RedactCommandLine(string[] args)
	{
		return string.Join(" ", args.Select(arg =>
			Regex.Replace(arg, @"(?i)(--?(?:token|ctoken|auth|password|secret)(?:=|\s+)).+", "$1<redacted>")));
	}

	private static string ReportPath(CrashReport report, bool pending) => Path.Combine(
		ProjectSettings.GlobalizePath(ReportsFolder),
		$"{report.TimestampUtc:yyyyMMdd-HHmmss}-{report.ReportId}{(pending ? ".pending" : ".sent")}.json"
	);

	private static void Save(CrashReport report, bool pending)
	{
		try
		{
			string folder = ProjectSettings.GlobalizePath(ReportsFolder);
			Directory.CreateDirectory(folder);
			File.WriteAllText(ReportPath(report, pending), JsonSerializer.Serialize(report, JsonOptions));
		}
		catch (Exception error)
		{
			GD.PrintErr($"Could not save crash report {report.ReportId}: {error}");
		}
	}

	private static void Present(CrashReport report)
	{
		BV.CallOnMainThread(() =>
		{
			if (_host == null || !_host.TryGetTarget(out Node? host) || !GodotObject.IsInstanceValid(host))
			{
				OS.Alert($"BrickVerse encountered an error.\n\nReport ID: {report.ReportId}\nA local report was saved.", "BrickVerse Crash Reporter");
				return;
			}
			CrashReporterDialog dialog = new(report);
			host.GetTree().Root.AddChild(dialog);
			dialog.PopupCentered();
		});
	}

	public static async Task<bool> SubmitAsync(CrashReport report)
	{
		try
		{
			string json = JsonSerializer.Serialize(report, JsonOptions);
			using StringContent content = new(json, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await Http.PostAsync(
				Globals.ApiEndpoint.PathJoin("/v3/diagnostics/crash-report"), content);
			if (!response.IsSuccessStatusCode) return false;

			string pendingPath = ReportPath(report, pending: true);
			string sentPath = ReportPath(report, pending: false);
			if (File.Exists(pendingPath)) File.Move(pendingPath, sentPath, true);
			else Save(report, pending: false);
			return true;
		}
		catch { return false; }
	}

	internal static void UpdatePending(CrashReport report) => Save(report, pending: true);

	private static async Task UploadPendingReportsAsync()
	{
		try
		{
			string folder = ProjectSettings.GlobalizePath(ReportsFolder);
			if (!Directory.Exists(folder)) return;
			foreach (string path in Directory.EnumerateFiles(folder, "*.pending.json").Take(10))
			{
				CrashReport? report = JsonSerializer.Deserialize<CrashReport>(await File.ReadAllTextAsync(path));
				if (report != null) await SubmitAsync(report);
			}
		}
		catch { }
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
	{
		if (Interlocked.Exchange(ref _handling, 1) != 0) return;
		try
		{
			Exception exception = args.ExceptionObject as Exception
				?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown unmanaged exception");
			Capture(exception, "Unhandled exception", args.IsTerminating, showDialog: false);
		}
		finally { Interlocked.Exchange(ref _handling, 0); }
	}

	private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
	{
		Capture(args.Exception, "Unobserved background task", false);
		args.SetObserved();
	}

	private static T Safe<T>(Func<T> getter, T fallback)
	{
		try { return getter(); } catch { return fallback; }
	}
}

public sealed partial class CrashReporterDialog : Window
{
	private readonly CrashReport _report;
	private readonly TextEdit _description = new();
	private readonly CheckBox _includeLogs = new() { Text = "Include recent application logs", ButtonPressed = true };
	private readonly Label _status = new();
	private readonly Button _send = new() { Text = "Send Report" };

	public CrashReporterDialog(CrashReport report)
	{
		_report = report;
		Title = "BrickVerse Crash Reporter";
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		MinSize = new Vector2I(600, 480);
		Size = new Vector2I(680, 540);
		Exclusive = true;
		CloseRequested += QueueFree;
	}

	public override void _Ready()
	{
		VBoxContainer root = new();
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.OffsetLeft = 18;
		root.OffsetTop = 18;
		root.OffsetRight = -18;
		root.OffsetBottom = -18;
		AddChild(root);
		Label title = new() { Text = $"BrickVerse {_report.Application} encountered an error", AutowrapMode = TextServer.AutowrapMode.WordSmart };
		title.AddThemeFontSizeOverride("font_size", 22);
		root.AddChild(title);
		root.AddChild(new Label { Text = "A diagnostic report was saved locally. Add what you were doing, then send it to help us fix the crash.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
		root.AddChild(new Label { Text = $"Report ID: {_report.ReportId}\n{_report.ExceptionType}: {_report.ExceptionMessage}", AutowrapMode = TextServer.AutowrapMode.WordSmart });
		root.AddChild(new Label { Text = "What were you doing when this happened?" });
		_description.PlaceholderText = "Steps leading up to the crash (optional)";
		_description.CustomMinimumSize = new Vector2(0, 150);
		_description.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		root.AddChild(_description);
		root.AddChild(_includeLogs);
		root.AddChild(_status);
		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.End };
		Button folder = new() { Text = "Open Reports Folder" };
		Button close = new() { Text = "Close" };
		folder.Pressed += () => OS.ShellOpen(ProjectSettings.GlobalizePath("user://crash-reports"));
		close.Pressed += QueueFree;
		_send.Pressed += Send;
		buttons.AddChild(folder); buttons.AddChild(close); buttons.AddChild(_send);
		root.AddChild(buttons);
	}

	private async void Send()
	{
		_send.Disabled = true;
		_status.Text = "Sending crash report…";
		_report.UserDescription = _description.Text.Trim();
		if (!_includeLogs.ButtonPressed) _report.RecentLogs = "";
		CrashReporter.UpdatePending(_report);
		bool sent = await CrashReporter.SubmitAsync(_report);
		_status.Text = sent ? "Report sent. Thank you." : "Could not send right now. The report will retry next launch.";
		_send.Text = sent ? "Sent" : "Retry";
		_send.Disabled = sent;
	}
}
