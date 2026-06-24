// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;

namespace Polytoria.Utils;

public static class PathUtils
{
	public static bool IsPathInsideDirectory(string path, string directory)
	{
		try
		{
			// Resolve path relative to directory
			string fullPath = Path.GetFullPath(Path.Combine(directory, path)).SanitizePath();
			string fullDirectory = Path.GetFullPath(directory).SanitizePath();

			return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			// Invalid path format
			return false;
		}
	}
}
