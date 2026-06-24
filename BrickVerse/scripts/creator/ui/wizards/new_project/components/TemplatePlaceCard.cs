// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Managers;
using System.Text.Json;
using static Polytoria.Creator.Managers.ProjectManager;

namespace Polytoria.Creator.UI.Wizards;

public partial class TemplatePlaceCard : Button
{
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Label _nameLabel = null!;
	[Export] private Label _descLabel = null!;
	public string TemplateFolderPath = "";

	public override void _Ready()
	{
		if (!string.IsNullOrEmpty(TemplateFolderPath))
		{
			string f = FileAccess.GetFileAsString(TemplateFolderPath.PathJoin("template.json"));
			TemplateProjectJSON templateData = JsonSerializer.Deserialize(f, TemplateProjectJSONGenerationContext.Default.TemplateProjectJSON);
			_nameLabel.Text = templateData.Name;
			_descLabel.Text = templateData.Description;
			_thumbnailRect.Texture = GD.Load<Texture2D>(TemplateFolderPath.PathJoin("thumbnail.png"));
		}
	}
}
