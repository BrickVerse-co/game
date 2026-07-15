from __future__ import annotations

import argparse
import re
from pathlib import Path

import emoji

# Usage:
## py C:\Users\tyran\OneDrive\Desktop\BrickVerseEngine\bin_scripts\rename_twemoji.py "C:\Users\tyran\OneDrive\Desktop\BrickVerseEngine\BrickVerse\assets\textures\client\emojis" --apply


CODEPOINT_FILENAME = re.compile(
    r"^[0-9a-f]+(?:-[0-9a-f]+)*$",
    re.IGNORECASE,
)


def filename_to_emoji(filename: str) -> str | None:
    """
    Convert a Twemoji filename such as:

        1f600.png
        1f91f-1f3fb.png
        1f93d-1f3fb-200d-2640-fe0f.png

    into its Unicode emoji sequence.
    """
    stem = Path(filename).stem

    if not CODEPOINT_FILENAME.fullmatch(stem):
        return None

    try:
        return "".join(chr(int(codepoint, 16)) for codepoint in stem.split("-"))
    except (ValueError, OverflowError):
        return None


def emoji_to_readable_name(value: str) -> str | None:
    """
    Convert an emoji into an alias-style name.

    Example:
        😄 -> smile
    """
    # "alias" prefers familiar shortcode aliases where available.
    name = emoji.demojize(value, language="alias")

    if name == value:
        # Fall back to standard English CLDR names.
        name = emoji.demojize(value, language="en")

    if name == value:
        return None

    # Remove surrounding shortcode colons.
    name = name.strip(":")

    # Make the result safe and consistent as a Windows filename.
    name = name.lower()
    name = name.replace("&", "and")
    name = name.replace("’", "")
    name = name.replace("'", "")
    name = re.sub(r"[^a-z0-9]+", "_", name)
    name = re.sub(r"_+", "_", name).strip("_")

    return name or None


def find_available_destination(
    directory: Path,
    readable_name: str,
    extension: str,
    original_stem: str,
) -> Path:
    destination = directory / f"{readable_name}{extension.lower()}"

    if not destination.exists():
        return destination

    # Keep both files if multiple Unicode sequences resolve to the same name.
    destination = directory / (
        f"{readable_name}_{original_stem.lower()}{extension.lower()}"
    )

    counter = 2
    while destination.exists():
        destination = directory / (
            f"{readable_name}_{original_stem.lower()}_{counter}"
            f"{extension.lower()}"
        )
        counter += 1

    return destination


def rename_twemoji_files(directory: Path, apply_changes: bool) -> None:
    renamed = 0
    skipped = 0
    failed = 0

    for source in sorted(directory.iterdir()):
        if not source.is_file() or source.suffix.lower() != ".png":
            continue

        unicode_emoji = filename_to_emoji(source.name)

        if unicode_emoji is None:
            skipped += 1
            continue

        readable_name = emoji_to_readable_name(unicode_emoji)

        if readable_name is None:
            print(f"[UNKNOWN] {source.name}")
            failed += 1
            continue

        destination = find_available_destination(
            directory=directory,
            readable_name=readable_name,
            extension=source.suffix,
            original_stem=source.stem,
        )

        print(f"{source.name} -> {destination.name}")

        if apply_changes:
            source.rename(destination)

        renamed += 1

    mode = "Renamed" if apply_changes else "Would rename"

    print()
    print(f"{mode}: {renamed}")
    print(f"Skipped already-readable files: {skipped}")
    print(f"Unknown emoji names: {failed}")

    if not apply_changes:
        print()
        print("This was a dry run. Add --apply to perform the renames.")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Rename Twemoji codepoint filenames to readable emoji names."
    )
    parser.add_argument(
        "directory",
        nargs="?",
        type=Path,
        default=Path.cwd(),
        help="Twemoji directory. Defaults to the current directory.",
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Actually rename files. Without this option, only preview changes.",
    )

    args = parser.parse_args()
    directory = args.directory.resolve()

    if not directory.exists():
        raise SystemExit(f"Directory does not exist: {directory}")

    if not directory.is_dir():
        raise SystemExit(f"Path is not a directory: {directory}")

    rename_twemoji_files(directory, args.apply)


if __name__ == "__main__":
    main()