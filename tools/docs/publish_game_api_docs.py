#!/usr/bin/env python3
"""Generate BrickVerse Engine API Markdown and merge community documentation.

The C# runtime owns signatures. The game-api repository owns prose and samples:

  docs/contributions/classes/<Class>/overview.md
  docs/contributions/classes/<Class>/{properties,methods,events}/<Member>.md
  docs/contributions/enums/<Enum>/overview.md
  docs/contributions/enums/<Enum>/values/<Value>.md
  docs/code-samples/classes/<Class>/overview/*.luau
  docs/code-samples/classes/<Class>/{properties,methods,events}/<Member>/*.luau

Only docs/api/types, docs/api/enums, the generated manifest, and copied API icons
are managed by this script. All other files in game-api remain untouched.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


CATEGORY_MEMBERS: dict[str, set[str]] = {
    "UI": {"GUI", "CreatorGUI", "PlayerGUI", "CoreUIService"},
    "Gameplay": {"Actor", "CharacterModel", "InteractionPrompt", "Inventory", "NPC", "Pawn", "Player", "Players", "SpawnLocation", "Team", "Teams", "Tool", "Vehicle", "VehicleSeat", "VehicleWheel", "Weapon"},
    "Scripting": {"BVCallback", "BVFunction", "BVSignal", "BVSignalConnection", "BindableEvent", "ClientScript", "LocalNetworkEvent", "ModuleScript", "NetMessage", "NetworkEvent", "Script", "ScriptService", "ScriptSharedTable", "ServerScript"},
}


class DocsError(RuntimeError):
    pass


def log(message: str) -> None:
    print(f"[game-api-docs] {message}", flush=True)


def run(command: list[str], cwd: Path, description: str, timeout: int = 300) -> None:
    log(description)
    log("$ " + subprocess.list2cmdline(command))
    try:
        subprocess.run(command, cwd=cwd, check=True, timeout=timeout)
    except (OSError, subprocess.SubprocessError) as exc:
        raise DocsError(f"{description} failed: {exc}") from exc


def infer_category(item: dict, classes: dict[str, dict], seen: set[str] | None = None) -> str:
    name = str(item.get("Name", ""))
    if name.startswith("UI") or name in CATEGORY_MEMBERS["UI"]:
        return "UI"
    if name.startswith(("Audio", "Sound", "VoiceChat")):
        return "Audio"
    if name.startswith(("Creator", "Addon")):
        return "Creator"
    if name.endswith("Service"):
        return "Services"
    if name.endswith("Asset"):
        return "Assets"
    if name.startswith(("Input", "VR")):
        return "Input"
    for category, members in CATEGORY_MEMBERS.items():
        if name in members:
            return category
    if name.endswith("Value") or name in {"Bounds", "Color", "ColorSeries", "NumberRange", "NumberSeries", "Quaternion", "RayResult", "Vector2", "Vector3"}:
        return "Data"
    if name.endswith("Constraint") or name in {"Attachment", "Beam", "Camera", "Dynamic", "Environment", "Light", "Lighting", "Mesh", "Model", "Part", "Physical", "RigidBody", "Sky", "Terrain", "Weld", "World"}:
        return "3D"
    base = item.get("BaseType")
    seen = seen or set()
    if base and base in classes and base not in seen:
        return infer_category(classes[base], classes, seen | {name})
    return "Core"


def clean_type(value: object) -> str:
    aliases = {"bool": "boolean", "Boolean": "boolean", "String": "string", "float": "number", "int": "number", "long": "number", "void": "nil", "Void": "nil", "object": "any", "Object": "any"}
    text = str(value or "any").strip()
    return aliases.get(text, text)


def contribution(docs_root: Path, kind: str, owner: str, section: str = "overview", member: str | None = None) -> str:
    base = docs_root / "contributions" / kind / owner
    path = base / f"{section}.md" if member is None else base / section / f"{member}.md"
    return path.read_text(encoding="utf-8").strip() if path.is_file() else ""


def samples(docs_root: Path, owner: str, section: str = "overview", member: str | None = None) -> str:
    base = docs_root / "code-samples" / "classes" / owner / section
    directory = base if member is None else base / member
    if not directory.is_dir():
        return ""
    blocks: list[str] = []
    for sample in sorted(directory.glob("*.luau")):
        title = sample.stem.replace("-", " ").replace("_", " ").title()
        source = sample.read_text(encoding="utf-8").rstrip()
        blocks.append(f"#### {title}\n\n```luau\n{source}\n```")
    return "\n\n".join(blocks)


def missing_doc(repo: str, kind: str, owner: str, section: str, member: str | None = None) -> str:
    suffix = f"/{section}/{member}.md" if member else f"/{section}.md"
    target = f"docs/contributions/{kind}/{owner}{suffix}"
    target_path = Path(target)
    url = f"https://github.com/{repo}/new/main/{target_path.parent.as_posix()}?filename={target_path.name}"
    return f"> Documentation for this API is incomplete. [Add documentation]({url})."


def render_body(docs_root: Path, repo: str, kind: str, owner: str, section: str, member: str | None = None) -> str:
    body = contribution(docs_root, kind, owner, section, member)
    sample_text = samples(docs_root, owner, section, member) if kind == "classes" else ""
    if not body:
        body = missing_doc(repo, kind, owner, section, member)
    return body + (f"\n\n{sample_text}" if sample_text else "")


def method_signature(method: dict) -> str:
    parameters = []
    for parameter in method.get("Parameters", []):
        optional = "?" if parameter.get("IsOptional") else ""
        parameters.append(f"{parameter.get('Name', 'value')}{optional}")
    return f"{method.get('Name', 'Method')}({', '.join(parameters)})"


def render_class(item: dict, category: str, docs_root: Path, repo: str) -> str:
    name = item["Name"]
    lines = ["---", f'title: "{name}"', f'category: "{category}"', "generated: true", "---", "", f"# {name}", ""]
    if item.get("BaseType"):
        lines += [f"**Inherits:** `{item['BaseType']}`", ""]
    flags = []
    if item.get("IsStatic"): flags.append("Static")
    if item.get("IsAbstract"): flags.append("Abstract")
    if item.get("IsInstantiable"): flags.append("Instantiable")
    if flags: lines += [f"**Kind:** {' · '.join(flags)}", ""]
    lines += [render_body(docs_root, repo, "classes", name, "overview"), ""]

    sections = (("Properties", "properties"), ("Methods", "methods"), ("Events", "events"))
    for source_key, section in sections:
        members = item.get(source_key, []) or []
        if not members:
            continue
        lines += [f"## {source_key}", ""]
        for member in members:
            member_name = str(member.get("Name", "Unknown"))
            heading = method_signature(member) if source_key == "Methods" else member_name
            lines += [f"### {heading}", ""]
            if member.get("IsStatic"): lines += ["**Static**", ""]
            if member.get("IsAsync"): lines += ["**Yields**", ""]
            if member.get("IsObsolete"): lines += ["> **Deprecated:** This API is retained for compatibility and should not be used in new code.", ""]
            if source_key == "Properties":
                lines += [f"**Type:** `{clean_type(member.get('Type'))}`", ""]
                if member.get("IsReadOnly"): lines += ["**Read-only**", ""]
                if member.get("IsAccessibleByScripts") is False: lines += ["**Not accessible from scripts**", ""]
            elif source_key == "Methods":
                parameters = member.get("Parameters", []) or []
                if parameters:
                    lines += ["#### Parameters", ""]
                    for parameter in parameters:
                        optional = " — optional" if parameter.get("IsOptional") else ""
                        default = f"; default: `{parameter['DefaultValue']}`" if parameter.get("DefaultValue") not in (None, "") else ""
                        lines.append(f"- `{parameter.get('Name', 'value')}`: `{clean_type(parameter.get('Type'))}`{optional}{default}")
                    lines.append("")
                lines += [f"**Returns:** `{clean_type(member.get('ReturnType'))}`", ""]
            elif source_key == "Events":
                parameters = member.get("Parameters", []) or []
                lines += ["**Type:** `BVSignal`", ""]
                if parameters:
                    lines += ["#### Parameters", ""]
                    for parameter in parameters:
                        lines.append(f"- `{parameter.get('Name', 'value')}`: `{clean_type(parameter.get('Type'))}`")
                    lines.append("")
            lines += [render_body(docs_root, repo, "classes", name, section, member_name), ""]
    return "\n".join(lines).rstrip() + "\n"


def render_enum(item: dict, docs_root: Path, repo: str) -> str:
    name = item["Name"]
    lines = ["---", f'title: "{name}"', 'category: "Enums"', "generated: true", "---", "", f"# {name}", "", render_body(docs_root, repo, "enums", name, "overview"), "", "## Values", ""]
    options = item.get("Options", []) or item.get("Values", []) or []
    for option in options:
        value = str(option.get("Name")) if isinstance(option, dict) else str(option)
        lines += [f"### {name}.{value}", "", render_body(docs_root, repo, "enums", name, "values", value), ""]
    return "\n".join(lines).rstrip() + "\n"


def replace_tree(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(source, destination)


def ensure_contribution_guides(docs_root: Path) -> None:
    guides = {
        docs_root / "contributions" / "README.md": """# Contributing API documentation

Generated signatures come from the BrickVerse Engine and must not be edited by hand. Add durable prose using:

- `classes/<Class>/overview.md`
- `classes/<Class>/properties/<Property>.md`
- `classes/<Class>/methods/<Method>.md`
- `classes/<Class>/events/<Event>.md`
- `enums/<Enum>/overview.md`
- `enums/<Enum>/values/<Value>.md`

These Markdown fragments are inserted into generated pages on every Engine publish. Ordinary documentation outside `docs/api/types` and `docs/api/enums` is fully hand-editable.
""",
        docs_root / "code-samples" / "README.md": """# Contributing Luau samples

Place runnable `.luau` files under:

- `classes/<Class>/overview/`
- `classes/<Class>/methods/<Method>/`
- `classes/<Class>/properties/<Property>/`
- `classes/<Class>/events/<Event>/`

The filename becomes the example heading. Samples are inserted beside the matching API member and survive regeneration.
""",
    }
    for path, text in guides.items():
        if not path.exists():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")


def generate_llms_index(docs_root: Path) -> None:
    lines = [
        "# BrickVerse Engine Documentation",
        "",
        "> Official, community-editable BrickVerse Engine API documentation.",
        "",
        "## Documentation",
        "",
    ]
    for page in sorted(docs_root.rglob("*.md")):
        relative = page.relative_to(docs_root)
        if any(part.startswith(".") for part in relative.parts) or relative.parts[0] in {"contributions", "code-samples"}:
            continue
        source = page.read_text(encoding="utf-8", errors="replace")
        title_match = next((line[2:].strip() for line in source.splitlines() if line.startswith("# ")), None)
        title = title_match or page.stem.replace("-", " ").title()
        route = relative.with_suffix("").as_posix()
        if route.endswith("/README"):
            route = route[:-7].rstrip("/")
        lines.append(f"- [{title}](https://brickverse.gg/creator/docs/{route})")
    (docs_root / "llms.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


def generate(api_reference: Path, docs_root: Path, icon_source: Path | None, repo: str) -> None:
    try:
        data = json.loads(api_reference.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise DocsError(f"Invalid API reference: {api_reference}") from exc
    classes_list = data.get("Classes")
    enums = data.get("Enums")
    if not isinstance(classes_list, list) or not classes_list or not isinstance(enums, list):
        raise DocsError("API reference must contain non-empty Classes and an Enums array.")
    ensure_contribution_guides(docs_root)
    classes = {item["Name"]: item for item in classes_list}
    with tempfile.TemporaryDirectory(prefix="brickverse-docs-") as temporary:
        stage = Path(temporary)
        type_root = stage / "types"
        enum_root = stage / "enums"
        for item in classes_list:
            category = infer_category(item, classes)
            output = type_root / category / f"{item['Name']}.md"
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(render_class(item, category, docs_root, repo), encoding="utf-8")
        for item in enums:
            output = enum_root / f"{item['Name']}.md"
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(render_enum(item, docs_root, repo), encoding="utf-8")
        replace_tree(type_root, docs_root / "api" / "types")
        replace_tree(enum_root, docs_root / "api" / "enums")
    if icon_source and icon_source.is_dir():
        icon_target = docs_root / ".gitbook" / "assets" / "api-icons"
        icon_target.mkdir(parents=True, exist_ok=True)
        for icon in icon_source.glob("*.svg"):
            shutil.copy2(icon, icon_target / icon.name)
    manifest = {
        "engineVersion": data.get("Version"),
        "classes": len(classes_list),
        "enums": len(enums),
        "source": "BrickVerse-co/game",
    }
    manifest_path = docs_root / "api" / "generated-manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    generate_llms_index(docs_root)
    log(f"Generated {len(classes_list)} classes and {len(enums)} enums in {docs_root}")


def parse_args() -> argparse.Namespace:
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-reference", type=Path, default=root / "BrickVerse" / "apiref.json")
    parser.add_argument("--docs-root", type=Path, required=True, help="Path to game-api/docs.")
    parser.add_argument("--icon-source", type=Path, default=root / "BrickVerse" / "assets" / "textures" / "datamodel")
    parser.add_argument("--repository", default="BrickVerse-co/game-api")
    parser.add_argument("--refresh-api", action="store_true", help="Build the Engine and run Godot --genapi first.")
    parser.add_argument("--godot", type=Path, help="Godot .NET executable used with --refresh-api.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = Path(__file__).resolve().parents[2]
    if args.refresh_api:
        godot = args.godot or Path(shutil.which("godot") or shutil.which("godot4") or "")
        if not godot.is_file():
            raise DocsError("Godot .NET was not found. Pass --godot with --refresh-api.")
        run(["dotnet", "build", "BrickVerse/BrickVerse.csproj", "--no-restore"], root, "Building Engine API")
        run([str(godot), "--headless", "--path", "BrickVerse", "--", "--genapi"], root, "Generating apiref.json", 180)
    generate(args.api_reference.resolve(), args.docs_root.resolve(), args.icon_source.resolve(), args.repository)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except DocsError as exc:
        print(f"[game-api-docs] ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
