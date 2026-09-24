// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Creator.Managers;
using BrickVerse.Creator.UI;
using BrickVerse.Creator.UI.Docking;
using BrickVerse.Datamodel.Resources;
using BrickVerse.Scripting;
using BrickVerse.Shared;
using Godot;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace BrickVerse.Datamodel.Creator;

[Static("Addons")]
public sealed partial class CreatorAddons : Instance
{
	private const float CleanupTimeout = 10;

	private readonly Dictionary<string, AddonObject> _identifierToAddon = [];
	private readonly Dictionary<Script, AddonObject> _scriptToAddon = [];

	[ScriptMethod(Permissions = ScriptPermissionFlags.ContextAccess)]
	public AddonObject Register([ScriptingCaller] Script caller, string identifier)
	{
		if (_scriptToAddon.ContainsKey(caller))
		{
			throw new System.Exception("This script has already been registered");
		}
		if (_identifierToAddon.TryGetValue(identifier, out AddonObject? addonObject))
		{
			CleanupAddonObject(addonObject);
		}
		AddonObject obj = new() { Identifier = identifier, Root = Root, ScriptSource = caller };
		obj.Creator = new CreatorAddonService(obj);
		_identifierToAddon[identifier] = obj;
		_scriptToAddon.Add(caller, obj);
		return obj;
	}

	public override void PreDelete()
	{
		foreach (AddonObject addon in _identifierToAddon.Values)
		{
			CleanupAddonObject(addon);
		}
		_identifierToAddon.Clear();
		base.PreDelete();
	}

	private async void CleanupAddonObject(AddonObject obj)
	{
		Menu.Singleton.RemoveAddonMenu(obj);
		obj.Creator?.Cleanup();
		obj.CleanupReceived.Invoke();
		_scriptToAddon.Remove(obj.ScriptSource);

		// Wait for CleanupTimeout then stop the scripts
		await Globals.Singleton.WaitAsync(CleanupTimeout);
		obj.ScriptSource.ForceDelete();
	}

	public class AddonObject : IScriptObject
	{
		private string _addonName = "No name";
		private BVImageAsset? _addonIcon;

		public World Root = null!;
		public Script ScriptSource = null!;
		[ScriptProperty] public CreatorAddonService Creator { get; internal set; } = null!;

		[ScriptProperty] public string Identifier { get; internal set; } = "";
		[ScriptProperty] public BVSignal CleanupReceived { get; private set; } = new();

		[ScriptProperty]
		public string AddonName
		{
			get => _addonName;
			set
			{
				_addonName = value;
				Menu.Singleton.UpdateAddonMenu(this);
			}
		}

		[ScriptProperty]
		public BVImageAsset? AddonIcon
		{
			get => _addonIcon;
			set
			{
				_addonIcon = value;
				Menu.Singleton.UpdateAddonMenu(this);
			}
		}

		public readonly List<AddonToolItem> ToolItems = [];

		[ScriptMethod]
		public static async Task RequestPermissions([ScriptingCaller] Script caller, AddonPermissionEnum[] perms)
		{
			var data = AddonsManager.GetAddonSession(caller);
			if (data != null)
			{
				if (AddonsManager.GetHasAskedForPerms(data.Path)) return;
				bool res = await CreatorService.Interface.PromptAddonReqPerm(perms, data.Data);

				if (res)
				{
					AddonsManager.SetAddonPermissions(caller, perms, true);
				}
				else
				{
					AddonsManager.SetAddonPermissions(caller, perms, false);
					throw new System.Exception("User declined permissions");
				}
			}
		}

		[ScriptMethod]
		public AddonToolItem CreateToolItem(string txt)
		{
			AddonToolItem item = new(txt);
			ToolItems.Add(item);
			Menu.Singleton.UpdateAddonMenu(this);
			return item;
		}
	}

	/// <summary>
	/// Per-addon Creator API. It deliberately routes mutations through CreatorHistory
	/// and owns every dock widget it creates, matching Studio-style plugin cleanup.
	/// </summary>
	public sealed class CreatorAddonService(AddonObject addon) : IScriptObject
	{
		private readonly AddonObject _addon = addon;
		private readonly List<AddonDockWidget> _widgets = [];

		[ScriptMethod]
		public AddonHistoryTrack CreateHistoryTrack(string title) => new(_addon.Root.CreatorContext.History, title);

		[ScriptMethod]
		public AddonDockWidget CreateDockWidget(string title, string preferredHost = "right.primary")
		{
			string panelId = $"addon.{_addon.Identifier}.{Guid.NewGuid():N}";
			AddonDockWidget widget = new(panelId, title, _addon.Root);
			if (!DockManager.AddPanel(new DockPanel(panelId, title, widget.RootControl), preferredHost))
			{
				// A layout may not include the requested host; use the standard right dock.
				DockManager.AddPanel(new DockPanel(panelId, title, widget.RootControl), "right.primary");
			}
			_widgets.Add(widget);
			return widget;
		}

		internal void Cleanup()
		{
			foreach (AddonDockWidget widget in _widgets)
				widget.Destroy();
			_widgets.Clear();
		}
	}

	public sealed class AddonHistoryTrack : IScriptObject
	{
		private readonly CreatorHistory _history;
		private bool _committed;

		internal AddonHistoryTrack(CreatorHistory history, string title)
		{
			_history = history;
			_history.NewAction(string.IsNullOrWhiteSpace(title) ? "Addon action" : title);
		}

		[ScriptMethod]
		public void Record(BVCallback redo, BVCallback undo)
		{
			if (_committed) throw new InvalidOperationException("This history track has already been committed.");
			_history.AddDoCallback(redo);
			_history.AddUndoCallback(undo);
		}

		[ScriptMethod]
		public void Commit()
		{
			if (_committed) return;
			_history.CommitAction();
			_committed = true;
		}

		[ScriptMethod]
		public void Cancel()
		{
			if (_committed) return;
			_history.CancelAction();
			_committed = true;
		}

	}

	public sealed class AddonDockWidget : IScriptObject
	{
		internal VBoxContainer RootControl { get; }
		private readonly string _panelId;
		private readonly UIView _uiRoot;

		internal AddonDockWidget(string panelId, string title, World world)
		{
			_panelId = panelId;
			RootControl = new VBoxContainer { Name = "AddonDockWidget", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
			_uiRoot = Globals.LoadInstance<UIView>(world);
			_uiRoot.Name = title;
			_uiRoot.OverrideParentCheck = true;
			RootControl.AddChild(_uiRoot.GDNode);
			RootControl.Resized += ResizeUIRoot;
			ResizeUIRoot();
		}

		private void ResizeUIRoot()
		{
			_uiRoot.SizeOffset = RootControl.Size;
			_uiRoot.PositionOffset = Vector2.Zero;
		}

		/// <summary>Creates and mounts a real BrickVerse UI instance such as UIView, UILabel, UIButton, or UIImage.</summary>
		[ScriptMethod]
		public UIField CreateUI(string className)
		{
			UIField field = Globals.LoadInstance<UIField>(className, _uiRoot.Root)
				?? throw new ArgumentException($"'{className}' is not an instantiable UIField.", nameof(className));
			field.Parent = _uiRoot;
			return field;
		}

		/// <summary>Mounts an existing UI tree under this dock widget.</summary>
		[ScriptMethod]
		public void MountUI(UIField field)
		{
			ArgumentNullException.ThrowIfNull(field);
			if (field.Root != _uiRoot.Root) throw new InvalidOperationException("Dock UI must belong to the same Creator world as the addon.");
			field.Parent = _uiRoot;
		}

		[ScriptMethod]
		public AddonDockLabel CreateLabel(string text)
		{
			Label label = new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			RootControl.AddChild(label); return new AddonDockLabel(label);
		}

		[ScriptMethod]
		public AddonDockButton CreateButton(string text)
		{
			Button button = new() { Text = text };
			AddonDockButton item = new(button);
			button.Pressed += () => item.Pressed.Invoke();
			RootControl.AddChild(button); return item;
		}

		[ScriptMethod] public void Show() => DockManager.OpenPanel(_panelId);
		[ScriptMethod] public void Hide() => DockManager.ClosePanel(_panelId);
		[ScriptMethod] public void Destroy()
		{
			DockManager.ClosePanel(_panelId, suppressSave: true);
			RootControl.Resized -= ResizeUIRoot;
			_uiRoot.ForceDelete();
			RootControl.QueueFree();
		}
	}

	public sealed class AddonDockLabel(Label label) : IScriptObject
	{
		private readonly Label _label = label;
		[ScriptProperty] public string Text { get => _label.Text; set => _label.Text = value; }
	}

	public sealed class AddonDockButton(Button button) : IScriptObject
	{
		private readonly Button _button = button;
		[ScriptProperty] public string Text { get => _button.Text; set => _button.Text = value; }
		[ScriptProperty] public BVSignal Pressed { get; private set; } = new();
	}

	public class AddonToolItem(string txt) : IScriptObject
	{
		public string Text = txt;

		[ScriptProperty] public BVSignal Pressed { get; private set; } = new();
	}

	[ScriptEnum(IsCreatorOnly = true)]
	public enum AddonPermissionEnum
	{
		IORead,
		IOWrite
	}
}
