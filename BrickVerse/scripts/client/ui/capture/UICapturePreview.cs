// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Services;

namespace Polytoria.Client.UI.Capture;

public partial class UICapturePreview : Control
{
	public CoreUIRoot CoreUI = null!;

	[Export] private TextureRect _pictureRect = null!;
	[Export] private Button _firstShareBtn = null!;
	[Export] private Button _saveBtn = null!;
	[Export] private Button _firstCloseBtn = null!;
	[Export] private Button _shareBtn = null!;
	[Export] private Button _cancelBtn = null!;
	[Export] private Control _captionWritePage = null!;
	[Export] private Control _firstMenuPage = null!;
	[Export] private TextEdit _captionTextEdit = null!;
	[Export] private AnimationPlayer _animPlay = null!;
	[Export] private AnimationPlayer _shareAnimPlay = null!;

	private CaptureService _capture = null!;

	public override void _Ready()
	{
		_capture = CoreUI.Root.Capture;
		Visible = false;
		_cancelBtn.Pressed += Close;
		_firstCloseBtn.Pressed += Close;
		_saveBtn.Pressed += OnSaveBtnPressed;
		_firstShareBtn.Pressed += OnFirstShareBtnPressed;
		_shareBtn.Pressed += OnShareBtnPressed;
		base._Ready();
	}

	private void OnShareBtnPressed()
	{
		Close();
		_capture.UploadCurrentPhoto(_captionTextEdit.Text);
	}

	private void OnFirstShareBtnPressed()
	{
		_shareAnimPlay.Play("share_appear");
		_firstMenuPage.Visible = false;
		_captionWritePage.Visible = true;
	}

	private void OnSaveBtnPressed()
	{
		_capture.SaveCurrentPhoto();
		_capture.OpenCurrentPhotoFile();
	}

	public void Open()
	{
		CoreUI.CoreUIActive = true;
		_captionTextEdit.Text = "";
		_firstMenuPage.Visible = true;
		_captionWritePage.Visible = false;
		_shareBtn.GrabFocus();
		_pictureRect.Texture = _capture.CurrentPhoto;
		_saveBtn.Visible = _capture.CurrentPhotoPath == null;
		_animPlay.Play("appear");
	}

	public void Close()
	{
		CoreUI.CoreUIActive = false;
		_animPlay.Play("disappear");
	}
}
