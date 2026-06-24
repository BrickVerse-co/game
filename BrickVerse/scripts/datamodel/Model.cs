// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Attributes;
using BrickVerse.Datamodel.Interfaces;

namespace BrickVerse.Datamodel;

[Instantiable]
public sealed partial class Model : Dynamic, IGroup { }
