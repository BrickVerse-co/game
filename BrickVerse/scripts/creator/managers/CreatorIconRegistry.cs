// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.

using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BrickVerse.Creator.Managers;

/// <summary>Project-local Explorer icon overrides. Instance overrides win over class overrides.</summary>
public static class CreatorIconRegistry
{
	private const string RegistryFileName = "icon-registry.json";
	private const string IconFolderName = "icons";
	private static readonly Dictionary<string, IconRegistryData> Registries = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, Texture2D> TextureCache = new(StringComparer.OrdinalIgnoreCase);

	public static event Action<CreatorSession>? Changed;

	public static Texture2D Resolve(Instance instance)
	{
		CreatorSession? session = GetSession(instance);
		if (session == null) return Globals.LoadIcon(instance.ClassName);
		IconRegistryData data = GetData(session);
		if (data.Instances.TryGetValue(instance.ObjectID, out string? individual))
			return LoadOverride(session, individual) ?? Globals.LoadIcon(instance.ClassName);
		if (data.Classes.TryGetValue(instance.ClassName, out string? global))
			return LoadOverride(session, global) ?? Globals.LoadIcon(instance.ClassName);
		return Globals.LoadIcon(instance.ClassName);
	}

	public static string? GetClassOverride(CreatorSession session, string className) =>
		GetData(session).Classes.TryGetValue(className, out string? path) ? path : null;

	public static Texture2D ResolveClass(CreatorSession session, string className)
	{
		string? path = GetClassOverride(session, className);
		return path == null ? Globals.LoadIcon(className) : LoadOverride(session, path) ?? Globals.LoadIcon(className);
	}

	public static string? GetInstanceOverride(Instance instance)
	{
		CreatorSession? session = GetSession(instance);
		return session != null && GetData(session).Instances.TryGetValue(instance.ObjectID, out string? path) ? path : null;
	}

	public static void PromptSetInstance(Instance instance)
	{
		CreatorSession? session = GetSession(instance);
		PromptForImage(session, path =>
		{
			GetData(session!).Instances[instance.ObjectID] = ImportImage(session!, path);
			Save(session!);
		});
	}

	public static void PromptSetClass(CreatorSession? session, string className) => PromptForImage(session, path =>
	{
		GetData(session!).Classes[className] = ImportImage(session!, path);
		Save(session!);
	});

	public static void ClearInstance(Instance instance)
	{
		CreatorSession? session = GetSession(instance);
		if (session != null && GetData(session).Instances.Remove(instance.ObjectID)) Save(session);
	}

	public static void ClearClass(CreatorSession session, string className)
	{
		if (GetData(session).Classes.Remove(className)) Save(session);
	}

	public static string RegistryPath(CreatorSession session) => Path.Combine(session.BVProjectFolderPath, RegistryFileName);

	private static CreatorSession? GetSession(Instance instance)
	{
		// A World enters the Explorer before its Root property is assigned. Use the
		// world itself when possible and tolerate the rest of that startup window.
		World? world = instance as World ?? instance.Root;
		return world?.LinkedSession;
	}

	public static void Reload(CreatorSession session)
	{
		Registries.Remove(RegistryPath(session));
		TextureCache.Clear();
		_ = GetData(session);
		Changed?.Invoke(session);
	}

	private static void PromptForImage(CreatorSession? session, Action<string> selected)
	{
		if (session == null) return;
		CreatorService.Interface.PromptFileSelect(new()
		{
			Title = "Choose an Explorer icon",
			CurrentDirectory = session.ProjectFolderPath,
			DialogMode = DisplayServer.FileDialogMode.OpenFile,
			Filters = ["*.png,*.jpg,*.jpeg,*.webp,*.svg ; Image files"]
		}, paths =>
		{
			if (paths.Length == 0) return;
			try { selected(paths[0]); }
			catch (Exception exception) { CreatorService.Interface.PopupAlert(exception.Message, "Could not set icon"); }
		});
	}

	private static string ImportImage(CreatorSession session, string source)
	{
		if (!File.Exists(source)) throw new FileNotFoundException("The selected icon could not be found.", source);
		string extension = Path.GetExtension(source).ToLowerInvariant();
		if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".webp" and not ".svg")
			throw new InvalidOperationException("Choose a PNG, JPG, WebP, or SVG image.");
		string folder = Path.Combine(session.BVProjectFolderPath, IconFolderName);
		Directory.CreateDirectory(folder);
		string name = $"{Guid.NewGuid():N}{extension}";
		File.Copy(source, Path.Combine(folder, name), true);
		return $"{IconFolderName}/{name}";
	}

	private static Texture2D? LoadOverride(CreatorSession session, string relativePath)
	{
		string absolute = Path.GetFullPath(Path.Combine(session.BVProjectFolderPath, relativePath));
		if (!absolute.StartsWith(session.BVProjectFolderPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(absolute)) return null;
		if (TextureCache.TryGetValue(absolute, out Texture2D? cached) && GodotObject.IsInstanceValid(cached)) return cached;
		Image image = Image.LoadFromFile(absolute);
		if (image.IsEmpty()) return null;
		Texture2D texture = ImageTexture.CreateFromImage(image);
		TextureCache[absolute] = texture;
		return texture;
	}

	private static IconRegistryData GetData(CreatorSession session)
	{
		string path = RegistryPath(session);
		if (Registries.TryGetValue(path, out IconRegistryData? cached)) return cached;
		IconRegistryData data = new();
		try
		{
			if (File.Exists(path)) data = JsonSerializer.Deserialize<IconRegistryData>(File.ReadAllText(path)) ?? new();
		}
		catch (Exception exception) { GD.PushWarning($"Could not load Creator icon registry '{path}': {exception.Message}"); }
		Registries[path] = data;
		return data;
	}

	private static void Save(CreatorSession session)
	{
		Directory.CreateDirectory(session.BVProjectFolderPath);
		File.WriteAllText(RegistryPath(session), JsonSerializer.Serialize(GetData(session), new JsonSerializerOptions { WriteIndented = true }));
		Changed?.Invoke(session);
	}

	public sealed class IconRegistryData
	{
		public Dictionary<string, string> Classes { get; set; } = new(StringComparer.Ordinal);
		public Dictionary<string, string> Instances { get; set; } = new(StringComparer.Ordinal);
	}
}
