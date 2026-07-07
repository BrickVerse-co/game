// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using System.Diagnostics;
using System.IO;

namespace Polytoria.Renderer;

public partial class RendererEntry : AppEntry
{
	public override async void _Ready()
	{
		Stopwatch sw = new();
		sw.Restart();

		RendererViewport viewport = new();
		AddChild(viewport);
		viewport.Setup();
		PT.Print("Viewport setup: ", sw.ElapsedMilliseconds, "ms");

		sw.Restart();
		PT.Print("Loading avatar...");
		await viewport.AddAvatar(64499, RendererViewport.AvatarPhotoTypeEnum.FullAvatar);
		//await viewport.AddAccessory(48150);
		PT.Print("Load avatar: ", sw.ElapsedMilliseconds, "ms");

		sw.Restart();
		byte[] png = await viewport.SavePng();
		PT.Print("Save to png: ", sw.ElapsedMilliseconds, "ms");
		File.WriteAllBytes(ProjectSettings.GlobalizePath("res://temp/test.png"), png);
	}
}
