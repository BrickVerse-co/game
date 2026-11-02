// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using BrickVerse.Datamodel;

namespace BrickVerse.Creator.Spatial;

public interface IGizmo
{
	List<Dynamic> Targets { get; set; }
	bool Visible { get; set; }
	Gizmos? RootGizmos { get; set; }
	void RefreshVisuals();
}
