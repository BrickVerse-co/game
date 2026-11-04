# Engine API documentation publishing

`publish_game_api_docs.py` converts the live Engine `apiref.json` into the public [`BrickVerse-co/game-api`](https://github.com/BrickVerse-co/game-api) documentation tree.

## Manual publishing

Clone `game-api` beside this repository, generate a fresh API reference, then run:

```powershell
python tools/docs/publish_game_api_docs.py --docs-root ..\game-api\docs --refresh-api --godot C:\path\to\Godot.exe
```

To reuse an existing `BrickVerse/apiref.json` without launching Godot:

```powershell
python tools/docs/publish_game_api_docs.py --docs-root ..\game-api\docs
```

Review and commit changes from the `game-api` repository. The script only replaces generated `docs/api/types` and `docs/api/enums`, refreshes API icons and `llms.txt`, and leaves hand-authored documentation intact.

## Automated publishing

`.github/workflows/publish-game-api-docs.yml` regenerates the live API on changes to Engine scripting surfaces and pushes the result to `BrickVerse-co/game-api`. Configure the `GAME_API_PUBLISH_TOKEN` Actions secret with permission to write to that repository.

## Community documentation

The public repository remains the source of truth for prose. Contributors add Markdown fragments under `docs/contributions` and runnable Luau examples under `docs/code-samples`. Generated pages merge those files beside the matching class or member on every publish. The generator creates README files in both folders with the supported layout.
