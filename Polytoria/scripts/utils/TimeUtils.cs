// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Utils;

public static class TimeUtils
{
	public static string FormatSeconds(long sec)
	{
		TimeSpan time = TimeSpan.FromSeconds(sec);

		string result = "";
		if (time.Hours > 0)
			result += $"{time.Hours}h ";
		if (time.Minutes > 0 || time.Hours > 0)
			result += $"{time.Minutes}m ";
		result += $"{time.Seconds}s";

		return result.Trim();
	}
}
