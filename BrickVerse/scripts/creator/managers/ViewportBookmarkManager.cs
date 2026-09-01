// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
using Godot;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using System.IO;

namespace BrickVerse.Creator.Managers;

public static class ViewportBookmarkManager
{
	private const string FileName = "viewport-bookmarks.cfg";

	public static bool Save(int slot)
	{
		if (!TryGetContext(out World world, out CreatorSession session, out string path)) return false;
		ConfigFile config = new();
		config.Load(path);
		string section = GetSection(world);
		config.SetValue(section, $"slot_{slot}_transform", world.CreatorContext.Freelook.GDNode3D.GlobalTransform);
		config.SetValue(section, $"slot_{slot}_fov", world.CreatorContext.Freelook.FOV);
		Error result = config.Save(path);
		if (result != Error.Ok)
		{
			CreatorService.Interface.PopupAlert($"Could not save viewport bookmark: {result}");
			return false;
		}
		CreatorService.Interface.StatusBar?.SetStatus($"Saved viewport bookmark {slot}");
		return true;
	}

	public static bool Recall(int slot)
	{
		if (!TryGetContext(out World world, out _, out string path)) return false;
		ConfigFile config = new();
		if (config.Load(path) != Error.Ok || !config.HasSectionKey(GetSection(world), $"slot_{slot}_transform"))
		{
			CreatorService.Interface.StatusBar?.SetStatus($"Viewport bookmark {slot} has not been saved");
			return false;
		}
		string section = GetSection(world);
		world.CreatorContext.Freelook.GDNode3D.GlobalTransform = config.GetValue(section, $"slot_{slot}_transform").AsTransform3D();
		if (config.HasSectionKey(section, $"slot_{slot}_fov"))
			world.CreatorContext.Freelook.FOV = config.GetValue(section, $"slot_{slot}_fov").AsSingle();
		CreatorService.Interface.StatusBar?.SetStatus($"Recalled viewport bookmark {slot}");
		return true;
	}

	public static bool Exists(int slot)
	{
		if (!TryGetContext(out World world, out _, out string path)) return false;
		ConfigFile config = new();
		return config.Load(path) == Error.Ok && config.HasSectionKey(GetSection(world), $"slot_{slot}_transform");
	}

	private static bool TryGetContext(out World world, out CreatorSession session, out string path)
	{
		world = World.Current!;
		session = CreatorService.CurrentSession!;
		path = "";
		if (world == null || session == null) return false;
		Directory.CreateDirectory(session.BVProjectFolderPath);
		path = Path.Join(session.BVProjectFolderPath, FileName);
		return true;
	}

	private static string GetSection(World world) => string.IsNullOrWhiteSpace(world.WorldFilePath)
		? "main" : world.WorldFilePath.Replace('/', '_').Replace('\\', '_');
}
