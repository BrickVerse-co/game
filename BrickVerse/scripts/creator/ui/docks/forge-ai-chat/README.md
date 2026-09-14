# Forge Web in Creator

Creator loads https://forge.brickverse.gg?creator_landing=1 for the production API.
With a non-production API it loads http://localhost:3000/forge-ai-chat?creator_landing=1.
The IPC origin allowlist follows this choice; browser security remains enabled.
The native chat scene is replaced by the browser host. Sign in to Forge in
the embedded browser. Both environments share web UI, conversations and models.

## Runtime

On Windows x64 run: pwsh -File bin_scripts/install-godot-cef.ps1
Then restart Godot. The entire locally installed CEF addon and root release archives are gitignored. Creator CI
installs the official pinned archive for all desktop exports. Non-Creator presets
exclude the addon. Keep CEF dependencies beside the exported app as specified in
the upstream gdextension. Documentation: https://godotcef.org/api/

## Shared MCP server

`ForgeMcpServer` is the single protocol core and `ForgeToolCatalog` is the single
tool registry. Forge's CEF bridge and the optional Custom MCP HTTP endpoint are
only transports around that core; do not add tools or policy to either transport.
The core implements MCP JSON-RPC `initialize`, `ping`, `tools/list`, `tools/call`,
and cancellation notifications.

World operations run on the Godot main thread using ForgeToolExecutor, including
its existing history, script diffs and rollback. Changing worlds replaces the
executor. Direct Luau execution is not advertised. Windows adds run_shell for PowerShell/cmd.

### Connect an external client

1. Open a world in Creator.
2. Choose **Tools > Custom MCP Server** and select **Start server**.
3. Choose a client format and use **Copy config**. For Codex, run the copied
   `codex mcp add brickverse-creator --url "..."` command, or paste the TOML into
   `~/.codex/config.toml`. For Cursor, merge the copied server entry into its MCP
   JSON configuration.
4. Keep Creator open while using the client. Restart the external client if it
   does not refresh its MCP tool list automatically.

The endpoint uses Streamable HTTP at `/mcp`. It binds to `127.0.0.1` only and
includes a random session token in the copied URL. The token changes when Creator
restarts. Do not share the URL. Stop the server from the same window when finished.
All requests are marshalled onto Godot's main thread. The external server exposes
the same inspection, tree, metadata, instance, property, script, diff and rollback
tools as embedded Forge. World edits retain the same inspect-before-edit guard.

Script creation is one atomic `create_instance` call. Use `ServerScript`,
`ClientScript`, or `ModuleScript` (`Script` aliases `ServerScript`) and provide the
final source in the top-level `source` argument. Creator writes the corresponding
`.server.luau`, `.client.luau`, or `.luau` project file and links the instance.
If file creation fails, the partially inserted instance and file are removed.
`edit_script_source` updates the linked file. Deleting a linked Script also moves
its project file to the recycle bin. `manage_project_file` supports bounded list,
read, write/create, and recycle-bin deletion for other project files; use the
script instance tools for scripts so world/file links remain synchronized.

The signed-in web UI registers the discovered tool catalog with the authenticated
backend. Redis holds user-owned sessions (300-second lease), one outstanding call
per session and bounded, expiring results. The model loop calls Creator through
that relay; requests cannot choose an arbitrary network endpoint.
In a regular browser, choose Connect Creator while the same account has Forge
open in Creator. The most recently active editor is selected.

Ask/plan expose read-only tools. Edit/agent/goal enable mutations unless the policy
is Deny. Allow once displays exact arguments in the embedded UI; Always allow
follows the existing Forge preference. Late approval never executes an expired
request. After ambiguous failures inspect the world before retrying a mutation.
Approval requests from browser conversations appear in Creator.

Deploy frontend and backend together before testing against the hosted URL.
Production requires shared Redis. A signed-in smoke test should inspect a world,
create a Part, create/edit a Script, inspect its diff, roll back, decline an edit,
close the world, and verify expired calls fail without being repeated.

## Shell commands and approval

run_shell requires an open project, shell (powershell or cmd), exact command and
reason. Its working_directory is resolved inside the project, but the command
runs with normal user permissions and is not filesystem-sandboxed. Every command
requires a native Creator confirmation showing the source, reason, directory and
timeout. No web approval flag, Always allow setting, or session grant bypasses it.
Approval expires after 90 seconds. Execution is limited to 60 seconds, output is
bounded, and cancellation attempts to terminate the process tree. Results include
stdout, stderr, exit code and whether cancellation/timeout occurred. Commands
cannot undo external effects; inspect partial changes before retrying.

Web world-edit approval uses a modal dialog with keyboard focus, Escape to decline,
a countdown, exact arguments, and allow-once or identical-request session grants.
Grants are cleared on inspection, errors or reconnect. Shell approval stays native.
Creator activity shows recent results and command output. Relay requests expire
after 170 seconds; disconnect/cancellation is forwarded to native shell execution.
