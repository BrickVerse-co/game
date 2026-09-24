// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using BrickVerse.Scripting.Managed;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Script = BrickVerse.Datamodel.Script;

namespace BrickVerse.Scripting.CSharp;

public sealed class CSharpProvider : IScriptLanguageProvider
{
	private readonly Dictionary<Script, ScriptState> _states = [];

	public byte[] CompileSource(string source)
	{
		if (source.Length > 262_144) throw new InvalidOperationException("C# script exceeds the 256 KiB source limit");
		SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14));
		ValidateSyntax(tree);
		SyntaxTree bindings = CSharpSyntaxTree.ParseText(CSharpBindingGenerator.Generate(), new CSharpParseOptions(LanguageVersion.CSharp14));
		CSharpCompilation compilation = CSharpCompilation.Create(
			"BrickVerseGuest_" + Guid.NewGuid().ToString("N"), [tree, bindings], GetReferences(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
				optimizationLevel: OptimizationLevel.Release, allowUnsafe: false,
				checkOverflow: true, concurrentBuild: false));
		using MemoryStream output = new();
		EmitResult result = compilation.Emit(output);
		if (!result.Success)
			throw new InvalidOperationException(string.Join(Environment.NewLine,
				result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
		ValidateSymbols(compilation, tree);
		return output.ToArray();
	}

	public void Run(Script script)
	{
		try
		{
			script.TryCompile();
			AssemblyLoadContext context = new("BrickVerseScript", isCollectible: true);
			using MemoryStream stream = new(script.Bytecode!, writable: false);
			Assembly assembly = context.LoadFromStream(stream);
			Type type = assembly.GetTypes().SingleOrDefault(static type =>
				type is { IsAbstract: false, IsPublic: true } && type.IsSubclassOf(typeof(BrickVerseScript)))
				?? throw new InvalidOperationException("C# script must contain exactly one public BrickVerseScript subclass");
			BrickVerseScript instance = (BrickVerseScript)Activator.CreateInstance(type)!;
			instance.Attach(new ScriptCapabilityBridge(script));
			_states[script] = new(context, instance);
			instance.Start();
		}
		catch (Exception exception) { script.Root.ScriptService.Logger.LogError(script, Unwrap(exception), 0); }
	}

	public Task CallAsync(Script script, string funcName, object?[]? args)
	{
		Execute(script, state => state.Instance.Invoke(funcName, args ?? []));
		return Task.CompletedTask;
	}
	public void CallUpdate(Script script, double delta) => Execute(script, state => state.Instance.Update(delta));
	public void CallFixedUpdate(Script script, double delta) => Execute(script, state => state.Instance.FixedUpdate(delta));
	public void Close(Script script) { if (_states.Remove(script, out ScriptState? state)) state.Context.Unload(); }
	public void FreeBVCallback(BVCallback callback) { }
	public void Dispose() { foreach (Script script in _states.Keys.ToArray()) Close(script); }
	private void Execute(Script script, Action<ScriptState> action)
	{
		if (!_states.TryGetValue(script, out ScriptState? state)) return;
		try { action(state); }
		catch (Exception exception) { script.Root.ScriptService.Logger.LogError(script, Unwrap(exception), 0); }
	}

	private static IEnumerable<MetadataReference> GetReferences()
	{
		string[] allowed = ["System.Private.CoreLib.dll", "System.Runtime.dll", "System.Collections.dll", "netstandard.dll"];
		string[] platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator);
		foreach (string path in platform.Where(path => allowed.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
			yield return MetadataReference.CreateFromFile(path);
		yield return MetadataReference.CreateFromFile(typeof(BrickVerseScript).Assembly.Location);
	}

	private static void ValidateSyntax(SyntaxTree tree)
	{
		SyntaxNode root = tree.GetRoot();
		if (root.DescendantNodes().Any(static node => node is UnsafeStatementSyntax
			or PointerTypeSyntax or FunctionPointerTypeSyntax or StackAllocArrayCreationExpressionSyntax
			or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
			or GotoStatementSyntax or LockStatementSyntax or AwaitExpressionSyntax
			or LocalFunctionStatementSyntax
			or ObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax
			or ImplicitArrayCreationExpressionSyntax))
			throw new UnauthorizedAccessException("unsafe code, unbounded loops/allocations, threading, and dynamic delegates are disabled");
		foreach (AnonymousFunctionExpressionSyntax lambda in root.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
		{
			InvocationExpressionSyntax? invocation = lambda.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
			string target = invocation?.Expression switch
			{
				IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
				MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
				_ => "",
			};
			if (target != "Connect")
				throw new UnauthorizedAccessException("Callbacks are only permitted as arguments to the sandboxed Connect API");
		}
		foreach (UsingDirectiveSyntax directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
		{
			string name = directive.Name?.ToString() ?? "";
			if (name is not ("System" or "System.Collections.Generic" or "BrickVerse.Scripting.Managed"))
				throw new UnauthorizedAccessException($"Namespace '{name}' is not available to scripts");
		}
	}

	private static void ValidateSymbols(CSharpCompilation compilation, SyntaxTree tree)
	{
		SemanticModel model = compilation.GetSemanticModel(tree);
		HashSet<string> allowedSystemTypes = ["Object", "String", "Boolean", "Byte", "SByte", "Int16", "UInt16",
			"Int32", "UInt32", "Int64", "UInt64", "Single", "Double", "Decimal", "Char", "Math", "Convert",
			"Array", "Nullable<T>", "Action<T>", "List<T>", "Dictionary<TKey, TValue>", "HashSet<T>"];
		foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
		{
			ISymbol? symbol = model.GetSymbolInfo(node).Symbol;
			if (symbol is IMethodSymbol method && method.Name is "GetType" or "MemberwiseClone"
				or "GetMethod" or "GetProperty" or "InvokeMember")
				throw new UnauthorizedAccessException($"Method '{method.Name}' is outside the script sandbox");
			if (symbol is IMethodSymbol ownMethod
				&& SymbolEqualityComparer.Default.Equals(ownMethod.ContainingAssembly, compilation.Assembly)
				&& node is InvocationExpressionSyntax)
				throw new UnauthorizedAccessException("Calling script-defined methods is disabled to prevent unbounded recursion; use lifecycle overrides and Invoke");
			INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
			if (type == null || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)) continue;
			if (type.SpecialType != SpecialType.None) continue;
			if (type.ContainingNamespace.ToDisplayString() == "BrickVerse.Scripting.Managed"
				&& type.Name is nameof(BrickVerseScript) or nameof(IScriptApi) or nameof(ScriptObject)
					or nameof(GameApi) or nameof(ScriptSignal)) continue;
			if (type.ContainingNamespace.ToDisplayString().StartsWith("System", StringComparison.Ordinal)
				&& allowedSystemTypes.Contains(type.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) continue;
			throw new UnauthorizedAccessException($"Type or member '{symbol?.ToDisplayString() ?? type.ToDisplayString()}' is outside the script sandbox");
		}
	}

	private static string Unwrap(Exception exception) => exception.InnerException?.Message ?? exception.Message;
	private sealed record ScriptState(AssemblyLoadContext Context, BrickVerseScript Instance);
}
