// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Text;

namespace BrickVerse.Scripting.JavaScript;

/// <summary>Uses Node's non-executing type-stripper, then runs the resulting JS in Jint.</summary>
public sealed class TypeScriptProvider : JavaScriptProvider
{
	private const string CompilerProgram = "import{stripTypeScriptTypes as s}from'node:module';let d='';process.stdin.setEncoding('utf8');process.stdin.on('data',c=>d+=c);process.stdin.on('end',()=>process.stdout.write(s(d,{mode:'transform'})));";

	public override byte[] CompileSource(string source)
	{
		using Process process = new()
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = "node",
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			},
		};
		process.StartInfo.ArgumentList.Add("--input-type=module");
		process.StartInfo.ArgumentList.Add("--no-warnings");
		process.StartInfo.ArgumentList.Add("-e");
		process.StartInfo.ArgumentList.Add(CompilerProgram);
		try { process.Start(); }
		catch (Exception exception) { throw new InvalidOperationException("TypeScript requires Node.js 22.13 or newer in PATH", exception); }
		process.StandardInput.Write(source);
		process.StandardInput.Close();
		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		if (!process.WaitForExit(10_000)) { process.Kill(true); throw new TimeoutException("TypeScript compilation timed out"); }
		if (process.ExitCode != 0) throw new InvalidOperationException("TypeScript compilation failed: " + error.Trim());
		return Encoding.UTF8.GetBytes(output);
	}
}
