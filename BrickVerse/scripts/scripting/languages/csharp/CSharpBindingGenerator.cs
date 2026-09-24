// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BrickVerse.Attributes;
using BrickVerse.Scripting.Managed;

namespace BrickVerse.Scripting.CSharp;

internal static class CSharpBindingGenerator
{
	public static string Generate()
	{
		Type marker = typeof(IScriptObject);
		Type[] types = marker.Assembly.GetTypes()
			.Where(type => type.IsPublic && !type.IsGenericType && marker.IsAssignableFrom(type)
				&& type != typeof(BVSignal) && type != typeof(BVSignalConnection))
			.OrderBy(InheritanceDepth).ThenBy(type => type.FullName, StringComparer.Ordinal).ToArray();
		HashSet<Type> available = types.ToHashSet();
		StringBuilder source = new("#nullable enable\nnamespace BrickVerse.Scripting.Managed;\n");
		foreach (Type type in types) AppendType(source, type, available);
		return source.ToString();
	}

	private static void AppendType(StringBuilder source, Type type, HashSet<Type> available)
	{
		string name = SafeIdentifier(type.Name);
		Type? parent = type.BaseType;
		string baseName = parent != null && available.Contains(parent) ? SafeIdentifier(parent.Name) : nameof(ScriptObject);
		source.Append("public class ").Append(name).Append(" : ").Append(baseName).Append(" {\n")
			.Append("public ").Append(name).Append("(IScriptApi api,int handle):base(api,handle){}\n");
		foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
		{
			if (property.GetCustomAttribute<ScriptPropertyAttribute>() == null) continue;
			string propertyType = MapType(property.PropertyType);
			string propertyName = SafeIdentifier(property.Name);
			source.Append("public ").Append(propertyType).Append(' ').Append(propertyName).Append(" { get=>Read<")
				.Append(propertyType).Append(">(").Append(Literal(property.Name)).Append(")!;");
			if (property.CanWrite) source.Append(" set=>Write(").Append(Literal(property.Name)).Append(",value);");
			source.Append(" }\n");
		}
		foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
		{
			ScriptMethodAttribute? attribute = method.GetCustomAttribute<ScriptMethodAttribute>();
			if (attribute == null || method.IsGenericMethod || method.GetParameters().Any(p => p.ParameterType.IsByRef || p.GetCustomAttribute<ScriptingCallerAttribute>() != null)) continue;
			string exposedName = attribute.MethodName ?? method.Name;
			ParameterInfo[] parameters = method.GetParameters();
			if (parameters.Any(p => typeof(Delegate).IsAssignableFrom(p.ParameterType) || p.ParameterType == typeof(BVCallback))) continue;
			string returnType = MapType(method.ReturnType);
			source.Append("public ").Append(returnType).Append(' ').Append(SafeIdentifier(exposedName)).Append('(');
			for (int index = 0; index < parameters.Length; index++)
			{
				if (index > 0) source.Append(',');
				source.Append(MapType(parameters[index].ParameterType)).Append(' ').Append(SafeIdentifier(parameters[index].Name ?? $"arg{index}"));
			}
			source.Append("){ ");
			if (method.ReturnType == typeof(void)) source.Append("Invoke(");
			else source.Append("return Invoke<").Append(returnType).Append(">(");
			source.Append(Literal(exposedName));
			foreach (ParameterInfo parameter in parameters) source.Append(',').Append(SafeIdentifier(parameter.Name!));
			source.Append("); }\n");
		}
		source.Append("}\n");
	}

	private static string MapType(Type type)
	{
		Type raw = Nullable.GetUnderlyingType(type) ?? type;
		if (raw == typeof(void)) return "void";
		if (raw == typeof(string)) return "string";
		if (raw == typeof(bool)) return "bool";
		if (raw == typeof(int)) return "int";
		if (raw == typeof(long)) return "long";
		if (raw == typeof(float)) return "float";
		if (raw == typeof(double)) return "double";
		if (raw.IsGenericType && raw.GetGenericTypeDefinition().Name.StartsWith("BVSignal`", StringComparison.Ordinal))
			return $"ScriptSignal<{string.Join(",", raw.GetGenericArguments().Select(MapType))}>";
		if (raw == typeof(BVSignal)) return nameof(ScriptSignal);
		if (typeof(IScriptObject).IsAssignableFrom(raw)) return SafeIdentifier(raw.Name);
		if (raw.IsEnum) return "int";
		return "object";
	}

	private static int InheritanceDepth(Type type) { int depth = 0; for (Type? current = type.BaseType; current != null; current = current.BaseType) depth++; return depth; }
	private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
	private static string SafeIdentifier(string value) => value is "object" or "string" or "event" or "base" or "new" or "params" or "internal" ? "@" + value : value.Replace('`', '_');
}
