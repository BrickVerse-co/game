// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Script = BrickVerse.Datamodel.Script;

namespace BrickVerse.Scripting;

public interface IScriptLanguageProvider : IDisposable
{
	void Run(Script script);
	void Close(Script script);
	byte[] CompileSource(string source);
	Task CallAsync(Script script, string funcName, object?[]? args);
	void CallUpdate(Script script, double delta);
	void CallFixedUpdate(Script script, double delta);
	void FreeBVCallback(BVCallback callback);
}

public enum ScriptLanguagesEnum
{
	// Values are serialized into world files. Never reorder or reuse them.
	Luau = 0,
	CSharp = 1,
	JavaScript = 2,
	TypeScript = 3,
	Cpp = 4,
}

/// <summary>
/// Canonical metadata for every script language understood by the runtime and Creator.
/// Keeping this in the shared assembly prevents extension and filename handling from
/// drifting between the editor, packer, and runtime.
/// </summary>
public sealed record ScriptLanguageDefinition(
	ScriptLanguagesEnum Language,
	string DisplayName,
	string PrimaryExtension,
	string[] Extensions,
	bool RequiresCompilation,
	string EmptyModuleSource,
	ScriptPlatformSupport PlatformSupport
);

[Flags]
public enum ScriptPlatformSupport
{
	None = 0,
	Desktop = 1 << 0,
	DedicatedServer = 1 << 1,
	Mobile = 1 << 2,
	All = Desktop | DedicatedServer | Mobile,
}

public static class ScriptLanguageRegistry
{
	private static readonly ScriptLanguageDefinition[] _definitions =
	[
		new(ScriptLanguagesEnum.Luau, "Luau", "luau", ["luau", "lua"], true,
			"local module = {}\n\nreturn module", ScriptPlatformSupport.All),
		new(ScriptLanguagesEnum.CSharp, "C#", "cs", ["cs"], true,
			"using BrickVerse.Scripting.Managed;\n\npublic sealed class Module : BrickVerseScript\n{\n}\n",
			ScriptPlatformSupport.Desktop | ScriptPlatformSupport.DedicatedServer),
		new(ScriptLanguagesEnum.JavaScript, "JavaScript", "js", ["js", "mjs"], false,
			"const module = {};\n",
			ScriptPlatformSupport.Desktop | ScriptPlatformSupport.DedicatedServer),
		new(ScriptLanguagesEnum.TypeScript, "TypeScript", "ts", ["ts", "tsx"], true,
			"const module: Record<string, unknown> = {};\n",
			ScriptPlatformSupport.Desktop | ScriptPlatformSupport.DedicatedServer),
		new(ScriptLanguagesEnum.Cpp, "C++ (WebAssembly)", "cpp", ["cpp", "cc", "cxx"], true,
			"#include <brickverse.h>\n\nBV_EXPORT(\"bv_start\") void start() {}\nBV_EXPORT(\"bv_update\") void update(double delta) {}\nBV_EXPORT(\"bv_fixed_update\") void fixed_update(double delta) {}\n",
			ScriptPlatformSupport.Desktop | ScriptPlatformSupport.DedicatedServer),
	];

	private static readonly Dictionary<ScriptLanguagesEnum, ScriptLanguageDefinition> _byLanguage =
		_definitions.ToDictionary(static definition => definition.Language);

	private static readonly Dictionary<string, ScriptLanguageDefinition> _byExtension =
		_definitions
			.SelectMany(static definition => definition.Extensions.Select(extension => (extension, definition)))
			.ToDictionary(static pair => pair.extension, static pair => pair.definition, StringComparer.OrdinalIgnoreCase);

	public static IReadOnlyList<ScriptLanguageDefinition> Definitions => _definitions;

	public static ScriptLanguageDefinition Get(ScriptLanguagesEnum language) =>
		_byLanguage.TryGetValue(language, out ScriptLanguageDefinition? definition)
			? definition
			: throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown script language");

	public static bool TryFromPath(string path, out ScriptLanguageDefinition definition)
	{
		string extension = Path.GetExtension(path).TrimStart('.');
		return _byExtension.TryGetValue(extension, out definition!);
	}

	public static ScriptLanguagesEnum FromPath(string path) =>
		TryFromPath(path, out ScriptLanguageDefinition definition)
			? definition.Language
			: throw new NotSupportedException($"'{Path.GetExtension(path)}' is not a supported script extension");

	public static bool IsScriptPath(string path) => TryFromPath(path, out _);

	public static bool IsSupportedOnCurrentPlatform(ScriptLanguagesEnum language)
	{
		ScriptPlatformSupport current = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
			? ScriptPlatformSupport.Mobile
			: ScriptPlatformSupport.Desktop;
		return Get(language).PlatformSupport.HasFlag(current);
	}

	public static string GetPlatformDescription(ScriptLanguageDefinition language) =>
		language.PlatformSupport == ScriptPlatformSupport.All
			? "All supported platforms"
			: "Creator, desktop, and dedicated server only; not supported on Android or iOS";

	public static string CreateFileName(string name, ScriptTypeKind type, ScriptLanguagesEnum language)
	{
		string role = type switch
		{
			ScriptTypeKind.Server => ".server",
			ScriptTypeKind.Client => ".client",
			_ => "",
		};
		return $"{name}{role}.{Get(language).PrimaryExtension}";
	}

	public static string CreateStarterSource(ScriptLanguagesEnum language, ScriptTypeKind type)
	{
		ScriptLanguageDefinition definition = Get(language);
		if (type == ScriptTypeKind.Module) return definition.EmptyModuleSource;
		return language switch
		{
			ScriptLanguagesEnum.CSharp => "using BrickVerse.Scripting.Managed;\n\npublic sealed class Main : BrickVerseScript\n{\n\tpublic override void Start()\n\t{\n\t\tPlayers players = Game.GetService<Players>(\"Players\");\n\t\tplayers.PlayerAdded.Connect(player => Print(player.Name));\n\t}\n}\n",
			ScriptLanguagesEnum.JavaScript => "const Players = game.GetService(\"Players\");\n\nPlayers.PlayerAdded.Connect(player => {\n    console.log(player.Name);\n});\n",
			ScriptLanguagesEnum.TypeScript => "const Players = game.GetService<Players>(\"Players\");\n\nPlayers.PlayerAdded.Connect((player: Player) => {\n    console.log(player.Name);\n});\n",
			ScriptLanguagesEnum.Cpp => "#include <brickverse.h>\n\nBV_EXPORT(\"bv_start\") void start()\n{\n    auto players = Game::GetService<Players>(\"Players\");\n    players.PlayerAdded([](Player player) {\n        BV::Print(player.Name());\n    });\n}\n",
			_ => "",
		};
	}
}

// Shared counterpart of Creator's ScriptTypeEnum. The runtime assembly cannot
// depend on Creator-only types because those are stripped from exported builds.
public enum ScriptTypeKind
{
	Module,
	Server,
	Client,
}
