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

## Tool transport

ForgeMcpServer implements MCP JSON-RPC initialize, ping, tools/list and tools/call
over Godot CEF custom IPC. This is not a public TCP or stdio listener.
World operations run on the Godot main thread using ForgeToolExecutor, including
its existing history, script diffs and rollback. Changing worlds replaces the
executor. Direct Luau execution is not advertised. Windows adds run_shell for PowerShell/cmd.

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
