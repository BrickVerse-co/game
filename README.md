# BrickVerse Game

[![License: MPL 2.0](https://img.shields.io/badge/License-MPL_2.0-brightgreen.svg)](https://opensource.org/licenses/MPL-2.0)
[![Godot Engine](https://img.shields.io/badge/Godot-4.7-blue?logo=godot-engine)](https://godotengine.org/)
[![.NET](https://img.shields.io/badge/.NET-10-purple?logo=dotnet)](https://dotnet.microsoft.com/)

BrickVerse is an open-source, cross-platform multiplayer game engine and client built with the Godot Engine and .NET 10.

It provides the foundation for creating and playing multiplayer experiences on the BrickVerse platform, featuring server-authoritative networking, synchronized state replication, and scripting powered by Luau. Games created with BrickVerse can be published to **https://brickverse.gg** and played across Windows, macOS, Linux, and mobile devices.

## Features

* Cross-platform support for Windows, macOS, Linux, and mobile
* Multiplayer-first, server-authoritative architecture
* High-performance engine built with Godot 4 and .NET 10
* Networked object replication and synchronization
* Luau scripting for gameplay and game logic
* Integrated publishing and cloud services through BrickVerse

## Contributing

We welcome contributions from the community. Please read the [Contributor Guidelines](CONTRIBUTING.md) before opening an issue or submitting a pull request.

### Client authentication and join checks

`-token=<joinToken> -network=client` launches directly into a game, including
builds using the mobile UI. This token authorizes the game connection; it is
never used as the app's saved login credential. Leaving the game opens the
mobile shell, which validates its own saved session or asks the user to sign in.
Only an HTTP 401 from session validation clears that saved credential. Network
and server failures retain it for another attempt when the app is reopened.
The Android HTTP bridge lives under `Globals` so shell/game transitions do not
destroy pending requests or remove the game's HTTP transport.

Build and run the unit regressions with .NET 10:

```sh
dotnet build BrickVerse.sln
dotnet test --project BrickVerse.Tests/BrickVerse.Tests.csproj --no-build
```

For a real local ENet handshake, use a Godot 4.7 .NET executable after the Debug
build:

```sh
python3 tests/ClientJoin/run.py /path/to/godot
```

This starts an empty local server and client and requires the player-ready
handshake within 45 seconds. It prints the captured log directory and stops
both processes. It needs no account or backend; avatar/thumbnail HTTP errors
are expected if the development backend is unavailable. It catches missing
replication payloads, invalid camera defaults, and world/service ID collisions.
Platform sign-in and production admission still need validation with an account
on the target device. When diagnosing a production join, check server auth logs
and client `JoinFailure` reports; a connected client also times out if no initial
world data arrives within 30 seconds.

## License

Unless otherwise noted:

* Source code is licensed under the Mozilla Public License 2.0. See [LICENSE](LICENSE) for details.
* The BrickVerse name, logos, artwork, branding, and trademarks are proprietary of Meta Games LLC and are not licensed for reuse.
* Third-party libraries, assets, and native binaries are subject to their own respective licenses.

## Contributors
<a href="https://github.com/BrickVerse-co/game/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=BrickVerse-co/game" />
</a>

Made with [contrib.rocks](https://contrib.rocks).
