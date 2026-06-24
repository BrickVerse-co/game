// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#pragma warning disable
using System.Collections.Generic;

namespace Discord
{
	public partial class StorageManager
	{
		public IEnumerable<FileStat> Files()
		{
			var fileCount = Count();
			var files = new List<FileStat>();
			for (var i = 0; i < fileCount; i++)
			{
				files.Add(StatAt(i));
			}
			return files;
		}
	}
}
