// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using BrickVerse.Scripting;
using BrickVerse.Scripting.CSharp;
using BrickVerse.Scripting.JavaScript;

namespace BrickVerse.Tests;

public sealed class ScriptLanguageProviderTests
{
	[Fact]
	public void Registry_RecognizesEveryLanguageExtension()
	{
		foreach (ScriptLanguageDefinition definition in ScriptLanguageRegistry.Definitions)
			foreach (string extension in definition.Extensions)
				Assert.Equal(definition.Language, ScriptLanguageRegistry.FromPath("script." + extension));
	}

	[Fact]
	public void CSharp_CompilesCapabilityOnlyScript()
	{
		using CSharpProvider provider = new();
		byte[] assembly = provider.CompileSource("""
using BrickVerse.Scripting.Managed;
public sealed class Main : BrickVerseScript
{
    public override void Update(double delta) { _ = Bv.Get(World, "Name"); }
}
""");
		Assert.NotEmpty(assembly);
	}

	[Fact]
	public void CSharp_RejectsFilesystemAccess()
	{
		using CSharpProvider provider = new();
		Assert.ThrowsAny<Exception>(() => provider.CompileSource("""
using BrickVerse.Scripting.Managed;
public sealed class Main : BrickVerseScript
{
    public override void Start() { System.IO.File.ReadAllText("secret"); }
}
"""));
	}

	[Fact]
	public void TypeScript_StripsTypesWithoutExecutingSource()
	{
		using TypeScriptProvider provider = new();
		string javascript = Encoding.UTF8.GetString(provider.CompileSource("const value: number = 4;"));
		Assert.Contains("const value", javascript);
		Assert.DoesNotContain(": number", javascript);
	}
}
