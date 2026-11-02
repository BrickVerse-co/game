using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BrickVerse.Mobile.Utils;
using BrickVerse.Shared;

namespace BrickVerse.Tests;

public sealed class ClientAuthenticationTests
{
	[Theory]
	[InlineData("join-token", false)]
	[InlineData("", true)]
	[InlineData("   ", true)]
	public void MobileLaunchOnlyOpensShellWithoutJoinToken(string token, bool opensShell)
	{
		Assert.Equal(
			opensShell,
			AppEntry.ShouldOpenAppShell(
				true,
				false,
				new Dictionary<string, string> { ["token"] = token }
			)
		);
	}

	[Fact]
	public void DedicatedServerNeverOpensMobileShell()
	{
		Assert.False(
			AppEntry.ShouldOpenAppShell(
				true,
				false,
				new Dictionary<string, string> { ["network"] = "server" }
			)
		);
	}

	[Fact]
	public void StandaloneClientCanOpenShell()
	{
		Assert.True(AppEntry.ShouldOpenAppShell(false, true, new Dictionary<string, string>()));
	}

	[Fact]
	public void OnlyAnUnauthorizedResponseInvalidatesSavedSession()
	{
		Assert.True(
			BVMobileAuthAPI.IsSessionRejected(
				new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized)
			)
		);
		foreach (
			Exception error in new Exception[]
			{
				new HttpRequestException("Offline"),
				new HttpRequestException("Unavailable", null, HttpStatusCode.ServiceUnavailable),
				new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden),
				new TaskCanceledException("Timeout"),
				new JsonException("Invalid response"),
				new System.IO.IOException("Storage failure"),
			}
		)
		{
			Assert.False(BVMobileAuthAPI.IsSessionRejected(error), error.Message);
		}
	}
}
