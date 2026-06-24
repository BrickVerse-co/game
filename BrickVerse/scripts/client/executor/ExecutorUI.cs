// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using Polytoria.Shared.Misc;

namespace Polytoria.Client.Executor;

public partial class ExecutorUI : Window
{
	[Export] private CodeEdit _codeField = null!;
	[Export] private Button _runCompatBtn = null!;
	[Export] private Button _runBtn = null!;

	public override void _EnterTree()
	{
		CloseRequested += Hide;
		_runBtn.Pressed += OnRun;
		_runCompatBtn.Pressed += OnRunCompat;
		InputHelper h = new();
		h.GodotUnhandledInputEvent += UnhandledKeyInput;
		Globals.Singleton.AddChild(h);
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		CloseRequested -= Hide;
		_runBtn.Pressed -= OnRun;
		_runCompatBtn.Pressed -= OnRunCompat;
		base._ExitTree();
	}

	public void UnhandledKeyInput(InputEvent @event)
	{
		if (@event is InputEventKey k && k.Keycode == Key.F9 && k.IsReleased())
		{
			PopupCentered();
		}
	}

	private void OnRun()
	{
		Run(false);
	}

	private void OnRunCompat()
	{
		Run(true);
	}

	private void Run(bool compat)
	{
		string scriptSource = _codeField.Text;
		World g = World.Current!;

		var cs = g.New<ClientScript>(g.Environment);
		cs.Source = scriptSource;
		cs.Compatibility = compat;

		g.ScriptService.Run(cs);
	}
}
