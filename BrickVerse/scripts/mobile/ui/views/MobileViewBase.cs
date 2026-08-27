// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace BrickVerse.Mobile.UI;

public partial class MobileViewBase : Control
{
	protected void ApplyResponsiveMaxWidth(Control content, float maxWidth = 1120f, float horizontalMargin = 16f)
	{
		float top = content.OffsetTop;
		float bottom = content.OffsetBottom;
		void UpdateWidth()
		{
			if (!IsInstanceValid(content)) return;
			float available = Mathf.Max(0f, Size.X - horizontalMargin * 2f);
			float width = Mathf.Min(maxWidth, available);
			content.AnchorLeft = 0.5f;
			content.AnchorRight = 0.5f;
			content.OffsetLeft = -width / 2f;
			content.OffsetRight = width / 2f;
			content.OffsetTop = top;
			content.OffsetBottom = bottom;
		}

		Resized += UpdateWidth;
		Callable.From(UpdateWidth).CallDeferred();
	}

	public virtual void ShowView(object? args)
	{

	}

	public virtual void HideView()
	{

	}

	public virtual void RefreshView() { }

	public virtual bool TryNavigateAway(System.Action continuation) => true;
}
