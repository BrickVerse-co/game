// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#pragma warning disable
using System.Collections.Generic;

namespace Discord
{
	public partial class StoreManager
	{
		public IEnumerable<Entitlement> GetEntitlements()
		{
			var count = CountEntitlements();
			var entitlements = new List<Entitlement>();
			for (var i = 0; i < count; i++)
			{
				entitlements.Add(GetEntitlementAt(i));
			}
			return entitlements;
		}

		public IEnumerable<Sku> GetSkus()
		{
			var count = CountSkus();
			var skus = new List<Sku>();
			for (var i = 0; i < count; i++)
			{
				skus.Add(GetSkuAt(i));
			}
			return skus;
		}
	}
}
