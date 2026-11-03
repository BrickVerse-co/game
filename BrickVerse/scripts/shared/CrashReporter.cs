// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

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
using Godot;

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
	private const string EngineLogPath = "user://logs/brickverseapp.log";
	private const int MaxDiagnosticLogChars = 90_000;
	private static readonly System.Net.Http.HttpClient Http = new()
	{
		Timeout = TimeSpan.FromSeconds(12),
	};
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
			if (_initialized)
				return;
			_initialized = true;
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		}
		_ = UploadPendingReportsAsync();
	}

	public static CrashReport Capture(
		Exception exception,
		string context,
		bool fatal,
		bool showDialog = true
	)
	{
		CrashReport report = CreateReport(exception, context, fatal);
		Save(report, pending: true);
		BV.PrintErr($"Crash report {report.ReportId} saved for {context}: ", exception);
		if (showDialog)
			Present(report);
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
			BuildCommit = string.IsNullOrWhiteSpace(Globals.ShortBuildCommit)
				? "local"
				: Globals.ShortBuildCommit,
			OperatingSystem = Safe(
				() => $"{OS.GetName()} {OS.GetVersion()}",
				System.Environment.OSVersion.ToString()
			),
			Processor = Safe(OS.GetProcessorName, "Unknown"),
			ProcessorCount = System.Environment.ProcessorCount,
			ManagedMemoryBytes = GC.GetTotalMemory(false),
			Renderer = renderer,
			CommandLine = RedactCommandLine(OS.GetCmdlineArgs()),
			ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
			ExceptionMessage = RedactSensitiveText(exception.Message),
			Exception = RedactSensitiveText(FormatException(exception)),
			RecentLogs = CollectDiagnosticLogs(),
		};
	}

	private static string FormatException(Exception exception)
	{
		string formatted = exception.ToString();
		if (string.IsNullOrWhiteSpace(exception.StackTrace))
			formatted += $"\nCapture stack:\n{System.Environment.StackTrace}";
		if (exception is AggregateException aggregate)
		{
			formatted += "\nFlattened background exceptions:";
			int index = 0;
			foreach (Exception inner in aggregate.Flatten().InnerExceptions)
				formatted += $"\n--- Exception {++index} ---\n{inner}";
		}
		return formatted;
	}

	private static string CollectDiagnosticLogs()
	{
		string memoryLogs = BV.GetRecentLogs();
		string engineLogs = ReadLogTail(EngineLogPath, MaxDiagnosticLogChars / 2);
		string combined =
			"=== BrickVerse recent events ===\n"
			+ (string.IsNullOrWhiteSpace(memoryLogs) ? "(none captured)" : memoryLogs)
			+ "\n\n=== Godot/native log tail ===\n"
			+ (string.IsNullOrWhiteSpace(engineLogs) ? "(log unavailable)" : engineLogs);
		combined = RedactSensitiveText(combined);
		return combined.Length <= MaxDiagnosticLogChars
			? combined
			: combined[^MaxDiagnosticLogChars..];
	}

	private static string RedactSensitiveText(string text)
	{
		if (string.IsNullOrEmpty(text))
			return text;
		text = Regex.Replace(
			text,
			@"(?i)(authorization\s*[:=]\s*bearer\s+)[^\s,;]+",
			"$1<redacted>"
		);
		text = Regex.Replace(
			text,
			@"(?i)((?:auth_token|token|ctoken|creatorToken|password|secret)[\""']?\s*[:=]\s*[\""']?)[^\s,;\""']+",
			"$1<redacted>"
		);
		return text;
	}

	private static string ReadLogTail(string path, int maxChars)
	{
		try
		{
			string absolutePath = ProjectSettings.GlobalizePath(path);
			if (!File.Exists(absolutePath))
				return "";
			using FileStream stream = new(
				absolutePath,
				FileMode.Open,
				System.IO.FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete
			);
			long bytesToRead = Math.Min(stream.Length, maxChars * 4L);
			stream.Seek(-bytesToRead, SeekOrigin.End);
			byte[] buffer = new byte[(int)bytesToRead];
			int read = stream.Read(buffer, 0, buffer.Length);
			string text = Encoding.UTF8.GetString(buffer, 0, read);
			// A tail seek can begin in the middle of a line or UTF-8 sequence.
			int firstLine = text.IndexOf('\n');
			if (stream.Length > bytesToRead && firstLine >= 0)
				text = text[(firstLine + 1)..];
			return text.Length <= maxChars ? text : text[^maxChars..];
		}
		catch (Exception error)
		{
			return $"(could not read engine log: {error.GetType().Name}: {error.Message})";
		}
	}

	private static string RedactCommandLine(string[] args)
	{
		return string.Join(
			" ",
			args.Select(arg =>
				Regex.Replace(
					arg,
					@"(?i)(--?(?:token|ctoken|auth|password|secret)(?:=|\s+)).+",
					"$1<redacted>"
				)
			)
		);
	}

	private static string ReportPath(CrashReport report, bool pending) =>
		Path.Combine(
			ProjectSettings.GlobalizePath(ReportsFolder),
			$"{report.TimestampUtc:yyyyMMdd-HHmmss}-{report.ReportId}{(pending ? ".pending" : ".sent")}.json"
		);

	private static void Save(CrashReport report, bool pending)
	{
		try
		{
			string folder = ProjectSettings.GlobalizePath(ReportsFolder);
			Directory.CreateDirectory(folder);
			string target = ReportPath(report, pending);
			string temporary = target + ".tmp";
			File.WriteAllText(temporary, JsonSerializer.Serialize(report, JsonOptions));
			File.Move(temporary, target, true);
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
			if (
				_host == null
				|| !_host.TryGetTarget(out Node? host)
				|| !GodotObject.IsInstanceValid(host)
			)
			{
				OS.Alert(
					$"BrickVerse encountered an error.\n\nReport ID: {report.ReportId}\nA local report was saved.",
					"BrickVerse Crash Reporter"
				);
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
				Globals.ApiEndpoint.PathJoin("/v3/diagnostics/crash-report"),
				content
			);
			if (!response.IsSuccessStatusCode)
				return false;

			string pendingPath = ReportPath(report, pending: true);
			string sentPath = ReportPath(report, pending: false);
			if (File.Exists(pendingPath))
				File.Move(pendingPath, sentPath, true);
			else
				Save(report, pending: false);
			return true;
		}
		catch
		{
			return false;
		}
	}

	internal static void UpdatePending(CrashReport report) => Save(report, pending: true);

	private static async Task UploadPendingReportsAsync()
	{
		try
		{
			string folder = ProjectSettings.GlobalizePath(ReportsFolder);
			if (!Directory.Exists(folder))
				return;
			foreach (string path in Directory.EnumerateFiles(folder, "*.pending.json").Take(10))
			{
				try
				{
					CrashReport? report = JsonSerializer.Deserialize<CrashReport>(
						await File.ReadAllTextAsync(path)
					);
					if (report != null)
						await SubmitAsync(report);
				}
				catch (Exception error)
				{
					BV.PrintErr($"Could not retry crash report {Path.GetFileName(path)}: {error.Message}");
					try { File.Move(path, path + ".invalid", true); } catch { }
				}
			}
		}
		catch { }
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
	{
		if (Interlocked.Exchange(ref _handling, 1) != 0)
			return;
		try
		{
			Exception exception =
				args.ExceptionObject as Exception
				?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown unmanaged exception");
			Capture(exception, "Unhandled exception", args.IsTerminating, showDialog: false);
		}
		finally
		{
			Interlocked.Exchange(ref _handling, 0);
		}
	}

	private static void OnUnobservedTaskException(
		object? sender,
		UnobservedTaskExceptionEventArgs args
	)
	{
		Capture(args.Exception, "Unobserved background task", false);
		args.SetObserved();
	}

	private static T Safe<T>(Func<T> getter, T fallback)
	{
		try
		{
			return getter();
		}
		catch
		{
			return fallback;
		}
	}
}

public sealed partial class CrashReporterDialog : Window
{
	private readonly CrashReport _report;
	private readonly TextEdit _description = new();
	private readonly CheckBox _includeLogs = new()
	{
		Text = "Include recent application logs",
		ButtonPressed = true,
	};
	private readonly Label _status = new();
	private readonly Button _send = new() { Text = "Send Report" };
	private readonly TextEdit _details = new()
	{
		Editable = false,
		Visible = false,
		CustomMinimumSize = new Vector2(0, 135),
	};

	public CrashReporterDialog(CrashReport report)
	{
		_report = report;
		Title = "BrickVerse Crash Reporter";
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
		MinSize = new Vector2I(620, 540);
		Size = new Vector2I(720, 620);
		Exclusive = true;
		CloseRequested += QueueFree;
	}

	public override void _Ready()
	{
		ColorRect background = new() { Color = new Color("10141c") };
		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(background);

		VBoxContainer root = new();
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.OffsetLeft = 28;
		root.OffsetTop = 24;
		root.OffsetRight = -28;
		root.OffsetBottom = -24;
		root.AddThemeConstantOverride("separation", 12);
		AddChild(root);

		HBoxContainer heading = new();
		heading.AddThemeConstantOverride("separation", 14);
		Label icon = new() { Text = "!", CustomMinimumSize = new Vector2(44, 44) };
		icon.HorizontalAlignment = HorizontalAlignment.Center;
		icon.VerticalAlignment = VerticalAlignment.Center;
		icon.AddThemeFontSizeOverride("font_size", 28);
		icon.AddThemeColorOverride("font_color", new Color("ff6b5f"));
		heading.AddChild(icon);
		VBoxContainer headingText = new();
		headingText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		Label title = new()
		{
			Text = $"{_report.Application} encountered a problem",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		title.AddThemeFontSizeOverride("font_size", 24);
		headingText.AddChild(title);
		Label subtitle = new()
		{
			Text = _report.Fatal
				? "BrickVerse saved diagnostic information before closing."
				: "BrickVerse recovered, but this feature may not work correctly.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		subtitle.AddThemeColorOverride("font_color", new Color("aeb8ca"));
		headingText.AddChild(subtitle);
		heading.AddChild(headingText);
		root.AddChild(heading);

		PanelContainer summaryPanel = new();
		StyleBoxFlat summaryStyle = new()
		{
			BgColor = new Color("171d28"),
			BorderColor = new Color("30394a"),
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
			ContentMarginLeft = 14,
			ContentMarginRight = 14,
			ContentMarginTop = 12,
			ContentMarginBottom = 12,
		};
		summaryPanel.AddThemeStyleboxOverride("panel", summaryStyle);
		VBoxContainer summary = new();
		Label error = new()
		{
			Text = $"{_report.ExceptionType}: {_report.ExceptionMessage}",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		error.AddThemeColorOverride("font_color", new Color("f2f5fa"));
		summary.AddChild(error);
		HBoxContainer reportRow = new();
		Label reportId = new() { Text = $"Report ID  {_report.ReportId}" };
		reportId.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		reportId.AddThemeColorOverride("font_color", new Color("8ea0bb"));
		reportRow.AddChild(reportId);
		Button copy = new() { Text = "Copy ID", Flat = true };
		copy.Pressed += () =>
		{
			DisplayServer.ClipboardSet(_report.ReportId);
			copy.Text = "Copied";
		};
		reportRow.AddChild(copy);
		summary.AddChild(reportRow);
		summaryPanel.AddChild(summary);
		root.AddChild(summaryPanel);

		Label prompt = new() { Text = "What happened just before the problem?" };
		prompt.AddThemeFontSizeOverride("font_size", 16);
		root.AddChild(prompt);
		_description.PlaceholderText = "For example: I clicked Play after changing the terrain…";
		_description.CustomMinimumSize = new Vector2(0, 120);
		_description.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		root.AddChild(_description);
		root.AddChild(_includeLogs);

		_details.Text =
			$"Context: {_report.Context}\nVersion: {_report.AppVersion} ({_report.BuildCommit})\n"
			+ $"System: {_report.OperatingSystem}\nRenderer: {_report.Renderer}\n\n{_report.Exception}";
		root.AddChild(_details);
		Button toggleDetails = new() { Text = "Show technical details", Flat = true };
		toggleDetails.Pressed += () =>
		{
			_details.Visible = !_details.Visible;
			toggleDetails.Text = _details.Visible
				? "Hide technical details"
				: "Show technical details";
		};
		root.AddChild(toggleDetails);

		_status.AddThemeColorOverride("font_color", new Color("9fb0ca"));
		root.AddChild(_status);
		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 8);
		Button folder = new() { Text = "Reports Folder", Flat = true };
		Button close = new() { Text = "Not Now" };
		folder.Pressed += () => OS.ShellOpen(ProjectSettings.GlobalizePath("user://crash-reports"));
		close.Pressed += QueueFree;
		_send.Pressed += Send;
		buttons.AddChild(folder);
		buttons.AddChild(close);
		_send.AddThemeColorOverride("font_color", Colors.White);
		buttons.AddChild(_send);
		root.AddChild(buttons);
	}

	private async void Send()
	{
		_send.Disabled = true;
		_status.Text = "Sending crash report…";
		_report.UserDescription = _description.Text.Trim();
		if (!_includeLogs.ButtonPressed)
			_report.RecentLogs = "";
		CrashReporter.UpdatePending(_report);
		bool sent = await CrashReporter.SubmitAsync(_report);
		_status.Text = sent
			? "Report sent. Thank you."
			: "Could not send right now. The report will retry next launch.";
		_send.Text = sent ? "Sent" : "Retry";
		_send.Disabled = sent;
	}
}
