using Godot;
using System;
using System.IO;

public partial class DevScreenShot : Node
{
	private const Key ScreenshotKey = Key.F2;
	private const Key OpenFolderKey = Key.F1;

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		if (key.Keycode == ScreenshotKey)
		{
			CaptureScreenshot();
		}
		else if (key.Keycode == OpenFolderKey)
		{
			OpenScreenshotFolder();
		}
	}

	private void CaptureScreenshot()
	{
		Image image = GetViewport().GetTexture().GetImage();

		string directory = "user://screenshots";
		DirAccess.MakeDirRecursiveAbsolute(
			ProjectSettings.GlobalizePath(directory)
		);

		string path =
			$"{directory}/screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";

		Error error = image.SavePng(path);

		if (error == Error.Ok)
		{
			GD.Print(
				$"Screenshot saved: {ProjectSettings.GlobalizePath(path)}"
			);
		}
		else
		{
			GD.PrintErr($"Failed to save screenshot: {error}");
		}
	}

	private void OpenScreenshotFolder()
	{
		string directory = "user://screenshots";
		string absolutePath = ProjectSettings.GlobalizePath(directory);

		Directory.CreateDirectory(absolutePath);

		// Open using the operating system's default file manager.
		OS.ShellOpen(absolutePath);

		GD.Print($"Opened screenshot folder: {absolutePath}");
	}
}
