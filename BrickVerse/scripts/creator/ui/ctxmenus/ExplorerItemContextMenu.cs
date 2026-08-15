// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Attributes;
using BrickVerse.Creator.Managers;
using BrickVerse.Creator.Settings;
using BrickVerse.Creator.UI.Popups;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Datamodel.Interfaces;
using BrickVerse.Datamodel.Services;
using BrickVerse.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrickVerse.Creator.UI;

public partial class ExplorerItemContextMenu : ContextMenu
{
	public required List<Instance> Targets;
	public Instance? Target;

	public override void _Ready()
	{
		bool isSingle = Targets.Count == 1;

		if (isSingle)
		{
			Target = Targets[0];
			AddIconItem("plus", "Add Child", 1);
			AddIconItem("script", "Add Script", 2);
			AddSeparator();
			if (Target is Dynamic dyn)
			{
				AddIconItem("camera", "Go To", 5);
				AddSeparator();
			}
			if (Target.LinkedModel != null)
			{
				if (Target.EditableChildren)
				{
					AddIconItem("edit", "Close Model", 41);
				}
				else
				{
					AddIconItem("edit", "Edit Model", 41);
				}
				AddIconItem("save", "Save Model", 42);
				AddIconItem("link-off", "Detach Model", 43);
				AddSeparator();
			}
		}
		AddIconItem("cut", "Cut", 20);
		AddIconItem("copy", "Copy", 21);
		AddIconItem("clipboard", "Paste", 22);
		AddIconItem("duplicate", "Duplicate", 23);
		AddIconItem("select-all", "Select Children", 25);
		AddSeparator();
		AddIconItem("group", "Group", 31);
		if (Target is IGroup or RigidBody)
		{
			AddIconItem("ungroup", "Ungroup", 32);
		}

		Entity[] geometryTargets = [.. Targets.OfType<Entity>()];
		if (CreatorBetaFeatures.IsEnabled(CreatorBetaFeatures.SolidModeling) && geometryTargets.Length > 0)
		{
			AddSeparator();
			if (geometryTargets.Length >= 2)
				AddIconItem("group", "Union", 33);
			bool allNegated = geometryTargets.All(entity => entity.IsNegated);
			AddIconItem("subtract", allNegated ? "Unnegate" : "Negate", 34);
		}
		if (CreatorBetaFeatures.IsEnabled(CreatorBetaFeatures.SolidModeling) && isSingle && Target is UnionOperation)
		{
			AddIconItem("ungroup", "Separate Union", 35);
		}

		if (Target is World)
		{
			AddSeparator();
			AddIconItem("publish", "Publish world", 39);
		}
		else if (Target is Model)
		{
			AddSeparator();
			AddIconItem("publish", "Publish prefab", 39);
		}

		if (isSingle)
		{
			AddIconItem("route", "Copy Lua Path", 51);
			AddIconItem("book", "Open Documentation", 59);
		}
		AddSeparator();
		AddIconItem("lock", "Lock/Unlock", 61);
		if (Target is ServerScript)
		{
			AddSeparator();
			AddIconItem("addon", "Install as addon", 71);
			AddIconItem("publish", "Publish addon", 39);
		}
		if (!Targets[0].GetType().IsDefined(typeof(StaticAttribute), false))
		{
			AddSeparator();
			AddIconItem("trash", "Delete", 101);
		}

		IdPressed += OnIdPressed;
	}

	private async void OnIdPressed(long id)
	{
		Instance[] targets = [.. Targets];
		CreatorContextService context = targets[0].Root.CreatorContext;

		switch (id)
		{
			case 1: // Add child
				{
					CreatorService.Interface.OpenInsertMenu(Target);
					break;
				}
			case 2: // Add script
				{
					CreatorService.Interface.PromptCreateScript(Target);
					break;
				}
			case 5: // Go To
				{
					context.Freelook.MoveToSelected();
					break;
				}
			case 20: // Cut
				{
					await CreatorService.Clipboard.SetClipboard(targets);
					context.History.DeleteInstances(targets);
					break;
				}
			case 21: // Copy
				{
					await CreatorService.Clipboard.SetClipboard(targets);
					break;
				}
			case 22: // Paste
				{
					await CreatorService.Clipboard.PasteClipboard(true);
					break;
				}
			case 23: // Duplicate
				{
					context.History.DuplicateInstances(targets);
					break;
				}
			case 25: // Select Children
				{
					context.Selections.DeselectAll();
					foreach (Instance item in targets)
					{
						context.Selections.SelectChild(item);
					}
					break;
				}
			case 31: // Group
				{
					context.History.GroupInstances(targets);
					break;
				}
			case 32: // Ungroup
				{
					context.History.UngroupInstances(targets);
					break;
				}
			case 33: // Union
				{
					Entity[] entities = [.. targets.OfType<Entity>()];
					if (entities.Length < 2) break;
					Instance?[] parents = entities.Select(entity => entity.Parent).ToArray();
					Transform3D[] transforms = entities.Select(entity => entity.GDNode3D.GlobalTransform).ToArray();
					bool[] hidden = entities.Select(entity => entity.IsHidden).ToArray();
					bool[] collisions = entities.Select(entity => entity.CanCollide).ToArray();
					UnionOperation union;
					try
					{
						union = await context.Root.Geometry.UnionAsync(entities);
					}
					catch (Exception exception)
					{
						GD.PushError($"Unable to union the selected solids: {exception}");
						CreatorService.Interface.StatusBar?.SetStatus($"Union failed: {exception.Message}");
						break;
					}
					Instance unionParent = union.Parent!;
					Transform3D unionTransform = union.GDNode3D.GlobalTransform;
					context.History.RecordAppliedAction("Union solids",
						new BVCallback((_) =>
						{
							union.Parent = unionParent;
							union.GDNode3D.GlobalTransform = unionTransform;
							for (int i = 0; i < entities.Length; i++) ReparentPreservingTransform(entities[i], union, transforms[i], true, false);
							context.Selections.SelectOnly(union);
						}),
						new BVCallback((_) =>
						{
							union.Parent = union.Root.TemporaryContainer;
							for (int i = 0; i < entities.Length; i++)
							{
								ReparentPreservingTransform(entities[i], parents[i]!, transforms[i], hidden[i], collisions[i]);
							}
							context.Selections.DeselectAll();
							foreach (Entity entity in entities) context.Selections.Select(entity);
						}));
					break;
				}
			case 34: // Negate / Unnegate
				{
					Entity[] entities = [.. targets.OfType<Entity>()];
					bool[] previous = entities.Select(entity => entity.IsNegated).ToArray();
					bool negate = !entities.All(entity => entity.IsNegated);
					Action apply = () =>
					{
						foreach (Entity entity in entities) entity.IsNegated = negate;
						RebuildContainingUnions(entities);
					};
					Action revert = () =>
					{
						for (int i = 0; i < entities.Length; i++) entities[i].IsNegated = previous[i];
						RebuildContainingUnions(entities);
					};
					apply();
					context.History.RecordAppliedAction(negate ? "Negate solids" : "Unnegate solids", new BVCallback((_) => apply()), new BVCallback((_) => revert()));
					break;
				}
			case 35: // Separate Union
				{
					if (Target is not UnionOperation union || union.Parent == null) break;
					Instance parent = union.Parent;
					Entity[] sources = [.. union.GetChildren().OfType<Entity>()];
					Transform3D[] transforms = sources.Select(source => source.GDNode3D.GlobalTransform).ToArray();
					Action separate = () =>
					{
						for (int i = 0; i < sources.Length; i++) ReparentPreservingTransform(sources[i], parent, transforms[i], false, true);
						union.Parent = union.Root.TemporaryContainer;
						context.Selections.DeselectAll();
						foreach (Entity source in sources) context.Selections.Select(source);
					};
					Action restore = () =>
					{
						union.Parent = parent;
						for (int i = 0; i < sources.Length; i++) ReparentPreservingTransform(sources[i], union, transforms[i], true, false);
						context.Selections.SelectOnly(union);
					};
					separate();
					context.History.RecordAppliedAction("Separate union", new BVCallback((_) => separate()), new BVCallback((_) => restore()));
					break;
				}
			case 39: // Publish
				{
					if (Target is World)
					{
						CreatorService.Interface.OpenWorldPublish((World)Target);
						return;
					}

					CreatorService.Interface.OpenPublish(Target!);
					break;
				}
			case 41: // Edit Model
				{
					if (Target != null)
					{
						if (Target.EditableChildren)
						{
							if (!await CreatorService.Interface.PromptConfirmation("Closing this model will discard any unsaved changes.", dismissKey: CreatorSettingKeys.Popups.CloseModelWarning)) return;
						}
						Target?.EditableChildren = !Target.EditableChildren;
					}
					break;
				}
			case 42: // Save Model
				{
					Target?.SaveModel();
					break;
				}
			case 43: // Detach Model
				{
					Target?.DetachModel();
					break;
				}
			case 51: // Copy Lua Path
				{
					DisplayServer.ClipboardSet(Target!.LuaPath);
					break;
				}
			case 59: // Open Documentation
				{
					OS.ShellOpen("https://developers.brickverse.gg/game-api/types/" + Target!.ClassName.ToLower());
					break;
				}
			case 61: // Lock/Unlock
				{
					List<Dynamic> dyns = [];
					foreach (Instance item in targets)
					{
						if (item is Dynamic dyn)
						{
							dyns.Add(dyn);
						}
					}
					context.History.ToggleLockedDynamics([.. dyns]);
					break;
				}
			case 71: // Install as addon
				{
					if (Target is ServerScript s)
					{
						await AddonsManager.InstallAddonFromScript(s);
					}
					break;
				}
			case 101: // Delete
				{
					context.History.DeleteInstances(targets);
					break;
				}
		}
	}

	private static void ReparentPreservingTransform(Entity entity, Instance parent, Transform3D transform, bool hidden, bool canCollide)
	{
		entity.Parent = parent;
		entity.GDNode3D.GlobalTransform = transform;
		entity.IsHidden = hidden;
		entity.CanCollide = canCollide;
	}

	private static void RebuildContainingUnions(IEnumerable<Entity> entities)
	{
		foreach (UnionOperation union in entities.Select(entity => entity.Parent).OfType<UnionOperation>().Distinct())
			_ = union.RebuildAsync();
	}
}
