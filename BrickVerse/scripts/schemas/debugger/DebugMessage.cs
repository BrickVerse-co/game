// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using MemoryPack;
using static BrickVerse.Scripting.LogDispatcher;

namespace BrickVerse.Schemas.Debugger;

[MemoryPackable]
[MemoryPackUnion(0, typeof(MessageClientData))]
[MemoryPackUnion(1, typeof(MessageShutdown))]
[MemoryPackUnion(2, typeof(MessageLaunchWorld))]
[MemoryPackUnion(3, typeof(MessageNewServerRequest))]
[MemoryPackUnion(4, typeof(MessageNewServerResponse))]
[MemoryPackUnion(5, typeof(MessageServerReady))]
[MemoryPackUnion(6, typeof(MessageLogDispatch))]
[MemoryPackUnion(7, typeof(MessageObjPropChange))]
[MemoryPackUnion(8, typeof(MessageRuntimeSnapshotRequest))]
[MemoryPackUnion(9, typeof(MessageRuntimeSnapshot))]
[MemoryPackUnion(10, typeof(MessageRuntimePropertySet))]
[MemoryPackUnion(11, typeof(MessageRuntimeExecute))]
[MemoryPackUnion(12, typeof(MessageRuntimeRename))]
[MemoryPackUnion(13, typeof(MessageRuntimeViewportRect))]
[MemoryPackUnion(14, typeof(MessageRuntimeDeviceEmulation))]
[MemoryPackUnion(15, typeof(MessageRuntimeDiagnosticsRequest))]
[MemoryPackUnion(16, typeof(MessageRuntimeDiagnostics))]
public partial interface IDebugMessage
{
}

[MemoryPackable]
public partial class MessageClientData : IDebugMessage
{
	public string DebugID = "";
	public int ProcessID = 0;
	public bool IsServer;
	public int ClientID;
}

[MemoryPackable]
public partial class MessageShutdown : IDebugMessage { }


[MemoryPackable]
public partial class MessageLaunchWorld : IDebugMessage { }

[MemoryPackable]
public partial class MessageNewServerRequest : IDebugMessage
{
	public string WorldPath = "";
}

[MemoryPackable]
public partial class MessageNewServerResponse : IDebugMessage
{
	public string WorldPath = "";
	public string DebugID = "";
	public string Address = "";
	public int Port = 0;
}

[MemoryPackable]
public partial class MessageServerReady : IDebugMessage
{
}

[MemoryPackable]
public partial class MessageObjPropChange : IDebugMessage
{
	public string ObjectID = "";
	public string PropertyName = "";
	public byte[] PropertyValue = [];
}

[MemoryPackable]
public partial class MessageLogDispatch : IDebugMessage
{
	public LogTypeEnum LogType;
	public LogFromEnum LogFrom;
	public string Content = "";
	public string Source = "";
	public int SourceLine;
}

[MemoryPackable]
public partial class MessageRuntimeSnapshotRequest : IDebugMessage { }

[MemoryPackable]
public partial class MessageRuntimeSnapshot : IDebugMessage
{
	public RuntimeObjectInfo[] Objects = [];
}

[MemoryPackable]
public partial class RuntimeObjectInfo
{
	public string ObjectID = "";
	public string ParentObjectID = "";
	public string Name = "";
	public string ClassName = "";
	public RuntimePropertyInfo[] Properties = [];
}

[MemoryPackable]
public partial class RuntimePropertyInfo
{
	public string Name = "";
	public string TypeName = "";
	public string Value = "";
	public bool CanWrite;
}

[MemoryPackable]
public partial class MessageRuntimePropertySet : IDebugMessage
{
	public string ObjectID = "";
	public string PropertyName = "";
	public string Value = "";
}

[MemoryPackable]
public partial class MessageRuntimeExecute : IDebugMessage
{
	public string Source = "";
}

[MemoryPackable]
public partial class MessageRuntimeRename : IDebugMessage
{
	public string ObjectID = "";
	public string Name = "";
}

[MemoryPackable]
public partial class MessageRuntimeViewportRect : IDebugMessage
{
	public int X;
	public int Y;
	public int Width;
	public int Height;
	public bool Visible = true;
}

[MemoryPackable]
public partial class MessageRuntimeDeviceEmulation : IDebugMessage
{
	public string DeviceType = "PC";
	public bool Enabled;
	public bool Touchscreen;
	public bool Gamepad;
	public bool VR;
	public float LeftX;
	public float LeftY;
	public float RightX;
	public float RightY;
	public bool PrimaryButton;
	public bool SecondaryButton;
	public bool LeftTrigger;
	public bool RightTrigger;
	public float HeadYaw;
	public float HeadHeight = 1.7f;
	public float HandSpread = 0.45f;
}

[MemoryPackable]
public partial class MessageRuntimeDiagnosticsRequest : IDebugMessage { }

[MemoryPackable]
public partial class MessageRuntimeDiagnostics : IDebugMessage
{
	public double Fps;
	public double FrameTimeMs;
	public double PhysicsTimeMs;
	public long StaticMemoryBytes;
	public long VideoMemoryBytes;
	public int NodeCount;
	public int ObjectCount;
	public int DrawCalls;
	public int Active3DObjects;
	public string[] Scripts = [];
	public bool IsServer;
	public string NetworkMode = "Offline";
	public int Players;
	public int PingMs;
}
