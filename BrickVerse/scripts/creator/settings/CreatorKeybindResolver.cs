// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace BrickVerse.Creator.Settings;

public static class CreatorKeybindResolver
{
    public static Key Resolve(string settingKey, Key fallback)
    {
        if (CreatorSettingsService.Instance == null)
            return fallback;

        string raw = CreatorSettingsService.Instance.Get<string>(settingKey).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (Enum.TryParse(raw, true, out Key namedKey))
            return namedKey;

        if (long.TryParse(raw, out long numericKey))
            return (Key)numericKey;

        return fallback;
    }

    public static bool IsPressed(InputEvent @event, string settingKey, Key fallback)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
            return false;

        Key expected = Resolve(settingKey, fallback);
        return keyEvent.Keycode == expected || keyEvent.PhysicalKeycode == expected;
    }
}
