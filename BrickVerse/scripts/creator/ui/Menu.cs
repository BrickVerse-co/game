// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using BrickVerse.Creator.UI.Popups;
using BrickVerse.Creator.UI.Splashes;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Shared;
using Godot;
using static BrickVerse.Datamodel.Creator.CreatorAddons;

namespace BrickVerse.Creator.UI;

public sealed partial class Menu : PanelContainer
{
	private sealed class MenuAddonSlotItem : MenuButtonItem { }

	private class MenuButtonItem : MenuItem
	{
		public string Text = "Unknown";
		public string? Icon;
		public Shortcut? KeyShortcut;
		public Action? Pressed;
		public bool RequireGameOpen = false;
		public string? RequiredBetaFeature;
		public int Id = 0;
		public int Index = 0;
	}

	private sealed class MenuSeperatorItem : MenuItem
	{
		public string? Text = null;
	}

	private abstract class MenuItem { }

	private sealed class MenuButtonMenus
	{
		public string Title = null!;
		public MenuButton Button = null!;
		public PopupMenu Popup = null!;
		public readonly Dictionary<int, MenuItem> IdToItem = [];
		public bool RequireGameOpen = false;
		public bool DevOnly = false;
	}

	private class AddonMenuData
	{
		public bool Visible = false;
		public int Index = -1;
		public int ItemId;
		public Dictionary<int, AddonToolItem> IndexToToolItem = [];
		public AddonObject AddonObject = null!;
	}

	public static Menu Singleton { get; private set; } = null!;

	private Control _menuButtons = null!;
	private HBoxContainer _topRightLayout = null!;
	private readonly Dictionary<MenuButtonMenus, MenuItem[]> _menus = [];

	private readonly Dictionary<World, Dictionary<string, AddonMenuData>> _addonDataByRoot = [];
	private World? _currentRoot = null;
	private int _addonItemId = 0;

	private MenuButton _bvButton = null!;
	private PopupMenu _bvMenu = null!;
	private PopupMenu _addonSlotMenu = null!;

	public Menu()
	{
		Singleton = this;
	}

	public override void _Ready()
	{
		CreatorBetaFeatures.FeatureChanged += OnBetaFeatureChanged;
		_menus.Add(
			new() { Title = "File" },
			[
				new MenuButtonItem()
				{
					Text = "Command Palette...",
					Icon = "search",
					KeyShortcut = new() { Events = [new InputEventKey() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.P }] },
					Pressed = CommandPalettePopup.Open,
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "What's New",
					Icon = "star",
					Pressed = WhatsNewPopup.ShowLatest,
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "New",
					Icon = "plus",
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, Keycode = Key.N }],
					},
					Pressed = CreatorInterface.CreateNewWorld,
				},
				new MenuButtonItem()
				{
					Text = "Open",
					Icon = "folder",
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, Keycode = Key.O }],
					},
					Pressed = CreatorService.Interface.PromptOpenWorld,
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Save",
					Icon = "save",
					RequireGameOpen = true,
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, Keycode = Key.S }],
					},
					Pressed = () =>
					{
						CreatorService.SaveCurrentFile();
					},
				},
				new MenuButtonItem()
				{
					Text = "Save All",
					Icon = "save",
					KeyShortcut = new() { Events = [new InputEventKey() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.S }] },
					Pressed = () => Tabs.Singleton.SaveAll(),
				},
				new MenuButtonItem()
				{
					Text = "Save As...",
					Icon = "save",
					RequireGameOpen = true,
					KeyShortcut = new()
					{
						Events =
						[
							new InputEventKey()
							{
								CtrlPressed = true,
								ShiftPressed = true,
								Keycode = Key.S,
							},
						],
					},
					Pressed = CreatorService.SaveCurrentFileAs,
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Publish",
					Icon = "publish",
					RequireGameOpen = true,
					Pressed = async () =>
					{
						if (World.Current != null)
						{
							CreatorService.Interface.OpenWorldPublish(World.Current);
						}
						else
						{
							CreatorService.Interface.PopupAlert(
								"No world is currently open to publish."
							);
						}
					},
				},
				new MenuButtonItem()
				{
					Text = "Publish As...",
					Icon = "rocket",
					RequireGameOpen = true,
					Pressed = async () =>
					{
						if (World.Current != null)
						{
							CreatorService.Interface.OpenWorldPublish(World.Current, true);
						}
						else
						{
							CreatorService.Interface.PopupAlert(
								"No world is currently open to publish."
							);
						}
					},
				},
				new MenuButtonItem()
				{
					Text = "Edit Universe Details",
					Icon = "edit",
					RequireGameOpen = true,
					Pressed = async () =>
					{
						if (World.Current != null)
						{
							OS.ShellOpen(
								"https://brickverse.gg/creator/worlds/edit/"
									+ World.Current.UniverseID
							);
						}
						else
						{
							CreatorService.Interface.PopupAlert(
								"You must open a world before you can open it in the browser."
							);
						}
					},
				},
				new MenuButtonItem()
				{
					Text = "Open in Browser",
					Icon = "external-link",
					RequireGameOpen = true,
					Pressed = async () =>
					{
						if (World.Current != null)
						{
							OS.ShellOpen("https://brickverse.gg/worlds/" + World.Current.WorldID);
						}
						else
						{
							CreatorService.Interface.PopupAlert(
								"You must open a world before you can open it in the browser."
							);
						}
					},
				},
				new MenuButtonItem()
				{
					Text = "Close Place",
					Icon = "x",
					RequireGameOpen = true,
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { Keycode = Key.F4 }],
					},
					Pressed = () => Tabs.Singleton.CloseCurrentPlace(),
				},
				new MenuButtonItem()
				{
					Text = "Exit",
					Icon = "door-exit",
					Pressed = () =>
					{
						Globals.Singleton.Quit();
					},
				},
			]
		);

		_menus.Add(
			new() { Title = "Edit", RequireGameOpen = true },
			[
				new MenuButtonItem()
				{
					Text = "Undo",
					Icon = "arrow-counter-clockwise",
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, Keycode = Key.Z }],
					},
					Pressed = CreatorService.Undo,
				},
				new MenuButtonItem()
				{
					Text = "Redo",
					Icon = "arrow-clockwise",
					KeyShortcut = new()
					{
						Events =
						[
							new InputEventKey()
							{
								CtrlPressed = true,
								ShiftPressed = true,
								Keycode = Key.Z,
							},
						],
					},
					Pressed = CreatorService.Redo,
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Find in Files...",
					Icon = "search",
					KeyShortcut = new() { Events = [new InputEventKey() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.F }] },
					Pressed = FindInFilesPopup.Open,
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Delete",
					Icon = "trash",
					KeyShortcut = new()
					{
						Events =
						[
							new InputEventKey() { Keycode = Key.Delete },
							new InputEventKey() { Keycode = Key.Backspace },
						],
					},
					Pressed = () =>
					{
						World.Current?.CreatorContext.Selections.DeleteSelected();
					},
				},
				new MenuButtonItem()
				{
					Text = "Duplicate",
					Icon = "duplicate",
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, Keycode = Key.D }],
					},
					Pressed = () =>
					{
						World.Current?.CreatorContext.Selections.DuplicateSelected();
					},
				},
				new MenuButtonItem()
				{
					Text = "Toggle Locked",
					Icon = "lock",
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.L }],
					},
					Pressed = () =>
					{
						World.Current?.CreatorContext.Selections.ToggleLockSelected();
					},
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Select All",
					Icon = "select-all",
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, Keycode = Key.A }],
					},
					Pressed = () =>
					{
						if (World.Current == null)
							return;
						World.Current.CreatorContext.Selections.SelectChild(
							World.Current.Environment
						);
					},
				},
				new MenuSeperatorItem(),
			]
		);

		_menus.Add(
			new() { Title = "Insert", RequireGameOpen = true },
			[
				new MenuButtonItem()
				{
					Text = "New Instance",
					Icon = "plus",
					KeyShortcut = new()
					{
						Events =
						[
							new InputEventKey() { ShiftPressed = true, Keycode = Key.Space },
							new InputEventKey() { CtrlPressed = true, Keycode = Key.I },
						],
					},
					Pressed = () =>
					{
						CreatorService.Interface.OpenInsertMenu();
					},
				},
				new MenuButtonItem()
				{
					Text = "Upload Mesh",
					Icon = "cube",
					Pressed = () =>
					{
						CreatorService.Interface.OpenUploadMeshMenu();
					},
				},
				new MenuButtonItem()
				{
					Text = "Upload Texture",
					Icon = "image-square",
					Pressed = () =>
					{
						CreatorService.Interface.OpenUploadTextureMenu();
					},
				},
				new MenuButtonItem()
				{
					Text = "Upload Sound",
					Icon = "waveform",
					Pressed = () =>
					{
						CreatorService.Interface.OpenUploadSoundMenu();
					},
				},
				new MenuButtonItem()
				{
					Text = "Upload Video",
					Icon = "video",
					Pressed = () =>
					{
						CreatorService.Interface.OpenUploadVideoMenu();
					},
				},
			]
		);

		_menus.Add(
			new() { Title = "Model", RequireGameOpen = true },
			[
				new MenuButtonItem()
				{
					Text = "Group",
					Icon = "group",
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, Keycode = Key.G }],
					},
					Pressed = () =>
					{
						World.Current?.CreatorContext.Selections.GroupSelected();
					},
				},
				new MenuButtonItem()
				{
					Text = "Ungroup",
					Icon = "ungroup",
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, Keycode = Key.U }],
					},
					Pressed = () =>
					{
						World.Current?.CreatorContext.Selections.UngroupSelected();
					},
				},
				new MenuButtonItem()
				{
					Text = "Group Folder",
					Icon = "folder",
					KeyShortcut = new()
					{
						Events =
						[
							new InputEventKey()
							{
								CtrlPressed = true,
								AltPressed = true,
								Keycode = Key.G,
							},
						],
					},
					Pressed = () =>
					{
						World.Current?.CreatorContext.Selections.GroupSelected(
							Datamodel.Creator.CreatorHistory.GroupAsEnum.Folder
						);
					},
				},
				new MenuButtonItem()
				{
					Text = "Group RigidBody",
					Icon = "brick",
					KeyShortcut = new()
					{
						Events =
						[
							new InputEventKey()
							{
								CtrlPressed = true,
								ShiftPressed = true,
								Keycode = Key.G,
							},
						],
					},
					Pressed = () =>
					{
						World.Current?.CreatorContext.Selections.GroupSelected(
							Datamodel.Creator.CreatorHistory.GroupAsEnum.RigidBody
						);
					},
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Import",
					Icon = "arrow-down",
					Pressed = () =>
					{
						CreatorService.Interface.PromptImportModel();
					},
				},
				new MenuButtonItem()
				{
					Text = "Export",
					Icon = "arrow-up",
					Pressed = () =>
					{
						CreatorService.Interface.ExportSelectedModel();
					},
				},
			]
		);

		_menus.Add(
			new() { Title = "Tools" },
			[
				new MenuButtonItem() { Text = "Data Store Explorer", Icon = "database", RequireGameOpen = true, Pressed = () => CreatorDataToolsWindow.Open(0) },
				new MenuButtonItem() { Text = "Localization Manager", Icon = "translate", RequireGameOpen = true, Pressed = () => CreatorDataToolsWindow.Open(1) },
				new MenuButtonItem() { Text = "Scene History & Diff", Icon = "history", RequireGameOpen = true, Pressed = () => CreatorDataToolsWindow.Open(2) },
				new MenuButtonItem() { Text = "Particle Editor", Icon = "play-filled", RequireGameOpen = true, Pressed = ParticleEditorWindow.Open },
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Play Test",
					Icon = "play",
					RequireGameOpen = true,
					KeyShortcut = new() { Events = [new InputEventKey() { Keycode = Key.F5 }] },
					Pressed = () =>
					{
						CreatorService.Singleton.StartLocalTest();
					},
				},
				new MenuButtonItem()
				{
					Text = "Play Test Here",
					Icon = "camera",
					RequireGameOpen = true,
					KeyShortcut = new()
					{
						Events = [new InputEventKey() { CtrlPressed = true, Keycode = Key.F5 }],
					},
					Pressed = () =>
					{
						CreatorService.Singleton.StartLocalTest(true);
					},
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Manage Addons",
					Icon = "addon",
					Pressed = CreatorInterface.PopupManageAddons,
				},
				new MenuButtonItem()
				{
					Text = "Input Manager",
					Icon = "keyboard",
					Pressed = CreatorService.Interface.OpenInputManager,
				},
				new MenuButtonItem()
				{
					Text = "Device Emulator",
					Icon = "gamepad",
					RequiredBetaFeature = CreatorBetaFeatures.DeviceEmulator,
					Pressed = DeviceEmulatorPopup.Open,
				},
				new MenuButtonItem()
				{
					Text = "Animation Editor",
					Icon = "play-filled",
					RequiredBetaFeature = CreatorBetaFeatures.AnimationEditor,
					Pressed = CreatorService.Interface.OpenAnimationEditor,
				},
				new MenuAddonSlotItem()
				{
					Text = "Addons",
					Icon = "addon",
					RequireGameOpen = true,
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Migrate Coordinates",
					Icon = "route",
					RequireGameOpen = true,
					Pressed = () =>
					{
						CreatorService.MigrateCoordinates(World.Current!);
					},
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Settings",
					Icon = "settings",
					Pressed = () =>
					{
						CreatorService.Interface.OpenSettings();
					},
				},
			]
		);

		_menus.Add(
			new() { Title = "View" },
			[
				new MenuButtonItem()
				{
					Text = "Toggle Fullscreen",
					Icon = "resize-handle",
					KeyShortcut = new() { Events = [new InputEventKey() { Keycode = Key.F11 }] },
					Pressed = () =>
					{
						CreatorInterface.ToggleFullscreen();
					},
				},
				new MenuButtonItem()
				{
					Text = "Show Runtime Debug Windows",
					Icon = "code",
					Pressed = () =>
					{
						CreatorService.Singleton.ShowRuntimeDebugWindows();
					},
				},
			]
		);

		_menus.Add(
			new() { Title = "Help" },
			[
				new MenuButtonItem()
				{
					Text = "Copy System Info",
					Icon = "copy",
					Pressed = () =>
					{
						DisplayServer.ClipboardSet(
							$"System Name: {OS.GetName() + " " + OS.GetVersionAlias()}\nCPU: {OS.GetProcessorName()} cores: {OS.GetProcessorCount()}\nVideo Adapter: {OS.GetVideoAdapterDriverInfo().Join(", ")}"
						);
					},
				},
				new MenuButtonItem()
				{
					Text = "Open Documentation",
					Icon = "book",
					Pressed = () =>
					{
						OS.ShellOpen("https://developers.brickverse.gg/");
					},
				},
				new MenuSeperatorItem(),
				new MenuButtonItem()
				{
					Text = "Report a Bug",
					Icon = "bug",
					Pressed = BugReportPopup.Open,
				},
			]
		);

		_menus.Add(
			new() { Title = "Dev", DevOnly = true },
			[
				new MenuButtonItem()
				{
					Text = "Pack Current Project",
					Icon = "archive",
					RequireGameOpen = true,
					Pressed = CreatorService.PackCurrentProject,
				},
				new MenuButtonItem()
				{
					Text = "Link Device",
					Icon = "link",
					Pressed = () =>
					{
						CreatorService.Interface.OpenLinkDevicePrompt();
					},
				},
			]
		);

		_menuButtons = GetNode<Control>("Layout/MenuButtons");
		_topRightLayout = GetNode<HBoxContainer>("Layout/Margin/Layout");
		_topRightLayout.AddChild(new CreatorToolbarUserChip());

		_bvButton = _menuButtons.GetNode<MenuButton>("BV");
		_bvMenu = _bvButton.GetPopup();

		_bvMenu.IdPressed += OnBV;

		foreach ((MenuButtonMenus mbtn, MenuItem[] items) in _menus)
		{
			if (mbtn.DevOnly && !Globals.IsInGDEditor)
				continue;
			MenuButton btnRoot = new()
			{
				Text = mbtn.Title,
				Flat = false,
				SwitchOnHover = true,
				FocusMode = FocusModeEnum.All,
			};
			PopupMenu menu = btnRoot.GetPopup();

			mbtn.Button = btnRoot;
			mbtn.Popup = menu;

			menu.IdPressed += idx =>
			{
				if (mbtn.IdToItem[(int)idx] is MenuButtonItem btn)
				{
					btn.Pressed?.Invoke();
				}
			};

			int addedCount = 0;
			foreach (MenuItem item in items)
			{
				if (item is MenuButtonItem btnI)
				{
					int id = addedCount;
					mbtn.IdToItem[id] = item;
					if (btnI is MenuAddonSlotItem addonSlot)
					{
						_addonSlotMenu = new();
						menu.AddSubmenuNodeItem(btnI.Text, _addonSlotMenu, id);
					}
					else
					{
						menu.AddItem(btnI.Text, id);
					}

					int index = menu.GetItemIndex(id);

					btnI.Index = index;

					if (btnI.Icon != null)
					{
						menu.SetItemIcon(index, Globals.LoadUIIcon(btnI.Icon));
					}

					if (btnI.KeyShortcut != null)
					{
						// Setup Ctrl Auto remap for mac
						foreach (var ev in btnI.KeyShortcut.Events)
						{
							var ek = ev.As<InputEventKey>();
							if (ek.CtrlPressed)
							{
								ek.CommandOrControlAutoremap = true;
							}
						}
						menu.SetItemShortcut(index, btnI.KeyShortcut);
					}
					if (btnI.RequiredBetaFeature != null)
					{
						bool enabled = CreatorBetaFeatures.IsEnabled(btnI.RequiredBetaFeature);
						menu.SetItemDisabled(index, !enabled);
						menu.SetItemTooltip(
							index,
							enabled
								? "Experimental Creator feature"
								: $"Enable {btnI.Text} in Beta Features first"
						);
					}
					addedCount++;
				}
				else if (item is MenuSeperatorItem st)
				{
					menu.AddSeparator(st.Text ?? "", 1000);
					addedCount++;
				}
			}

			_menuButtons.AddChild(btnRoot);
		}

		foreach (Timer timer in FindChildren("*", "Timer", true, false).Cast<Timer>())
		{
			timer.IgnoreTimeScale = true;
			timer.WaitTime = 0.01;
		}

		SwitchTo(null);
	}

	public override void _ExitTree()
	{
		CreatorBetaFeatures.FeatureChanged -= OnBetaFeatureChanged;
	}

	private void OnBetaFeatureChanged(string flag, bool enabled)
	{
		foreach ((MenuButtonMenus menu, MenuItem[] items) in _menus)
		{
			foreach (MenuItem item in items)
			{
				if (item is not MenuButtonItem button || button.RequiredBetaFeature != flag)
					continue;
				menu.Popup.SetItemDisabled(button.Index, !enabled);
				menu.Popup.SetItemTooltip(
					button.Index,
					enabled
						? "Experimental Creator feature"
						: $"Enable {button.Text} in Beta Features first"
				);
			}
		}
	}

	public void UpdateAddonMenu(AddonObject obj)
	{
		// Get or create the dictionary for this root
		if (!_addonDataByRoot.TryGetValue(obj.Root, out var rootAddons))
		{
			rootAddons = [];
			_addonDataByRoot[obj.Root] = rootAddons;
		}

		if (!rootAddons.TryGetValue(obj.Identifier, out AddonMenuData? data))
		{
			rootAddons[obj.Identifier] = new() { AddonObject = obj };
			data = rootAddons[obj.Identifier];
		}

		data.AddonObject = obj;

		// Update the menu if is the current root
		if (obj.Root == _currentRoot)
		{
			UpdateAddonMenuInUI(obj, data);
		}
	}

	private void UpdateAddonMenuInUI(AddonObject obj, AddonMenuData data)
	{
		if (!data.Visible)
		{
			// Create new addon item
			_addonItemId++;

			_addonSlotMenu.AddSubmenuNodeItem(obj.AddonName, new(), _addonItemId);
			int index = _addonSlotMenu.GetItemIndex(_addonItemId);
			data.Index = index;
			data.Visible = true;
			data.ItemId = _addonItemId;

			PopupMenu mMenu = _addonSlotMenu.GetItemSubmenuNode(data.Index);
			mMenu.IndexPressed += ind =>
			{
				data.IndexToToolItem[(int)ind].Pressed.Invoke();
			};
		}

		_addonSlotMenu.SetItemText(data.Index, obj.AddonName);

		PopupMenu menu = _addonSlotMenu.GetItemSubmenuNode(data.Index);
		data.IndexToToolItem.Clear();
		menu.Clear();

		// Create tool items
		int i = 0;
		foreach (AddonToolItem item in obj.ToolItems)
		{
			int myI = i++;
			menu.AddItem(item.Text);
			data.IndexToToolItem[menu.GetItemIndex(myI)] = item;
		}
	}

	public void RemoveAddonMenu(AddonObject obj)
	{
		if (
			_addonDataByRoot.TryGetValue(obj.Root, out var rootAddons)
			&& rootAddons.TryGetValue(obj.Identifier, out AddonMenuData? data)
		)
		{
			// Remove from UI if it's currently displayed
			if (obj.Root == _currentRoot && data.Visible)
			{
				_addonSlotMenu.RemoveItem(_addonSlotMenu.GetItemIndex(data.ItemId));
			}

			rootAddons.Remove(obj.Identifier);

			// Clean up empty root dictionaries
			if (rootAddons.Count == 0)
			{
				_addonDataByRoot.Remove(obj.Root);
			}
		}
	}

	public void SwitchTo(World? game)
	{
		bool disabled = game == null;

		try
		{
			foreach ((MenuButtonMenus mbtn, MenuItem[] items) in _menus)
			{
				if (mbtn.DevOnly)
					continue;
				if (mbtn.RequireGameOpen)
				{
					mbtn.Button.Disabled = disabled;
				}
				foreach (MenuItem item in items)
				{
					if (
						item is MenuButtonItem btnI
						&& (btnI.RequireGameOpen || btnI.RequiredBetaFeature != null)
					)
					{
						try
						{
							bool betaDisabled =
								btnI.RequiredBetaFeature != null
								&& !CreatorBetaFeatures.IsEnabled(btnI.RequiredBetaFeature);
							mbtn.Popup.SetItemDisabled(
								btnI.Index,
								btnI.RequireGameOpen && disabled || betaDisabled
							);
						}
						catch (Exception e)
						{
							GD.PrintErr($"Error setting menu item disabled: {e}");
						}
					}
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Error switching menu to game {game?.Name}: {e}");
		}

		// Switch addon menus to the new root
		SwitchAddonRoot(game);
	}

	private void SwitchAddonRoot(World? newRoot)
	{
		// Remove all current addon menus from UI
		if (
			_currentRoot != null
			&& _addonDataByRoot.TryGetValue(_currentRoot, out var currentAddons)
		)
		{
			foreach (var data in currentAddons.Values)
			{
				if (data.Visible)
				{
					_addonSlotMenu.RemoveItem(_addonSlotMenu.GetItemIndex(data.ItemId));
					data.Visible = false;
				}
			}
		}

		// Add addon menus for the new root to UI
		_currentRoot = newRoot;
		if (newRoot != null && _addonDataByRoot.TryGetValue(newRoot, out var newAddons))
		{
			foreach (var kvp in newAddons)
			{
				UpdateAddonMenuInUI(kvp.Value.AddonObject, kvp.Value);
			}
		}
	}

	private void OnBV(long idx)
	{
		switch (idx)
		{
			case 0: // About BrickVerse
				{
					CreatorService.Interface.PopupCredits();
					break;
				}
			case 1: // Startup splash
				{
					StartupSplash.Singleton.Show();
					break;
				}
		}
	}
}
