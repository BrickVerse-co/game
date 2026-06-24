// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace Polytoria.Creator.Utils;

public static class PolyCreatorAPI
{
	private static readonly PTHttpClient _client = new();

	public static int UserID { get; private set; } = 0;
	public static APIUserInfo UserInfo { get; private set; }
	public static string Token { get; private set; } = "";

	public static event Action<int>? LaunchPlaceRequest;
	public static event Action? UserAuthenticated;
	public static bool IsUserAuthenticated { get; private set; }

	public static void SetToken(string token)
	{
		Token = token;
		_client.DefaultRequestHeaders.Add("Authorization", token);
	}

	public static async Task LoginWithToken(string token)
	{
		SetToken(token);
		CreatorAuthResponse res = await _client.GetFromJsonAsync(Globals.ApiEndpoint.PathJoin("/v1/creator/token-data"), CreatorAPIGenerationContext.Default.CreatorAuthResponse);
		UserID = res.UserID;
		if (res.PlaceID.HasValue)
		{
			LaunchPlaceRequest?.Invoke(res.PlaceID.Value);
		}

		UserInfo = await PolyAPI.GetUserFromID(UserID);
		IsUserAuthenticated = true;
		UserAuthenticated?.Invoke();
	}

	public static async Task<CreatorPlaceItem[]> GetPublishedWorlds()
	{
		if (!IsUserAuthenticated) throw new AuthenticationException("User authentication required");
		using HttpResponseMessage msg = await _client.GetAsync(Globals.ApiEndpoint.PathJoin("/v1/creator/get-places"));
		msg.EnsureSuccessStatusCode();
		return await msg.Content.ReadFromJsonAsync(CreatorAPIGenerationContext.Default.CreatorPlaceItemArray) ?? [];
	}

	public static async Task<CreatorPublishResponse> UploadWorld(byte[] placeData, int placeID = 0, string mainWorldPath = "")
	{
		if (!IsUserAuthenticated) throw new AuthenticationException("User authentication required");
		using MultipartFormDataContent form = new()
		{
			{ new StringContent(placeID.ToString()), "id" },
			{ new StringContent(Token), "token" },
			{ new StringContent(mainWorldPath), "mainPlacePath" },
			{ new StringContent(Globals.MajorAppVersion), "majorVersion" },
		};

		ByteArrayContent fileContent = new(placeData);
		fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		form.Add(fileContent, "file", "level.ptpacked");

		using HttpResponseMessage msg = await _client.PostAsync(Globals.ApiEndpoint.PathJoin("/v1/creator/upload-place"), form);
		msg.EnsureSuccessStatusCode();
		return await msg.Content.ReadFromJsonAsync(CreatorAPIGenerationContext.Default.CreatorPublishResponse);
	}

	public static async Task<CreatorPublishResponse> UploadModel(byte[] modelData, int modelId = 0)
	{
		if (!IsUserAuthenticated) throw new AuthenticationException("User authentication required");
		using MultipartFormDataContent form = new()
		{
			{ new StringContent(modelId.ToString()), "id" },
			{ new StringContent(Token), "token" },
			{ new StringContent(Globals.MajorAppVersion), "majorVersion" },
		};

		ByteArrayContent fileContent = new(modelData);
		fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		form.Add(fileContent, "data", "model.ptmd");

		using HttpResponseMessage msg = await _client.PostAsync(Globals.ApiEndpoint.PathJoin("/v1/creator/upload-model"), form);
		msg.EnsureSuccessStatusCode();
		return await msg.Content.ReadFromJsonAsync(CreatorAPIGenerationContext.Default.CreatorPublishResponse);
	}
}
