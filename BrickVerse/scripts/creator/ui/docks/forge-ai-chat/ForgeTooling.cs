// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using BrickVerse.Attributes;
using BrickVerse.Creator.UI.TextEditor;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;

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
            "Create a new instantiable instance under a parent path and optionally set common properties.",
            """
			{
			  "type": "object",
			  "properties": {
			    "class_name": { "type": "string" },
			    "parent_path": { "type": "string" },
			    "name": { "type": "string" },
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
            "Rollback the most recent Forge change in this request.",
            """
			{
			  "type": "object",
			  "properties": {},
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
    private readonly Stack<Action> _rollback = new();
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
            "list_instantiable_classes" => ListInstantiableClasses(),
            "search_instances" => SearchInstances(argumentsJson),
            "inspect_instance" => InspectInstance(argumentsJson),
            "select_instances" => SelectInstances(argumentsJson),
            "create_instance" => CreateInstance(argumentsJson),
            "set_instance_properties" => SetInstanceProperties(argumentsJson),
            "delete_instance" => DeleteInstance(argumentsJson),
            "edit_script_source" => EditScriptSource(argumentsJson),
            "get_script_diff" => GetScriptDiff(argumentsJson),
            "rollback_last_change" => RollbackLastChange(),
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
        _root.CreatorContext.Selections.SelectOnly(script);
        _root.ScriptService.Run(script);
        return $"Ran confirmed Luau as {script.LuaPath}. The generated ClientScript remains visible and selected in Inspector for review or deletion.";
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
            ?? throw new InvalidOperationException($"Could not resolve path '{args.Path}'.");
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

        instance.CreatorInserted();
        _root.CreatorContext.History.CreateInstances([instance], parentTo);

        string propertyResult = ApplyProperties(instance, args.Properties);
        _root.CreatorContext.Selections.SelectOnly(instance);

        string createdPath = instance.LuaPath;
        if (!IsUserAccessibleTarget(instance))
        {
            throw new InvalidOperationException(
                $"Creator inserted '{className}' at inaccessible path '{createdPath}'."
            );
        }

        _rollback.Push(() =>
        {
            Instance? created = ResolveInstance(createdPath) ?? instance;
            created?.Delete();
        });

        LastEvent = new ForgeToolEvent
        {
            ToolName = "create_instance",
            Title = $"{instance.ClassName} created",
            Detail = createdPath,
            InstancePath = createdPath,
            CanRollback = true,
        };

        StringBuilder result = new();
        result.AppendLine($"Created {instance.ClassName} at {createdPath}.");
        result.AppendLine($"Parent: {parentTo.LuaPath}");
        result.Append("The created instance is visible and selected in Inspector.");
        if (!string.IsNullOrWhiteSpace(propertyResult))
        {
            result.AppendLine();
            result.Append(propertyResult);
        }

        return result.ToString();
    }

    private Instance GetDefaultInsertParent(Instance instance)
    {
        switch (instance)
        {
            case Part:
                return _root.Environment;

            case Light:
                return _root.Lighting;

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
        instance.Delete();
        return $"Deleted {path}.";
    }

    private string EditScriptSource(string argumentsJson)
    {
        ForgeEditScriptSourceArgs args =
            JsonSerializer.Deserialize<ForgeEditScriptSourceArgs>(argumentsJson)
            ?? throw new InvalidOperationException("Missing edit_script_source arguments.");
        Instance instance =
            ResolveInstance(args.Path)
            ?? throw new InvalidOperationException($"Could not resolve path '{args.Path}'.");
        EnsureVisibleCreatorTarget(instance);
        if (instance is not BrickVerse.Datamodel.Script script)
            throw new InvalidOperationException($"'{args.Path}' is not a Script.");

        string before = script.Source;
        string after = args.Source ?? string.Empty;
        if (before == after)
            return $"No script changes were needed for {script.LuaPath}.";
        script.Source = after;
        string path = script.LuaPath;
        _scriptDiffs[path] = (before, after);
        _rollback.Push(() =>
        {
            if (ResolveInstance(path) is BrickVerse.Datamodel.Script current)
                current.Source = before;
        });
        _root.CreatorContext.Selections.SelectOnly(script);
        LastEvent = new ForgeToolEvent
        {
            ToolName = "edit_script_source",
            Title = "Script updated",
            Detail = path,
            InstancePath = path,
            Diff = BuildUnifiedDiff(before, after),
            CanRollback = true,
        };
        return $"Updated script source at {path}. The script is selected and a diff/rollback is available. Do not repeat the full source in chat.";
    }

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

    private string RollbackLastChange()
    {
        if (_rollback.Count == 0)
            return "There are no Forge changes to roll back in this request.";
        _rollback.Pop().Invoke();
        LastEvent = new ForgeToolEvent
        {
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
        int count = Math.Max(oldLines.Length, newLines.Length);
        for (int i = 0; i < count; i++)
        {
            string? oldLine = i < oldLines.Length ? oldLines[i] : null;
            string? newLine = i < newLines.Length ? newLines[i] : null;
            if (oldLine == newLine)
            {
                if (oldLine != null)
                    diff.AppendLine(" " + oldLine);
                continue;
            }
            if (oldLine != null)
                diff.AppendLine("-" + oldLine);
            if (newLine != null)
                diff.AppendLine("+" + newLine);
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

        string normalized = path.Trim();
        if (string.Equals(normalized, "world", StringComparison.OrdinalIgnoreCase))
        {
            return _root;
        }

        if (normalized.StartsWith("world.", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[6..];
        }

        return _root.FindDescendant(normalized);
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
