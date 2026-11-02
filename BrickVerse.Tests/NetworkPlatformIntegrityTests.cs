// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this file,
// You can obtain one at https://mozilla.org/MPL/2.0/.

using BrickVerse.Datamodel.Services;

namespace BrickVerse.Tests;

public sealed class NetworkPlatformIntegrityTests
{
	[Theory]
	[InlineData(NetworkService.ClientPlatformEnum.Desktop, "windows")]
	[InlineData(NetworkService.ClientPlatformEnum.Desktop, "linux")]
	[InlineData(NetworkService.ClientPlatformEnum.Desktop, "macos")]
	[InlineData(NetworkService.ClientPlatformEnum.Mobile, "android")]
	[InlineData(NetworkService.ClientPlatformEnum.Mobile, "ios")]
	[InlineData(NetworkService.ClientPlatformEnum.Tablet, "android")]
	[InlineData(NetworkService.ClientPlatformEnum.Tablet, "ios")]
	public void CompatiblePlatformClaimsAreAccepted(
		NetworkService.ClientPlatformEnum platform,
		string operatingSystem)
	{
		Assert.True(NetworkService.IsPlatformClaimConsistent(platform, operatingSystem));
	}

	[Theory]
	[InlineData(NetworkService.ClientPlatformEnum.Mobile, "windows")]
	[InlineData(NetworkService.ClientPlatformEnum.Tablet, "windows")]
	[InlineData(NetworkService.ClientPlatformEnum.Desktop, "android")]
	[InlineData(NetworkService.ClientPlatformEnum.Desktop, "ios")]
	[InlineData(NetworkService.ClientPlatformEnum.Desktop, "unknown")]
	public void ContradictoryPlatformClaimsAreRejected(
		NetworkService.ClientPlatformEnum platform,
		string operatingSystem)
	{
		Assert.False(NetworkService.IsPlatformClaimConsistent(platform, operatingSystem));
	}
}
