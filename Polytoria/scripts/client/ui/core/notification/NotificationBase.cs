// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Notification;

public partial class UINotificationBase : Control
{
	public UINotification NotificationCenter = null!;
	public virtual void Fire(object? data = null) { }
}
