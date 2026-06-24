// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using QRCoder;

namespace Polytoria.Creator.UI.Popups;

public sealed partial class LinkDevicePopup : PopupWindowBase
{
	[Export] private TextureRect _textureRect = null!;

	public override void _Ready()
	{
		string? connectAddress = DeviceLinker.GetConnectAddress();
		if (connectAddress == null) return;
		PT.Print(connectAddress);
		byte[] qrCodeImage = PngByteQRCodeHelper.GetQRCode(connectAddress, QRCodeGenerator.ECCLevel.Q, 20);

		Image image = new();
		image.LoadPngFromBuffer(qrCodeImage);

		ImageTexture t = ImageTexture.CreateFromImage(image);
		_textureRect.Texture = t;

		base._Ready();
	}
}
