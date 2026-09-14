// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using BrickVerse.Datamodel;

namespace BrickVerse.Creator.UI;

internal static class ForgeShellTool
{
	public static object Definition => new
	{
		name = "run_shell",
		description = "Run a PowerShell or cmd command on the user's Windows computer in the open project. Creator requires explicit approval of every exact command, even with Always allow. Commands have normal user permissions, not a filesystem sandbox. Explain why the command is needed. Never retry an uncertain mutation automatically.",
		inputSchema = JsonDocument.Parse("""
        {"type":"object","properties":{
          "shell":{"type":"string","enum":["powershell","cmd"]},
          "command":{"type":"string","minLength":1,"maxLength":16000},
          "working_directory":{"type":"string","description":"Directory relative to the current Creator project. Defaults to project root."},
          "reason":{"type":"string","minLength":1,"maxLength":1000},
          "timeout_seconds":{"type":"integer","minimum":1,"maximum":60}
        },"required":["shell","command","reason"],"additionalProperties":false}
        """).RootElement.Clone()
	};

	public static async Task<(string Text, bool IsError)> RunAsync(JsonElement args, Control owner, CancellationToken lifetime)
	{
		if (!OperatingSystem.IsWindows()) return ("PowerShell/cmd tooling is available on Windows only.", true);
		var world = World.Current;
		var project = world?.LinkedSession;
		if (project == null) return ("Open a Creator project before running a command.", true);
		string shell = args.GetProperty("shell").GetString() ?? "";
		string command = args.GetProperty("command").GetString() ?? "";
		string reason = args.GetProperty("reason").GetString() ?? "";
		if (shell is not ("powershell" or "cmd") || string.IsNullOrWhiteSpace(command) || command.Length > 16000
			|| string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
			return ("Supply a supported shell, a command (up to 16000 characters), and a reason.", true);
		string directory = project.GlobalizePath(args.TryGetProperty("working_directory", out var cwd) ? cwd.GetString() ?? "" : "");
		if (!Directory.Exists(directory)) return ("The requested working directory does not exist.", true);
		int timeout = args.TryGetProperty("timeout_seconds", out var seconds) ? seconds.GetInt32() : 30;
		if (timeout < 1 || timeout > 60) return ("Timeout must be between 1 and 60 seconds.", true);

		var decision = new TaskCompletionSource<bool>();
		var dialog = new ConfirmationDialog { Title = "Forge wants to run a command", OkButtonText = "Run once", CancelButtonText = "Decline", Exclusive = true };
		var layout = new VBoxContainer();
		layout.AddChild(new Label { Text = reason, AutowrapMode = TextServer.AutowrapMode.WordSmart });
		layout.AddChild(new Label { Text = $"Shell: {shell}    Timeout: {timeout}s\nWorking directory: {directory}", AutowrapMode = TextServer.AutowrapMode.WordSmart });
		layout.AddChild(new TextEdit { Text = command, Editable = false, CustomMinimumSize = new Vector2(640, 220), SizeFlagsVertical = Control.SizeFlags.ExpandFill });
		layout.AddChild(new Label { Text = "Runs with your user permissions and can modify files or access the network.\nApproval is for this command only. This request expires after 90 seconds.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
		dialog.AddChild(layout);
		layout.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		layout.OffsetLeft = 16;
		layout.OffsetRight = -16;
		layout.OffsetTop = 16;
		layout.OffsetBottom = -60;
		dialog.Confirmed += () => decision.TrySetResult(true);
		dialog.Canceled += () => decision.TrySetResult(false);
		dialog.CloseRequested += () => decision.TrySetResult(false);
		owner.AddChild(dialog);
		dialog.PopupCentered(new Vector2I(700, 420));
		dialog.GetCancelButton().GrabFocus();
		using var registration = lifetime.Register(() => decision.TrySetResult(false));
		var winner = await Task.WhenAny(decision.Task, Task.Delay(TimeSpan.FromSeconds(90), lifetime));
		bool approved = winner == decision.Task && await decision.Task && !lifetime.IsCancellationRequested;
		if (GodotObject.IsInstanceValid(dialog)) dialog.QueueFree();
		if (!approved) return ("Command declined, cancelled, or approval expired. Nothing was executed.", true);
		if (!ReferenceEquals(world, World.Current) || !ReferenceEquals(project, World.Current?.LinkedSession))
			return ("The active project changed while approval was pending. Nothing was executed.", true);

		var start = new ProcessStartInfo
		{
			FileName = shell == "cmd" ? Path.Combine(global::System.Environment.SystemDirectory, "cmd.exe")
				: Path.Combine(global::System.Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
			WorkingDirectory = directory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true
		};
		if (shell == "powershell")
		{
			start.ArgumentList.Add("-NoLogo"); start.ArgumentList.Add("-NoProfile");
			start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
			start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
		}
		else
		{
			// Pass approved source through stdin: Windows argv escaping is not cmd syntax.
			start.ArgumentList.Add("/d"); start.ArgumentList.Add("/q");
		}
		using var process = new Process { StartInfo = start };
		using var execution = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
		execution.CancelAfter(TimeSpan.FromSeconds(timeout));
		process.Start();
		var stdout = ReadBoundedAsync(process.StandardOutput, execution.Token);
		var stderr = ReadBoundedAsync(process.StandardError, execution.Token);
		bool interrupted = false;
		try
		{
			if (shell == "cmd")
				await process.StandardInput.WriteAsync((command + "\r\nexit\r\n").AsMemory(), execution.Token);
			process.StandardInput.Close();
			await process.WaitForExitAsync(execution.Token);
		}
		catch (OperationCanceledException)
		{
			interrupted = true;
			try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
		}
		string output = await stdout;
		string errors = await stderr;
		var result = new
		{
			shell,
			command,
			workingDirectory = directory,
			exitCode = process.HasExited ? (int?)process.ExitCode : null,
			timedOutOrCancelled = interrupted,
			stdout = output,
			stderr = errors,
			note = interrupted ? "Execution interrupted; inspect any partial changes before retrying." : ""
		};
		return (JsonSerializer.Serialize(result), interrupted || process.ExitCode != 0);
	}

	private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
	{
		var text = new StringBuilder();
		var buffer = new char[2048];
		bool truncated = false;
		try
		{
			int read;
			while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
			{
				int keep = Math.Min(read, 8000 - text.Length);
				text.Append(buffer, 0, keep);
				truncated |= keep < read;
			}
		}
		catch (OperationCanceledException) { }
		return text + (truncated ? "\n[Output truncated]" : "");
	}
}
