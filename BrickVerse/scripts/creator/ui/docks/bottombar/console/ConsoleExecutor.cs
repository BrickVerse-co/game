// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Godot;
using BrickVerse.Shared;
using BrickVerse.Datamodel;
using BrickVerse.Datamodel.Creator;
using BrickVerse.Scripting;
using BrickVerse.Creator.Settings;

namespace BrickVerse.Creator.UI.Docks.BottomBar.Console;

public partial class ConsoleExecutor : HBoxContainer
{
	[Export]
	private LineEdit _codeField = null!;

	[Export]
	private Button _executeButton = null!;

	private bool _isExecuting;

	public override void _EnterTree()
	{
		_executeButton.Pressed += OnExecutePressed;
		_codeField.TextSubmitted += OnCodeSubmitted;

		base._EnterTree();
	}

	public override void _ExitTree()
	{
		_executeButton.Pressed -= OnExecutePressed;
		_codeField.TextSubmitted -= OnCodeSubmitted;

		base._ExitTree();
	}

	private async void OnExecutePressed()
	{
		await ExecuteAsync();
	}

	private async void OnCodeSubmitted(string _)
	{
		await ExecuteAsync();
	}

	private async Task ExecuteAsync()
	{
		if (_isExecuting)
			return;

		string scriptSource = _codeField.Text.Trim();

		if (string.IsNullOrWhiteSpace(scriptSource))
		{
			BV.PrintErr("Cannot execute Luau because the input is empty.");
			CreatorService.Interface.PopupAlert("Cannot execute Luau because the input is empty.", "Execution Error");
			return;
		}

		World? world = World.Current;

		if (world == null)
		{
			BV.PrintErr("Cannot execute Luau because no world is currently loaded.");
			CreatorService.Interface.PopupAlert("Cannot execute Luau because no world is currently loaded.", "Execution Error");
			return;
		}

		bool confirmed = true;
		if (CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Popups.ExecutorConfirmation))
		{
			confirmed = await CreatorService.Interface.PromptConfirmation(
				"Are you sure you want to execute this Luau code? This action may have unintended consequences.",
				"Execute Luau Code"
			);
		}

		if (!confirmed || !IsInsideTree())
			return;

		_isExecuting = true;
		_executeButton.Disabled = true;
		_codeField.Editable = false;

		Datamodel.ClientScript? script = null;
		bool succeeded = false;

		try
		{
			script = world.New<Datamodel.ClientScript>(world.Environment);
			script.Name = "ConsoleExecutor";
			script.Source = scriptSource;
			script.PermissionFlags =
				ScriptPermissionFlags.CreatorAccess |
				ScriptPermissionFlags.ContextAccess;
			script.Compatibility = false;

			script.Run();
			succeeded = true;

			_codeField.Clear();

			// Keep the script alive briefly so yielded/deferred Luau work can run.
			await ToSignal(
				GetTree().CreateTimer(3.0),
				SceneTreeTimer.SignalName.Timeout
			);
		}
		catch (Exception exception)
		{
			BV.PrintErr($"Failed to execute Luau: {exception}");
			CreatorService.Interface.PopupAlert($"Failed to execute Luau: {exception}", "Execution Error");
		}
		finally
		{
			if (script != null) script.Destroy();

			if (IsInstanceValid(this))
			{
				_isExecuting = false;
				_executeButton.Disabled = false;
				_codeField.Editable = true;
				_codeField.GrabFocus();
			}

			if (
				succeeded
				&& CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Popups.ExecutorSuccess)
			)
			{
				CreatorService.Interface.PopupAlert("Luau code executed successfully.", "Execution Complete");
			}
		}
	}
}
