// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Polytoria.Networking.RateLimiters;

public class SlidingWindowRateLimiter(int maxMessages, TimeSpan timeWindow)
{
	private readonly Queue<DateTime> _timestamps = new();
	private readonly int _maxMessages = maxMessages;
	private readonly TimeSpan _timeWindow = timeWindow;
	private readonly Lock _lock = new();

	public bool TryAccept()
	{
		lock (_lock)
		{
			DateTime now = DateTime.UtcNow;
			DateTime cutoff = now - _timeWindow;

			// Remove old timestamps
			while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
			{
				_timestamps.Dequeue();
			}

			// Check if under limit
			if (_timestamps.Count >= _maxMessages)
			{
				return false;
			}

			_timestamps.Enqueue(now);
			return true;
		}
	}

	public int GetCurrentCount()
	{
		lock (_lock)
		{
			DateTime cutoff = DateTime.UtcNow - _timeWindow;
			while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
			{
				_timestamps.Dequeue();
			}
			return _timestamps.Count;
		}
	}
}
