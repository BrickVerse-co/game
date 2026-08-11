// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Creator.LSP.Schemas;
using BrickVerse.Datamodel;
using BrickVerse.Shared;
using BrickVerse.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using DatamodelScript = BrickVerse.Datamodel.Script;

namespace BrickVerse.Creator.LSP;

public class LuaCompletionService(CreatorSession session)
{
	private readonly CreatorSession _session = session;
	private readonly string _workspacePath = session.ProjectFolderPath;
	private Process _luaLSProcess = null!;
	private LspClient _client = null!;
	private readonly Dictionary<string, int> _versions = [];
	private readonly CancellationTokenSource _shutdown = new();
	private bool _isShutdown;

	public event Action<string, List<LspDiagnostic>>? PublishDiagnostics;

	public static readonly string[] LuaKeywords =
	[
		"and", "break", "do", "else", "elseif", "end",
		"false", "for", "function", "if",
		"in", "local", "nil", "not", "or", "repeat",
		"return", "then", "true", "until", "while",
		"continue", "const"
	];

	public async Task InitAsync()
	{
		string definitionPath = Path.Combine(_workspacePath, ".bvproject", "luau", "def.d.luau");
		if (!File.Exists(definitionPath))
			throw new FileNotFoundException("Generated BrickVerse Luau definitions were not found.", definitionPath);

		ProcessStartInfo processStartInfo = new()
		{
			FileName = NativeBinHelper.ResolveLuauLspBinPath(),
			// Pass an absolute definition file so the server cannot lose BrickVerse
			// globals when a project is opened from a path containing spaces.
			Arguments = $"lsp --stdio --definitions=\"{definitionPath}\"",
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = _workspacePath
		};

		_luaLSProcess = Process.Start(processStartInfo) ?? throw new Exception("Failed to start language server process");

		_luaLSProcess.ErrorDataReceived += (sender, e) =>
		{
			if (!string.IsNullOrEmpty(e.Data))
			{
				BV.PrintErr($"Server Error: {e.Data}");
			}
		};

		_luaLSProcess.BeginErrorReadLine();

		//BV.Print("LuaLS Started");

		_client = new LspClient(_luaLSProcess.StandardOutput.BaseStream, _luaLSProcess.StandardInput.BaseStream);
		await _client.InitializeAsync(_workspacePath, _shutdown.Token);
		if (_isShutdown) return;

		_client.PublishDiagnostics += OnPublishDiagnostics;

		//BV.Print("Language server initialized at ", _workspacePath);
	}

	private void OnPublishDiagnostics(LspPublishDiagnosticsParams @params)
	{
		string normalizedUri = new Uri(@params.Uri).AbsoluteUri;
		if (_client.LspPathToFull.TryGetValue(normalizedUri, out string? fullPath))
		{
			// Call publish in main thread
			Callable.From(() =>
			{
				PublishDiagnostics?.Invoke(fullPath, @params.Diagnostics);
			}).CallDeferred();
		}
	}

	public void Shutdown()
	{
		if (_isShutdown) return;
		_isShutdown = true;
		_shutdown.Cancel();
		_client?.Dispose();
		if (_luaLSProcess != null && !_luaLSProcess.HasExited)
		{
			_luaLSProcess.Kill();
			_luaLSProcess.Dispose();
		}
	}

	public async Task OpenScriptAsync(string scriptPath)
	{
		string content = File.ReadAllText(scriptPath);
		await _client.DidOpenAsync(scriptPath, "luau", content);
	}

	public async Task CloseScriptAsync(string scriptPath)
	{
		_versions.Remove(scriptPath);
		await _client.DidCloseAsync(scriptPath);
	}

	public async Task UpdateScriptChangeAsync(string scriptPath, string scriptContent)
	{
		if (!_versions.ContainsKey(scriptPath)) _versions[scriptPath] = 1;
		_versions[scriptPath]++;
		await _client.DidChangeAsync(scriptPath, scriptContent, _versions[scriptPath]);
	}

	public async Task<List<CodeEditCompletionItem>> GetCompletionsAsync(CodeEditCompletionContext context, CancellationToken? cancelToken = null)
	{
		LspCompletionItem[]? completionResult = await _client.RequestCompletionAsync(
			context.ScriptPath,
			context.CursorLine,
			context.CursorColumn,
			cancelToken ?? CancellationToken.None);

		List<CodeEditCompletionItem> items = [];

		if (completionResult != null)
		{
			foreach (LspCompletionItem item in completionResult)
			{
				CodeEdit.CodeCompletionKind kind = item.Kind switch
				{
					9 => CodeEdit.CodeCompletionKind.Function, // Method
					3 => CodeEdit.CodeCompletionKind.Function, // Function
					21 => CodeEdit.CodeCompletionKind.Constant, // Constant
					7 => CodeEdit.CodeCompletionKind.Class, // Class
					13 => CodeEdit.CodeCompletionKind.Enum, // Enum
					6 => CodeEdit.CodeCompletionKind.Variable, // Variable
					20 => CodeEdit.CodeCompletionKind.Member, // EnumMember
					10 => CodeEdit.CodeCompletionKind.Member, // Property
					5 => CodeEdit.CodeCompletionKind.Member, // Field
					14 => CodeEdit.CodeCompletionKind.PlainText, // Keyword
					_ => CodeEdit.CodeCompletionKind.PlainText,
				};

				items.Add(new()
				{
					DisplayText = item.Label ?? "",
					Kind = kind,
					Detail = item.Detail ?? item.LabelDetails?.Description ?? FirstDocumentationLine(item.Documentation?.Value),
					Documentation = item.Documentation?.Value ?? "",
					InsertText = string.IsNullOrWhiteSpace(item.InsertText) ? item.Label ?? "" : item.InsertText
				});
			}
		}

		AddDatamodelChildCompletions(context, items);

		return items;
	}

	private void AddDatamodelChildCompletions(CodeEditCompletionContext context, List<CodeEditCompletionItem> items)
	{
		if (context.CursorLine < 0 || context.CursorLine >= context.Content.Split('\n').Length) return;
		string line = context.Content.Split('\n')[context.CursorLine].TrimEnd('\r');
		int column = Math.Clamp(context.CursorColumn, 0, line.Length);
		string beforeCaret = line[..column];
		Match access = Regex.Match(beforeCaret,
			@"(?<root>world|game|script)(?<path>(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\.(?<prefix>[A-Za-z_][A-Za-z0-9_]*)?$");
		if (!access.Success) return;

		Instance? target = ResolveCompletionRoot(access.Groups["root"].Value, context.ScriptPath);
		if (target == null) return;

		foreach (string segment in access.Groups["path"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries))
		{
			target = segment == "Parent" ? target.Parent : target.FindChild(segment);
			if (target == null) return;
		}

		string prefix = access.Groups["prefix"].Value;
		HashSet<string> existing = items.Select(static item => item.InsertText).ToHashSet(StringComparer.OrdinalIgnoreCase);
		List<CodeEditCompletionItem> children = [];
		foreach (Instance child in target.GetChildren().OrderBy(static child => child.Name, StringComparer.OrdinalIgnoreCase))
		{
			if (!IsValidLuauIdentifier(child.Name)
				|| !child.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
				|| !existing.Add(child.Name)) continue;

			children.Add(new CodeEditCompletionItem
			{
				DisplayText = child.Name,
				InsertText = child.Name,
				Kind = CodeEdit.CodeCompletionKind.Member,
				Detail = $"{child.ClassName} child",
				Documentation = $"Child instance `{child.Name}` ({child.ClassName}) under `{target.Name}`."
			});
		}
		items.InsertRange(0, children);
	}

	private Instance? ResolveCompletionRoot(string rootName, string scriptPath)
	{
		World? world = World.Current != null && _session.OpenedWorlds.Contains(World.Current)
			? World.Current
			: _session.OpenedWorlds.FirstOrDefault();
		if (rootName is "world" or "game") return world;
		if (world == null) return null;

		string relativePath = Path.GetRelativePath(_workspacePath, scriptPath).SanitizePath();
		return world.GetDescendants().OfType<DatamodelScript>().FirstOrDefault(script =>
			string.Equals(script.LinkedScript?.LinkedPath?.SanitizePath(), relativePath, StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsValidLuauIdentifier(string name) =>
		!string.IsNullOrWhiteSpace(name)
		&& (char.IsLetter(name[0]) || name[0] == '_')
		&& name.Skip(1).All(static character => char.IsLetterOrDigit(character) || character == '_');

	private static string FirstDocumentationLine(string? documentation)
	{
		if (string.IsNullOrWhiteSpace(documentation)) return string.Empty;
		return documentation.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
	}

	public async Task<string?> GetHoverAsync(string scriptPath, int line, int column, CancellationToken cancellationToken)
	{
		LspHover? hover = await _client.RequestHoverAsync(scriptPath, line, column, cancellationToken);
		if (hover == null) return null;

		JsonElement contents = hover.Contents;
		if (contents.ValueKind == JsonValueKind.String) return contents.GetString();
		if (contents.ValueKind == JsonValueKind.Object)
		{
			if (contents.TryGetProperty("value", out JsonElement value)) return value.GetString();
			if (contents.TryGetProperty("language", out _) && contents.TryGetProperty("value", out value)) return value.GetString();
		}
		if (contents.ValueKind == JsonValueKind.Array)
		{
			List<string> sections = [];
			foreach (JsonElement item in contents.EnumerateArray())
			{
				string? value = item.ValueKind == JsonValueKind.String ? item.GetString() :
					item.TryGetProperty("value", out JsonElement objectValue) ? objectValue.GetString() : null;
				if (!string.IsNullOrWhiteSpace(value)) sections.Add(value);
			}
			return string.Join("\n\n", sections);
		}
		return null;
	}
}

public struct CodeEditCompletionItem
{
	public string DisplayText { get; set; }
	public CodeEdit.CodeCompletionKind Kind { get; set; }
	public string InsertText { get; set; }
	public string Detail { get; set; }
	public string Documentation { get; set; }
}

public struct CodeEditCompletionContext
{
	public string ScriptPath { get; set; }
	public string Content { get; set; }
	public int CursorLine { get; set; }
	public int CursorColumn { get; set; }
}
