// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using BrickVerse.Creator.UI.TextEditor;
using BrickVerse.Attributes;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using System.Diagnostics.CodeAnalysis;

namespace BrickVerse.Creator.UI;

internal static class ForgeToolCatalog
{
	public static readonly List<ForgeChatToolDefinition> Definitions =
	[
		Create(
			"get_creator_state",
			"Get the current Creator state including the active world, selection, active editor, and console snippet.",
			"""
			{
			  "type": "object",
			  "properties": {},
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"get_world_tree",
			"Inspect the active world hierarchy with stable instance paths, class names, world ID, universe ID, and configurable depth.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "max_depth": { "type": "integer", "minimum": 1, "maximum": 12 },
			    "max_nodes": { "type": "integer", "minimum": 1, "maximum": 1000 }
			  },
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"list_instantiable_classes",
			"List classes that Forge can create in the open world.",
			"""
			{
			  "type": "object",
			  "properties": {},
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"search_instances",
			"Search world instances by name, class, or path.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "query": { "type": "string" },
			    "class_name": { "type": "string" },
			    "under_path": { "type": "string" },
			    "limit": { "type": "integer", "minimum": 1, "maximum": 100 }
			  },
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"inspect_instance",
			"Inspect a specific instance path with public properties, children, tags, script source, and editor-relevant metadata.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "path": { "type": "string" }
			  },
			  "required": ["path"],
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"select_instances",
			"Select one or more instance paths in Creator.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "paths": {
			      "type": "array",
			      "items": { "type": "string" },
			      "minItems": 1
			    },
			    "mode": {
			      "type": "string",
			      "enum": ["replace", "add"]
			    }
			  },
			  "required": ["paths"],
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"create_instance",
			"Create a new instantiable instance under a parent path and optionally set common properties. Use canonical paths returned by search_instances, such as world.ScriptService; slash-form paths are accepted too. For scripts use ServerScript, ClientScript, or ModuleScript (Script aliases ServerScript) and pass source in this same call; Creator creates and links the project .luau file atomically.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "class_name": { "type": "string" },
			    "parent_path": { "type": "string" },
			    "name": { "type": "string" },
			    "source": { "type": "string", "description": "Initial Luau source for a Script instance." },
			    "properties": {
			      "type": "object",
			      "additionalProperties": true
			    }
			  },
			  "required": ["class_name"],
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"manage_project_file",
			"List, read, create or replace, and delete files inside the open linked Creator project. Prefer create_instance/edit_script_source for scripts so world links stay synchronized.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "action": { "type": "string", "enum": ["list", "read", "write", "delete"] },
			    "path": { "type": "string" },
			    "content": { "type": "string" }
			  },
			  "required": ["action"],
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"set_instance_properties",
			"Set writable public properties on an instance path.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "path": { "type": "string" },
			    "properties": {
			      "type": "object",
			      "additionalProperties": true
			    }
			  },
			  "required": ["path", "properties"],
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"delete_instance",
			"Delete an instance path from the current world.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "path": { "type": "string" }
			  },
			  "required": ["path"],
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"edit_script_source",
			"Replace the source of an existing user-visible Script. Use this instead of posting a full script in chat. The change is recorded for diff and rollback.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "path": { "type": "string" },
			    "source": { "type": "string" }
			  },
			  "required": ["path", "source"],
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"get_script_diff",
			"Get the latest Forge diff for a script path.",
			"""
			{
			  "type": "object",
			  "properties": { "path": { "type": "string" } },
			  "required": ["path"],
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"rollback_last_change",
			"Rollback a specific Forge change by change_id, or the most recent change when omitted.",
			"""
			{
			  "type": "object",
			  "properties": { "change_id": { "type": "string" } },
			  "additionalProperties": false
			}
			"""
		),
		Create(
			"run_luau",
			"Run Luau in the current Creator world only after the user reviews and confirms the exact source. Use this for testing when execution is necessary; never claim it ran before confirmation.",
			"""
			{
			  "type": "object",
			  "properties": {
			    "source": { "type": "string" },
			    "compatibility": { "type": "boolean" },
			    "reason": { "type": "string" }
			  },
			  "required": ["source"],
			  "additionalProperties": false
			}
			"""
		),
	];

	private static ForgeChatToolDefinition Create(
		string name,
		string description,
		string schemaJson
	)
	{
		return new ForgeChatToolDefinition
		{
			Function = new ForgeChatToolDefinitionFunction
			{
				Name = name,
				Description = description,
				Parameters = JsonDocument.Parse(schemaJson).RootElement.Clone(),
			},
		};
	}
}

[RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetTypes()")]
internal sealed class ForgeToolExecutor
{
	private static readonly string[] _instantiableClassNames = typeof(Instance)
		.Assembly.GetTypes()
		.Where(static type =>
			type.IsClass
			&& !type.IsAbstract
			&& typeof(Instance).IsAssignableFrom(type)
			&& type.IsDefined(typeof(InstantiableAttribute), false)
			&& !type.IsDefined(typeof(InternalAttribute), false)
		)
		.Select(static type => type.Name)
		.OrderBy(static name => name, StringComparer.Ordinal)
		.ToArray();

	private readonly World _root;
	private readonly List<(string Id, Action Rollback)> _rollback = [];
	private readonly Dictionary<string, (string Before, string After)> _scriptDiffs = new(
		StringComparer.OrdinalIgnoreCase
	);
	public ForgeToolEvent? LastEvent { get; private set; }

	public void ResetLastEvent() => LastEvent = null;

	public ForgeToolExecutor(World root)
	{
		_root = root;
	}

	public string Execute(string toolName, string argumentsJson)
	{
		LastEvent = null;
		return toolName switch
		{
			"get_creator_state" => GetCreatorState(),
			"get_world_tree" => GetWorldTree(argumentsJson),
			"list_instantiable_classes" => ListInstantiableClasses(),
			"search_instances" => SearchInstances(argumentsJson),
			"inspect_instance" => InspectInstance(argumentsJson),
			"select_instances" => SelectInstances(argumentsJson),
			"create_instance" => CreateInstance(argumentsJson),
			"set_instance_properties" => SetInstanceProperties(argumentsJson),
			"delete_instance" => DeleteInstance(argumentsJson),
			"edit_script_source" => EditScriptSource(argumentsJson),
			"manage_project_file" => ManageProjectFile(argumentsJson),
			"get_script_diff" => GetScriptDiff(argumentsJson),
			"rollback_last_change" => RollbackLastChange(argumentsJson),
			"run_luau" => "User confirmation is required before Luau can run.",
			_ => $"Unknown Forge tool: {toolName}",
		};
	}

	public string DescribeWorldOutline(int maxDepth = 2, int maxNodes = 40)
	{
		StringBuilder builder = new();
		builder.AppendLine($"world ({_root.WorldName.Or(_root.Name)})");
		int written = 0;

		foreach (Instance child in _root.GetChildren())
		{
			WriteTree(builder, child, 0, maxDepth, maxNodes, ref written);
			if (written >= maxNodes)
			{
				builder.AppendLine("... truncated ...");
				break;
			}
		}

		return builder.ToString().TrimEnd();
	}

	public static string GetCreatableClassPreview(int limit = 24)
	{
		return string.Join(", ", _instantiableClassNames.Take(limit));
	}

	public ForgeRunLuauArgs ParseRunLuauArguments(string argumentsJson)
	{
		ForgeRunLuauArgs args =
			JsonSerializer.Deserialize(argumentsJson, ForgeJsonContext.Default.ForgeRunLuauArgs)
			?? throw new InvalidOperationException("Missing run_luau arguments.");
		if (string.IsNullOrWhiteSpace(args.Source))
			throw new InvalidOperationException("Luau source cannot be empty.");
		return args;
	}

	public string RunConfirmedLuau(ForgeRunLuauArgs args)
	{
		ClientScript script = _root.New<ClientScript>(_root.Environment);
		script.Name = $"ForgeRun_{DateTime.UtcNow:HHmmss}";
		script.Source = args.Source;
		script.Compatibility = args.Compatibility;
		script.IsEnabled = false;
		string linkedFile = CreateAndLinkScriptFile(script);
		_root.CreatorContext.Selections.SelectOnly(script);
		_root.ScriptService.Run(script);
		return $"Ran confirmed Luau as {script.LuaPath}. The generated ClientScript is linked to {linkedFile} and remains visible and selected for review or deletion.";
	}

	private Instance GetDefaultVisibleParent(string? className = null)
	{
		Instance? selected = _root.CreatorContext.Selections.SelectedInstances.FirstOrDefault();
		if (selected != null && IsUserAccessibleTarget(selected))
			return selected;

		// Scripts belong in the visible ScriptService by default. Falling back to
		// Environment keeps normal world objects in a user-editable hierarchy.
		if (
			!string.IsNullOrWhiteSpace(className)
			&& className.Contains("Script", StringComparison.OrdinalIgnoreCase)
		)
		{
			Instance? scriptService =
				ResolveInstance("world.ScriptService")
				?? _root
					.GetDescendants()
					.FirstOrDefault(static item =>
						item.ClassName.Equals("ScriptService", StringComparison.OrdinalIgnoreCase)
					);
			if (scriptService != null && IsUserAccessibleTarget(scriptService))
				return scriptService;
		}

		return _root.Environment;
	}

	private static readonly string[] RestrictedHierarchySegments =
	[
		"Temporary",
		"Hidden",
		"Internal",
		"Runtime",
		"Cache",
		"Preview",
	];

	private static bool IsUserAccessibleTarget(Instance instance)
	{
		string path = instance.LuaPath ?? string.Empty;
		string[] segments = path.Split(
			'.',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
		);
		return !segments.Any(segment =>
			RestrictedHierarchySegments.Any(blocked =>
				segment.Equals(blocked, StringComparison.OrdinalIgnoreCase)
				|| segment.StartsWith(blocked + "_", StringComparison.OrdinalIgnoreCase)
			)
		);
	}

	private static void EnsureVisibleCreatorTarget(Instance instance)
	{
		if (!IsUserAccessibleTarget(instance))
			throw new InvalidOperationException(
				$"'{instance.LuaPath}' is an internal Creator hierarchy and cannot be accessed by Forge."
			);
	}

	private string GetCreatorState()
	{
		StringBuilder builder = new();
		builder.AppendLine($"Active world: {_root.WorldName.Or(_root.Name)}");
		builder.AppendLine($"World ID: {_root.WorldID}");
		builder.AppendLine($"Universe ID: {_root.UniverseID}");
		builder.AppendLine(
			$"Selection count: {_root.CreatorContext.Selections.SelectedInstances.Count}"
		);

		if (_root.CreatorContext.Selections.SelectedInstances.Count > 0)
		{
			builder.AppendLine("Selection:");
			foreach (
				Instance selected in _root.CreatorContext.Selections.SelectedInstances.Take(10)
			)
			{
				builder.AppendLine($"- {FormatInstanceSummary(selected)}");
			}
		}

		if (Tabs.Singleton?.CurrentControl is TextEditorContainer editor)
		{
			builder.AppendLine($"Active editor: {editor.TargetFilePath}");
		}

		string consoleSnippet = ForgeTab.GetConsoleSnippet();
		if (!string.IsNullOrWhiteSpace(consoleSnippet))
		{
			builder.AppendLine("Console:");
			builder.AppendLine(consoleSnippet);
		}

		return builder.ToString().TrimEnd();
	}

	private string GetWorldTree(string argumentsJson)
	{
		int maxDepth = 4;
		int maxNodes = 200;
		using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
		if (document.RootElement.TryGetProperty("max_depth", out var depth) && depth.TryGetInt32(out var requestedDepth))
			maxDepth = Math.Clamp(requestedDepth, 1, 12);
		if (document.RootElement.TryGetProperty("max_nodes", out var nodes) && nodes.TryGetInt32(out var requestedNodes))
			maxNodes = Math.Clamp(requestedNodes, 1, 1000);

		return $"World: {_root.WorldName.Or(_root.Name)}\nWorld ID: {_root.WorldID}\nUniverse ID: {_root.UniverseID}\n{DescribeWorldOutline(maxDepth, maxNodes)}";
	}

	private string ListInstantiableClasses()
	{
		return $"Creatable classes ({_instantiableClassNames.Length}):\n"
			+ string.Join("\n", _instantiableClassNames.Select(static name => $"- {name}"));
	}

	private string SearchInstances(string argumentsJson)
	{
		ForgeSearchInstancesArgs args =
			JsonSerializer.Deserialize(
				argumentsJson,
				ForgeJsonContext.Default.ForgeSearchInstancesArgs
			) ?? new ForgeSearchInstancesArgs();
		Instance scope = string.IsNullOrWhiteSpace(args.UnderPath)
			? _root
			: ResolveInstance(args.UnderPath)
				?? throw new InvalidOperationException(
					$"Could not resolve path '{args.UnderPath}'."
				);
		EnsureVisibleCreatorTarget(scope);
		int limit = Math.Clamp(args.Limit, 1, 100);

		string query = args.Query?.Trim() ?? string.Empty;
		string className = args.ClassName?.Trim() ?? string.Empty;

		IEnumerable<Instance> candidates =
			scope == _root
				? [scope, .. scope.GetDescendants()]
				: [scope, .. scope.GetDescendants()];

		if (!string.IsNullOrWhiteSpace(className))
		{
			candidates = candidates.Where(instance =>
				instance.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase)
			);
		}

		if (!string.IsNullOrWhiteSpace(query))
		{
			candidates = candidates.Where(instance =>
				instance.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| instance.ClassName.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| instance.LuaPath.Contains(query, StringComparison.OrdinalIgnoreCase)
			);
		}

		List<Instance> results = candidates
			.Where(instance =>
			{
				try
				{
					EnsureVisibleCreatorTarget(instance);
					return true;
				}
				catch
				{
					return false;
				}
			})
			.Take(limit)
			.ToList();

		if (results.Count == 0)
		{
			return "No instances matched the query.";
		}

		StringBuilder builder = new();
		builder.AppendLine($"Found {results.Count} instance(s):");
		foreach (Instance result in results)
		{
			builder.AppendLine($"- {FormatInstanceSummary(result)}");
		}

		return builder.ToString().TrimEnd();
	}

	private string InspectInstance(string argumentsJson)
	{
		ForgeInspectInstanceArgs args =
			JsonSerializer.Deserialize(
				argumentsJson,
				ForgeJsonContext.Default.ForgeInspectInstanceArgs
			) ?? throw new InvalidOperationException("Missing inspect_instance arguments.");

		Instance instance =
			ResolveInstance(args.Path)
			?? ResolveScriptByLinkedPath(args.Path)
			?? throw new InvalidOperationException($"Could not resolve script instance or linked project file '{args.Path}'.");
		EnsureVisibleCreatorTarget(instance);

		StringBuilder builder = new();
		builder.AppendLine(FormatInstanceSummary(instance));
		builder.AppendLine($"Children: {instance.GetChildren().Length}");
		builder.AppendLine(
			$"Tags: {(instance.Tags.Length == 0 ? "(none)" : string.Join(", ", instance.Tags))}"
		);

		if (instance is Dynamic dynamicInstance)
		{
			builder.AppendLine($"Position: {FormatVector3(dynamicInstance.Position)}");
			builder.AppendLine($"Rotation: {FormatVector3(dynamicInstance.Rotation)}");
			builder.AppendLine($"Size: {FormatVector3(dynamicInstance.Size)}");
		}

		builder.AppendLine("Public properties:");
		foreach (
			PropertyInfo property in instance
				.GetType()
				.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(static property =>
					property.CanRead && property.GetIndexParameters().Length == 0
				)
				.OrderBy(static property => property.Name, StringComparer.Ordinal)
				.Take(80)
		)
		{
			try
			{
				object? value = property.GetValue(instance);
				string rendered = value switch
				{
					null => "null",
					string text when text.Length > 4000 => text[..4000] + "\n... truncated ...",
					Array array => $"[{array.Length} items]",
					_ => value.ToString() ?? string.Empty,
				};
				builder.AppendLine($"- {property.Name} = {rendered}");
			}
			catch (Exception ex)
			{
				builder.AppendLine($"- {property.Name} = <unavailable: {ex.Message}>");
			}
		}

		if (instance is BrickVerse.Datamodel.Script script)
		{
			builder.AppendLine("Script source:");
			builder.AppendLine(
				script.Source.Length <= 16000
					? script.Source
					: script.Source[..16000] + "\n... truncated ..."
			);
		}

		Instance[] children = instance.GetChildren();
		if (children.Length > 0)
		{
			builder.AppendLine("Child paths:");
			foreach (Instance child in children.Take(100))
			{
				builder.AppendLine($"- {child.LuaPath} [{child.ClassName}]");
			}
		}

		return builder.ToString().TrimEnd();
	}

	private string SelectInstances(string argumentsJson)
	{
		ForgeSelectInstancesArgs args =
			JsonSerializer.Deserialize(
				argumentsJson,
				ForgeJsonContext.Default.ForgeSelectInstancesArgs
			) ?? throw new InvalidOperationException("Missing select_instances arguments.");

		if (args.Paths.Count == 0)
		{
			throw new InvalidOperationException("No paths were provided.");
		}

		CreatorSelections selections = _root.CreatorContext.Selections;
		if (!string.Equals(args.Mode, "add", StringComparison.OrdinalIgnoreCase))
		{
			selections.DeselectAll();
		}

		List<string> selected = [];
		foreach (string path in args.Paths)
		{
			Instance instance =
				ResolveInstance(path)
				?? throw new InvalidOperationException($"Could not resolve path '{path}'.");
			EnsureVisibleCreatorTarget(instance);
			selections.Select(instance);
			selected.Add(instance.LuaPath);
		}

		return $"Selected {selected.Count} instance(s):\n"
			+ string.Join("\n", selected.Select(static path => $"- {path}"));
	}

	private string CreateInstance(string argumentsJson)
	{
		ForgeCreateInstanceArgs args =
			JsonSerializer.Deserialize(
				argumentsJson,
				ForgeJsonContext.Default.ForgeCreateInstanceArgs
			) ?? throw new InvalidOperationException("Missing create_instance arguments.");

		string className = args.ClassName.Trim();
		if (className.Equals("Script", StringComparison.OrdinalIgnoreCase))
			className = nameof(ServerScript);
		if (string.IsNullOrWhiteSpace(className))
		{
			throw new InvalidOperationException("class_name is required.");
		}

		if (!_instantiableClassNames.Contains(className, StringComparer.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"'{className}' is not an available instantiable class."
			);
		}

		Instance instance =
			Globals.LoadInstance<Instance>(className, _root)
			?? throw new InvalidOperationException(
				$"Creator could not instantiate class '{className}'."
			);

		Instance parentTo;
		if (!string.IsNullOrWhiteSpace(args.ParentPath))
		{
			parentTo =
				ResolveInstance(args.ParentPath)
				?? throw new InvalidOperationException(
					$"Could not resolve parent path '{args.ParentPath}'."
				);
			EnsureVisibleCreatorTarget(parentTo);
		}
		else
		{
			parentTo = GetDefaultInsertParent(instance);
		}

		instance.Name = string.IsNullOrWhiteSpace(args.Name)
			? className
			: args.Name.Trim();

		string? createdScriptFile = null;
		string propertyResult;
		try
		{
			if (instance is BrickVerse.Datamodel.Script initialScript && args.Source != null)
				initialScript.Source = args.Source;
			propertyResult = ApplyProperties(instance, args.Properties);
			createdScriptFile = instance is BrickVerse.Datamodel.Script script
				? CreateAndLinkScriptFile(script)
				: null;
			instance.CreatorInserted();
			_root.CreatorContext.History.CreateInstances([instance], parentTo);
		}
		catch
		{
			instance.Delete();
			if (createdScriptFile != null && _root.LinkedSession != null && File.Exists(_root.LinkedSession.GlobalizePath(createdScriptFile)))
				_root.LinkedSession.RemoveFile(createdScriptFile);
			throw;
		}
		_root.CreatorContext.Selections.SelectOnly(instance);

		string createdPath = instance.LuaPath;
		if (!IsUserAccessibleTarget(instance))
		{
			throw new InvalidOperationException(
				$"Creator inserted '{className}' at inaccessible path '{createdPath}'."
			);
		}

		string changeId = Guid.NewGuid().ToString("N");
		_rollback.Add((changeId, () =>
		{
			Instance? created = ResolveInstance(createdPath) ?? instance;
			created?.Delete();
			if (
				createdScriptFile != null
				&& _root.LinkedSession != null
				&& File.Exists(_root.LinkedSession.GlobalizePath(createdScriptFile))
			)
			{
				_root.LinkedSession.RemoveFile(createdScriptFile);
			}
		}));

		LastEvent = new ForgeToolEvent
		{
			ChangeId = changeId,
			ToolName = "create_instance",
			Title = $"{instance.ClassName} created",
			Detail = createdPath,
			InstancePath = createdPath,
			CanRollback = true,
		};

		StringBuilder result = new();
		result.AppendLine($"Created {instance.ClassName} at {createdPath}.");
		result.AppendLine($"Parent: {parentTo.LuaPath}");
		if (createdScriptFile != null)
		{
			result.AppendLine($"Linked project file: {createdScriptFile}");
		}
		result.Append("The created instance is visible and selected in Inspector.");
		if (!string.IsNullOrWhiteSpace(propertyResult))
		{
			result.AppendLine();
			result.Append(propertyResult);
		}

		return result.ToString();
	}

	private string CreateAndLinkScriptFile(BrickVerse.Datamodel.Script script)
	{
		if (_root.LinkedSession == null)
		{
			throw new InvalidOperationException(
				"Cannot create a filesystem-backed script without an open Creator project."
			);
		}

		string folder;
		string suffix;
		switch (script)
		{
			case ServerScript:
				folder = "server";
				suffix = ".server";
				break;
			case ClientScript:
				folder = "client";
				suffix = ".client";
				break;
			default:
				folder = "modules";
				suffix = string.Empty;
				break;
		}

		string safeName = SanitizeFileName(script.Name);
		string relativePath = $"{folder}/{safeName}{suffix}.luau";
		int copyNumber = 2;
		while (File.Exists(_root.LinkedSession.GlobalizePath(relativePath)))
		{
			relativePath = $"{folder}/{safeName}{copyNumber}{suffix}.luau";
			copyNumber++;
		}

		string source = script.Source;
		try
		{
			_root.IO.WriteTextToPath(relativePath, source);
			script.LinkedScript = _root.Assets.GetFileLinkByPath(relativePath);
			return relativePath;
		}
		catch
		{
			if (File.Exists(_root.LinkedSession.GlobalizePath(relativePath)))
				_root.LinkedSession.RemoveFile(relativePath);
			throw;
		}
	}

	private string ManageProjectFile(string argumentsJson)
	{
		ForgeProjectFileArgs args = JsonSerializer.Deserialize(
			argumentsJson,
			ForgeJsonContext.Default.ForgeProjectFileArgs
		) ?? throw new InvalidOperationException("Missing manage_project_file arguments.");
		if (_root.LinkedSession == null)
			throw new InvalidOperationException("Open a linked Creator project first.");
		string action = args.Action.Trim().ToLowerInvariant();
		if (action == "list")
			return string.Join("\n", _root.IO.ListProjectFiles().Take(1000));
		string path = (args.Path ?? string.Empty).Trim().Replace('\\', '/');
		if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.StartsWith("../") || path.Contains("/../") || path == "..")
			throw new InvalidOperationException("A project-relative path is required.");
		string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
		if (extension is not ("lua" or "luau" or "json" or "txt"))
			throw new InvalidOperationException("Forge project-file editing is limited to .lua, .luau, .json, and .txt text files.");
		return action switch
		{
			"read" => _root.IO.ReadTextFromPath(path) ?? throw new FileNotFoundException("Project file not found.", path),
			"write" => WriteProjectFile(path, args.Content ?? string.Empty),
			"delete" => DeleteProjectFile(path),
			_ => throw new InvalidOperationException("action must be list, read, write, or delete."),
		};
	}

	private string WriteProjectFile(string path, string content)
	{
		string absolutePath = _root.LinkedSession!.GlobalizePath(path);
		bool existed = File.Exists(absolutePath);
		string before = existed ? File.ReadAllText(absolutePath) : string.Empty;
		_root.IO.WriteTextToPath(path, content);
		string changeId = Guid.NewGuid().ToString("N");
		_rollback.Add((changeId, () =>
		{
			if (existed) _root.IO.WriteTextToPath(path, before);
			else if (File.Exists(absolutePath)) _root.LinkedSession.RemoveFile(path, toRecycleBin: true);
		}));
		LastEvent = new ForgeToolEvent
		{
			ChangeId = changeId,
			ToolName = "manage_project_file",
			Title = existed ? "Project file updated" : "Project file created",
			Detail = path,
			InstancePath = path,
			Diff = BuildUnifiedDiff(before, content),
			CanRollback = true,
		};
		return $"Wrote project file {path} ({content.Length} characters).";
	}

	private string DeleteProjectFile(string path)
	{
		string absolutePath = _root.LinkedSession!.GlobalizePath(path);
		if (!File.Exists(absolutePath))
			throw new FileNotFoundException("Project file not found.", path);
		string before = File.ReadAllText(absolutePath);
		_root.LinkedSession.RemoveFile(path, toRecycleBin: true);
		string changeId = Guid.NewGuid().ToString("N");
		_rollback.Add((changeId, () => _root.IO.WriteTextToPath(path, before)));
		LastEvent = new ForgeToolEvent
		{
			ChangeId = changeId,
			ToolName = "manage_project_file",
			Title = "Project file deleted",
			Detail = path,
			InstancePath = path,
			Diff = BuildUnifiedDiff(before, string.Empty),
			CanRollback = true,
		};
		return $"Moved project file {path} to the recycle bin.";
	}

	private static string SanitizeFileName(string name)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string safeName = new(
			name.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray()
		);
		return string.IsNullOrWhiteSpace(safeName) ? "ForgeScript" : safeName;
	}

	private Instance GetDefaultInsertParent(Instance instance)
	{
		switch (instance)
		{
			case Part:
				return _root.Environment;

			case Light:
				return _root.Lighting;

			case GUI:
				return _root.PlayerGUI;

			case UIField when instance is not GUI:
				{
					GUI? existingUi = (GUI?)_root.PlayerGUI.FindChild("GUI");
					if (existingUi != null)
					{
						return existingUi;
					}

					GUI gui = _root.New<GUI>();
					gui.Name = "GUI";
					gui.CreatorInserted();
					_root.CreatorContext.History.CreateInstances([gui], _root.PlayerGUI);
					return gui;
				}

			case BrickVerse.Datamodel.Script:
				return _root.ScriptService;

			default:
				return _root.Environment;
		}
	}

	private string SetInstanceProperties(string argumentsJson)
	{
		ForgeSetInstancePropertiesArgs args =
			JsonSerializer.Deserialize(
				argumentsJson,
				ForgeJsonContext.Default.ForgeSetInstancePropertiesArgs
			) ?? throw new InvalidOperationException("Missing set_instance_properties arguments.");

		Instance instance =
			ResolveInstance(args.Path)
			?? throw new InvalidOperationException($"Could not resolve path '{args.Path}'.");
		EnsureVisibleCreatorTarget(instance);
		string result = ApplyProperties(instance, args.Properties);
		return string.IsNullOrWhiteSpace(result)
			? $"No writable properties were changed on {instance.LuaPath}."
			: $"Updated {instance.LuaPath}.\n{result}";
	}

	private string DeleteInstance(string argumentsJson)
	{
		ForgeDeleteInstanceArgs args =
			JsonSerializer.Deserialize(
				argumentsJson,
				ForgeJsonContext.Default.ForgeDeleteInstanceArgs
			) ?? throw new InvalidOperationException("Missing delete_instance arguments.");

		Instance instance =
			ResolveInstance(args.Path)
			?? throw new InvalidOperationException($"Could not resolve path '{args.Path}'.");
		EnsureVisibleCreatorTarget(instance);
		if (instance == _root)
		{
			throw new InvalidOperationException("The world root cannot be deleted.");
		}

		if (instance.GetType().IsDefined(typeof(StaticAttribute), true))
		{
			throw new InvalidOperationException(
				$"{instance.ClassName} is static and cannot be deleted."
			);
		}

		string path = instance.LuaPath;
		string? linkedFile = (instance as BrickVerse.Datamodel.Script)?.LinkedScript?.LinkedPath;
		instance.Delete();
		if (linkedFile != null && _root.LinkedSession != null && File.Exists(_root.LinkedSession.GlobalizePath(linkedFile)))
			_root.LinkedSession.RemoveFile(linkedFile, toRecycleBin: true);
		return linkedFile == null
			? $"Deleted {path}."
			: $"Deleted {path} and moved its linked project file {linkedFile} to the recycle bin.";
	}

	[RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	[RequiresDynamicCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	private string EditScriptSource(string argumentsJson)
	{
		ForgeEditScriptSourceArgs args =
			JsonSerializer.Deserialize<ForgeEditScriptSourceArgs>(argumentsJson)
			?? throw new InvalidOperationException("Missing edit_script_source arguments.");
		Instance instance =
			ResolveInstance(args.Path)
			?? ResolveScriptByLinkedPath(args.Path)
			?? throw new InvalidOperationException($"Could not resolve script instance or linked project file '{args.Path}'.");
		EnsureVisibleCreatorTarget(instance);
		if (instance is not BrickVerse.Datamodel.Script script)
			throw new InvalidOperationException($"'{args.Path}' is not a Script.");

		string before = script.Source;
		string after = args.Source ?? string.Empty;
		if (before == after)
			return $"No script changes were needed for {script.LuaPath}.";
		WriteScriptSource(script, after);
		string path = script.LuaPath;
		_scriptDiffs[path] = (before, after);
		string changeId = Guid.NewGuid().ToString("N");
		_rollback.Add((changeId, () =>
		{
			if (ResolveInstance(path) is BrickVerse.Datamodel.Script current)
				WriteScriptSource(current, before);
		}));
		_root.CreatorContext.Selections.SelectOnly(script);
		LastEvent = new ForgeToolEvent
		{
			ChangeId = changeId,
			ToolName = "edit_script_source",
			Title = "Script updated",
			Detail = path,
			InstancePath = path,
			Diff = BuildUnifiedDiff(before, after),
			CanRollback = true,
		};
		string fileDetail = script.LinkedScript?.LinkedPath is string linkedPath
			? $" Wrote project file {linkedPath}."
			: string.Empty;
		return $"Updated script source at {path}.{fileDetail} The script is selected and a diff/rollback is available. Do not repeat the full source in chat.";
	}

	private void WriteScriptSource(BrickVerse.Datamodel.Script script, string source)
	{
		if (script.LinkedScript?.LinkedPath is string linkedPath)
		{
			_root.IO.WriteTextToPath(linkedPath, source);
			string absolutePath = _root.LinkedSession?.GlobalizePath(linkedPath) ?? linkedPath;
			TextEditorRoot.NotifyExternalFileChanged(absolutePath);
			return;
		}

		script.Source = source;
	}

	[RequiresDynamicCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	private string GetScriptDiff(string argumentsJson)
	{
		ForgeScriptDiffArgs args =
			JsonSerializer.Deserialize<ForgeScriptDiffArgs>(argumentsJson)
			?? throw new InvalidOperationException("Missing get_script_diff arguments.");
		if (!_scriptDiffs.TryGetValue(args.Path, out var change))
			return $"No Forge script diff is available for {args.Path}.";
		string diff = BuildUnifiedDiff(change.Before, change.After);
		LastEvent = new ForgeToolEvent
		{
			ToolName = "get_script_diff",
			Title = "Script diff",
			Detail = args.Path,
			InstancePath = args.Path,
			Diff = diff,
		};
		return diff;
	}

	[RequiresDynamicCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	private string RollbackLastChange(string argumentsJson)
	{
		if (_rollback.Count == 0)
			return "There are no Forge changes to roll back in this request.";
		ForgeRollbackArgs args = JsonSerializer.Deserialize<ForgeRollbackArgs>(argumentsJson) ?? new();
		int index = string.IsNullOrWhiteSpace(args.ChangeId)
			? _rollback.Count - 1
			: _rollback.FindLastIndex(change => change.Id == args.ChangeId);
		if (index < 0)
			throw new InvalidOperationException("That Forge change is no longer available to roll back in this Creator session.");
		var change = _rollback[index];
		change.Rollback();
		_rollback.RemoveAt(index);
		LastEvent = new ForgeToolEvent
		{
			ChangeId = change.Id,
			ToolName = "rollback_last_change",
			Title = "Change rolled back",
			Detail = "Restored the previous state.",
		};
		return "Rolled back the most recent Forge change.";
	}

	private static string BuildUnifiedDiff(string before, string after)
	{
		string[] oldLines = before.Replace("\r\n", "\n").Split('\n');
		string[] newLines = after.Replace("\r\n", "\n").Split('\n');
		StringBuilder diff = new();
		diff.AppendLine("--- before");
		diff.AppendLine("+++ after");
		if ((long)oldLines.Length * newLines.Length > 1_000_000)
		{
			foreach (string line in oldLines) diff.AppendLine("-" + line);
			foreach (string line in newLines) diff.AppendLine("+" + line);
			return diff.ToString().TrimEnd();
		}
		int[,] lcs = new int[oldLines.Length + 1, newLines.Length + 1];
		for (int oldIndex = oldLines.Length - 1; oldIndex >= 0; oldIndex--)
			for (int newIndex = newLines.Length - 1; newIndex >= 0; newIndex--)
				lcs[oldIndex, newIndex] = oldLines[oldIndex] == newLines[newIndex]
					? lcs[oldIndex + 1, newIndex + 1] + 1
					: Math.Max(lcs[oldIndex + 1, newIndex], lcs[oldIndex, newIndex + 1]);
		int i = 0;
		int j = 0;
		while (i < oldLines.Length || j < newLines.Length)
		{
			if (i < oldLines.Length && j < newLines.Length && oldLines[i] == newLines[j])
			{
				diff.AppendLine(" " + oldLines[i]);
				i++;
				j++;
			}
			else if (j < newLines.Length && (i == oldLines.Length || lcs[i, j + 1] >= lcs[i + 1, j]))
				diff.AppendLine("+" + newLines[j++]);
			else
				diff.AppendLine("-" + oldLines[i++]);
		}
		return diff.ToString().TrimEnd();
	}

	private static void WriteTree(
		StringBuilder builder,
		Instance instance,
		int depth,
		int maxDepth,
		int maxNodes,
		ref int written
	)
	{
		if (written >= maxNodes || depth > maxDepth)
		{
			return;
		}

		builder.Append(' ', depth * 2);
		builder.AppendLine($"- {instance.Name} [{instance.ClassName}]");
		written++;

		if (depth == maxDepth)
		{
			return;
		}

		foreach (Instance child in instance.GetChildren())
		{
			WriteTree(builder, child, depth + 1, maxDepth, maxNodes, ref written);
			if (written >= maxNodes)
			{
				return;
			}
		}
	}

	private Instance? ResolveInstance(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		string normalized = path.Trim().Replace('\\', '/').Trim('/');
		if (
			string.Equals(normalized, "world", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalized, "root", StringComparison.OrdinalIgnoreCase)
		)
		{
			return _root;
		}

		if (
			normalized.StartsWith("world.", StringComparison.OrdinalIgnoreCase)
			|| normalized.StartsWith("world/", StringComparison.OrdinalIgnoreCase)
		)
		{
			normalized = normalized[6..];
		}
		else if (
			normalized.StartsWith("root.", StringComparison.OrdinalIgnoreCase)
			|| normalized.StartsWith("root/", StringComparison.OrdinalIgnoreCase)
		)
		{
			normalized = normalized[5..];
		}

		// LuaPath is dot-delimited, but LLMs and external MCP clients commonly
		// provide filesystem/Godot-style paths. Normalize both to the same form.
		normalized = string.Join(
			'.',
			normalized.Split(['.', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
		);
		if (string.IsNullOrWhiteSpace(normalized))
			return _root;

		return _root.FindDescendant(normalized);
	}

	private BrickVerse.Datamodel.Script? ResolveScriptByLinkedPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;
		string normalized = path.Trim().Replace('\\', '/');
		return _root.GetDescendants()
			.OfType<BrickVerse.Datamodel.Script>()
			.FirstOrDefault(script => string.Equals(
				script.LinkedScript?.LinkedPath?.Replace('\\', '/'),
				normalized,
				StringComparison.OrdinalIgnoreCase
			));
	}

	private static string FormatInstanceSummary(Instance instance)
	{
		string parentPath = instance.Parent?.LuaPath ?? "(none)";
		return $"{instance.LuaPath} [{instance.ClassName}] parent={parentPath}";
	}

	private static string FormatVector3(Vector3 value)
	{
		return $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###})";
	}

	private static string ApplyProperties(Instance instance, JsonElement propertiesElement)
	{
		if (propertiesElement.ValueKind != JsonValueKind.Object)
		{
			return string.Empty;
		}

		List<string> applied = [];
		List<string> failed = [];

		foreach (JsonProperty property in propertiesElement.EnumerateObject())
		{
			PropertyInfo? targetProperty = instance
				.GetType()
				.GetProperty(
					property.Name,
					BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
				);
			if (targetProperty == null || !targetProperty.CanWrite)
			{
				failed.Add($"- {property.Name}: property not found or not writable");
				continue;
			}

			try
			{
				object? converted = ConvertValue(property.Value, targetProperty.PropertyType);
				targetProperty.SetValue(instance, converted);
				applied.Add($"- {targetProperty.Name} = {property.Value.GetRawText()}");
			}
			catch (Exception ex)
			{
				failed.Add($"- {property.Name}: {ex.Message}");
			}
		}

		StringBuilder builder = new();
		if (applied.Count > 0)
		{
			builder.AppendLine("Applied:");
			builder.AppendLine(string.Join("\n", applied));
		}

		if (failed.Count > 0)
		{
			builder.AppendLine("Warnings:");
			builder.AppendLine(string.Join("\n", failed));
		}

		return builder.ToString().TrimEnd();
	}

	private static object? ConvertValue(JsonElement element, Type targetType)
	{
		Type? nullableTarget = Nullable.GetUnderlyingType(targetType);
		if (nullableTarget != null)
		{
			if (element.ValueKind == JsonValueKind.Null)
			{
				return null;
			}

			targetType = nullableTarget;
		}

		if (targetType == typeof(string))
		{
			return element.GetString() ?? string.Empty;
		}

		if (targetType == typeof(bool))
		{
			return element.GetBoolean();
		}

		if (targetType == typeof(int))
		{
			return element.GetInt32();
		}

		if (targetType == typeof(float))
		{
			return element.GetSingle();
		}

		if (targetType == typeof(double))
		{
			return element.GetDouble();
		}

		if (targetType == typeof(string[]))
		{
			return element
				.EnumerateArray()
				.Select(static item => item.GetString() ?? string.Empty)
				.ToArray();
		}

		if (targetType == typeof(Vector3))
		{
			return ConvertVector3(element);
		}

		if (targetType == typeof(Vector2))
		{
			return ConvertVector2(element);
		}

		if (targetType == typeof(Color))
		{
			return ConvertColor(element);
		}

		if (targetType.IsEnum)
		{
			return element.ValueKind switch
			{
				JsonValueKind.String => Enum.Parse(
					targetType,
					element.GetString() ?? string.Empty,
					true
				),
				_ => Enum.ToObject(targetType, element.GetInt32()),
			};
		}

		throw new InvalidOperationException($"Unsupported property type '{targetType.Name}'.");
	}

	private static Vector3 ConvertVector3(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Array)
		{
			float[] values = element
				.EnumerateArray()
				.Select(static item => item.GetSingle())
				.ToArray();
			if (values.Length != 3)
			{
				throw new InvalidOperationException("Vector3 arrays must have exactly 3 numbers.");
			}

			return new Vector3(values[0], values[1], values[2]);
		}

		if (element.ValueKind == JsonValueKind.Object)
		{
			return new Vector3(
				ReadFloat(element, "x"),
				ReadFloat(element, "y"),
				ReadFloat(element, "z")
			);
		}

		throw new InvalidOperationException("Vector3 values must be an array or object.");
	}

	private static Vector2 ConvertVector2(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Array)
		{
			float[] values = element
				.EnumerateArray()
				.Select(static item => item.GetSingle())
				.ToArray();
			if (values.Length != 2)
			{
				throw new InvalidOperationException("Vector2 arrays must have exactly 2 numbers.");
			}

			return new Vector2(values[0], values[1]);
		}

		if (element.ValueKind == JsonValueKind.Object)
		{
			return new Vector2(ReadFloat(element, "x"), ReadFloat(element, "y"));
		}

		throw new InvalidOperationException("Vector2 values must be an array or object.");
	}

	private static Color ConvertColor(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.String)
		{
			return Color.FromString(element.GetString() ?? string.Empty, Colors.White);
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			float[] values = element
				.EnumerateArray()
				.Select(static item => item.GetSingle())
				.ToArray();
			return values.Length switch
			{
				3 => new Color(values[0], values[1], values[2]),
				4 => new Color(values[0], values[1], values[2], values[3]),
				_ => throw new InvalidOperationException("Color arrays must have 3 or 4 numbers."),
			};
		}

		if (element.ValueKind == JsonValueKind.Object)
		{
			return new Color(
				ReadFloat(element, "r"),
				ReadFloat(element, "g"),
				ReadFloat(element, "b"),
				TryReadFloat(element, "a") ?? 1f
			);
		}

		throw new InvalidOperationException("Color values must be a string, array, or object.");
	}

	private static float ReadFloat(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out JsonElement property))
		{
			throw new InvalidOperationException($"Missing '{propertyName}'.");
		}

		return property.GetSingle();
	}

	private static float? TryReadFloat(JsonElement element, string propertyName)
	{
		return element.TryGetProperty(propertyName, out JsonElement property)
			? property.GetSingle()
			: null;
	}
}

internal static class ForgeStringExtensions
{
	public static string Or(this string value, string fallback)
	{
		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}
}
