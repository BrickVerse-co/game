using System.Text.Json;
using BrickVerse.Creator.UI;
using BrickVerse.Datamodel;

var server = new ForgeMcpServer();
JsonElement Call(string method, object? parameters = null) =>
    JsonDocument.Parse(server.Handle(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }))!).RootElement;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
Check(JsonDocument.Parse(server.Handle("{")!).RootElement.GetProperty("error").GetProperty("code").GetInt32() == -32700, "parse error");
Check(Call("tools/list").GetProperty("error").GetProperty("code").GetInt32() == -32002, "initialization gate");
Check(server.Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}""") == null, "notifications");
Check(Call("initialize").GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-06-18", "protocol negotiation");
Check(!Call("tools/list").GetRawText().Contains("run_luau"), "execution excluded");
Check(Call("tools/call", new { name = "run_luau" }).TryGetProperty("error", out _), "execution rejected");
Check(Call("tools/call", new { name = "inspect_instance" }).GetProperty("result").GetProperty("isError").GetBoolean(), "no world");
World.Current = new World();
Check(Call("tools/call", new { name = "create_instance" }).GetProperty("result").GetProperty("isError").GetBoolean(), "inspect before mutation");
Check(!Call("tools/call", new { name = "inspect_instance" }).GetProperty("result").GetProperty("isError").GetBoolean(), "inspect world");
Check(!Call("tools/call", new { name = "create_instance" }).GetProperty("result").GetProperty("isError").GetBoolean(), "mutation after inspection");
World.Current = new World();
Check(Call("tools/call", new { name = "create_instance" }).GetProperty("result").GetProperty("isError").GetBoolean(), "world switch invalidates mutations");
Console.WriteLine("MCP protocol: 10 checks passed.");

// Godot-independent doubles exercise the actual protocol dispatcher and its world-change boundary.
namespace BrickVerse.Datamodel { internal class World { public static World? Current { get; set; } } }
namespace BrickVerse.Creator.UI
{
    internal record Function(string Name, string Description, JsonElement Parameters);
    internal record Definition(Function Function);
    internal static class ForgeToolCatalog
    {
        public static Definition[] Definitions = new[] { "inspect_instance", "create_instance", "run_luau" }
            .Select(name => new Definition(new Function(name, name, JsonDocument.Parse("{}").RootElement))).ToArray();
    }
    internal sealed class ForgeToolExecutor(BrickVerse.Datamodel.World world)
    {
        public string Execute(string name, string arguments) => world != null ? "ok" : throw new InvalidOperationException();
    }
}

