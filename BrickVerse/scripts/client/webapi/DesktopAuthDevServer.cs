// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using BrickVerse.Shared;
using System;
using System.Net;
using System.Threading.Tasks;

namespace BrickVerse.Client.WebAPI;

/// <summary>
/// Development-only helper that emulates brickverse:// deep links when running inside the Godot editor.
/// Starts a tiny HTTP listener on http://localhost:42424/auth?token=xxx
/// The website (or a manual curl) can POST/GET to this endpoint during playtesting.
/// </summary>
public static class DesktopAuthDevServer
{
    private static HttpListener? _listener;
    private static bool _running;

    public static void StartIfEditor()
    {
        if (!Globals.IsInGDEditor) return;
        if (_running) return;

        _running = true;
        _ = Task.Run(RunListener);
        PT.Print("DesktopAuthDevServer listening on http://localhost:42424/auth?token=...");
    }

    private static async Task RunListener()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://localhost:42424/auth/");
        _listener.Start();

        while (_running)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                var query = ctx.Request.QueryString;
                string? token = query["token"];

                if (!string.IsNullOrWhiteSpace(token))
                {
                    Callable.From(async () =>
                    {
                        await PolyDesktopAuthAPI.LoginWithAuthToken(token);
                    }).CallDeferred();

                    ctx.Response.StatusCode = 200;
                    byte[] buf = System.Text.Encoding.UTF8.GetBytes("OK");
                    ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                }
                else
                {
                    ctx.Response.StatusCode = 400;
                }
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                if (_running)
                    GD.PushError($"DesktopAuthDevServer error: {ex.Message}");
            }
        }
    }

    public static void Stop()
    {
        _running = false;
        _listener?.Stop();
        _listener = null;
    }
}
