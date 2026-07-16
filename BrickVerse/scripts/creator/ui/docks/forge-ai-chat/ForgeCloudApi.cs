// Proposed BrickVerse Forge hosted API contract.
// Base: https://api.brickverse.gg/api/v3/forge-llm
namespace BrickVerse.Creator.UI;

public static class ForgeCloudRoutes
{
	public const string Base = "https://api.brickverse.gg/api/v3/forge-llm";
	public const string CreateChat = Base + "/chats";                 // POST
	public const string GetChats = Base + "/chats";                    // GET ?cursor=&limit=
	public static string GetChat(string id) => Base + "/chats/" + id; // GET
	public static string UpdateChat(string id) => Base + "/chats/" + id; // PATCH { title, archived }
	public static string DeleteChat(string id) => Base + "/chats/" + id; // DELETE
	public static string GetMessages(string id) => Base + "/chats/" + id + "/messages"; // GET
	public static string Complete(string id) => Base + "/chats/" + id + "/completions"; // POST, supports tools/stream
	public const string Models = Base + "/models";                     // GET availability/capabilities
	public const string Usage = Base + "/usage";                       // GET quota and reset time
	public const string Entitlement = Base + "/entitlement";           // GET Astro tier/access
	public const string RateLimits = Base + "/rate-limits";             // GET current request/token limits
}

public sealed class ForgeCloudUsage
{
	public int RequestsUsed { get; set; }
	public int RequestsLimit { get; set; }
	public int TokensUsed { get; set; }
	public int TokensLimit { get; set; }
	public System.DateTime? ResetsAt { get; set; }
	public string Subscription { get; set; } = "None";
	public bool HasForgeFreeAccess { get; set; }
}
