// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BrickVerse.Attributes;
using BrickVerse.Datamodel.Services;
using BrickVerse.Scripting.Managed;

namespace BrickVerse.Scripting.CSharp;

internal static class CSharpBindingGenerator
{
	public static string Generate()
	{
		Type marker = typeof(IScriptObject);
		Type[] types = marker.Assembly.GetTypes()
			.Where(type => (type.IsPublic || type.IsNestedPublic) && !type.IsGenericType && marker.IsAssignableFrom(type)
				&& type != typeof(BVSignal) && type != typeof(BVSignalConnection))
			.OrderBy(InheritanceDepth).ThenBy(type => type.FullName, StringComparer.Ordinal).ToArray();
		HashSet<Type> available = types.ToHashSet();
		StringBuilder source = new("#nullable enable\nnamespace BrickVerse.Scripting.Managed;\n");
		foreach ((string enumName, Type enumType) in ScriptService.EnumMap.OrderBy(pair => pair.Key, StringComparer.Ordinal))
			AppendEnum(source, enumName, enumType);
		foreach (Type type in types) AppendType(source, type, available);
		return source.ToString();
	}

	private static void AppendEnum(StringBuilder source, string exposedName, Type type)
	{
		if (!type.IsEnum) return;
		Type underlying = Enum.GetUnderlyingType(type);
		if (type.GetCustomAttribute<FlagsAttribute>() != null) source.Append("[System.Flags]\n");
		source.Append("public enum ").Append(SafeIdentifier(exposedName)).Append(" : ")
			.Append(MapEnumUnderlyingType(underlying))
			.Append(" {\n");
		foreach (string member in Enum.GetNames(type))
		{
			object value = Enum.Parse(type, member);
			source.Append(SafeIdentifier(member)).Append(" = ")
				.Append(underlying == typeof(ulong)
					? Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL"
					: Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture))
				.Append(",\n");
		}
		source.Append("}\n");
	}

	private static string MapEnumUnderlyingType(Type type) => type == typeof(byte) ? "byte"
		: type == typeof(sbyte) ? "sbyte" : type == typeof(short) ? "short"
		: type == typeof(ushort) ? "ushort" : type == typeof(uint) ? "uint"
		: type == typeof(long) ? "long" : type == typeof(ulong) ? "ulong" : "int";

	private static void AppendType(StringBuilder source, Type type, HashSet<Type> available)
	{
		string name = BindingName(type);
		Type? parent = type.BaseType;
		string baseName = parent != null && available.Contains(parent) ? BindingName(parent) : nameof(ScriptObject);
		source.Append("public class ").Append(name).Append(" : ").Append(baseName).Append(" {\n")
			.Append("public ").Append(name).Append("(IScriptApi api,int handle):base(api,handle){}\n");
		BindingFlags exposedFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
		if (ScriptService.GlobalDataMap.Values.Contains(type)) exposedFlags |= BindingFlags.Static;
		foreach (PropertyInfo property in type.GetProperties(exposedFlags))
		{
			if (property.GetCustomAttribute<ScriptPropertyAttribute>() == null) continue;
			string propertyType = MapType(property.PropertyType);
			string propertyName = SafeIdentifier(property.Name);
			source.Append("public ").Append(propertyType).Append(' ').Append(propertyName).Append(" { get=>Read<")
				.Append(propertyType).Append(">(").Append(Literal(property.Name)).Append(")!;");
			if (property.SetMethod?.IsPublic == true) source.Append(" set=>Write(").Append(Literal(property.Name)).Append(",value);");
			source.Append(" }\n");
		}
		foreach (MethodInfo method in type.GetMethods(exposedFlags))
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
				if (parameters[index].IsOptional) source.Append(" = default");
			}
			source.Append("){ ");
			if (returnType == "void") source.Append("Invoke(");
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
		if (raw.IsArray) return MapType(raw.GetElementType()!) + "[]";
		if (raw.IsGenericType && raw.GetGenericTypeDefinition().Name.StartsWith("BVSignal`", StringComparison.Ordinal))
			return $"ScriptSignal<{string.Join(",", raw.GetGenericArguments().Select(MapType))}>";
		if (typeof(System.Threading.Tasks.Task).IsAssignableFrom(raw))
			return raw.IsGenericType ? MapType(raw.GetGenericArguments()[0]) : "void";
		if (raw == typeof(BVSignal)) return nameof(ScriptSignal);
		if (typeof(IScriptObject).IsAssignableFrom(raw)) return BindingName(raw);
		if (raw.IsEnum)
		{
			string? exposedName = ScriptService.EnumMap.FirstOrDefault(pair => pair.Value == raw).Key;
			return exposedName == null ? "int" : SafeIdentifier(exposedName);
		}
		return "object";
	}

	private static int InheritanceDepth(Type type) { int depth = 0; for (Type? current = type.BaseType; current != null; current = current.BaseType) depth++; return depth; }
	private static string BindingName(Type type) => SafeIdentifier(type.IsNested && type.DeclaringType != null
		? type.DeclaringType.Name + "_" + type.Name
		: type.Name);
	private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
	private static string SafeIdentifier(string value) => value is "object" or "string" or "event" or "base" or "new" or "params" or "internal" ? "@" + value : value.Replace('`', '_');
}
