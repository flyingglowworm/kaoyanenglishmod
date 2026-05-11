# KaoyanEnglishMod Codex Handoff

Last updated: 2026-05-11

This document is the handoff note for the next Codex session. The current project is a Slay the Spire 2 / Godot C# mod named `KaoyanEnglishMod`.

## Current Stable State

The mod is currently in a stable, playable state:

- The game loads the `考研英语` mod successfully.
- The player receives the starter relic `考研词典` / `KaoyanLexicon` at run start.
- The starter relic works for all five current characters.
- The relic appears once in the compendium starter relic list.
- The relic tooltip no longer contains the old placeholder text.
- The core combat loop is implemented and has been smoke tested in game.
- `dotnet build` succeeds with 0 warnings and 0 errors.

## Gameplay Implemented

`KaoyanLexicon` currently does the following:

- On the player's first combat turn, shows a Challenge / Decline popup.
- Decline route:
  - Grants +1 Strength and +1 Dexterity for the current combat.
- Challenge route:
  - Draws 1 extra card at the start of each player turn.
  - Generates one random vocabulary question per player turn.
  - Shows A/B/C/D/Skip question UI.
  - Correct answer:
    - Opens a reward choice UI.
    - Replay reward: lets the player select a hand card and increments `BaseReplayCount`.
    - FreeThisTurn reward: lets the player select a hand card and calls `SetToFreeThisTurn()`.
  - Wrong answer:
    - Randomly exhausts one current hand card.
  - Skip:
    - No reward and no penalty.

## Key Source Files

- `src/KaoyanEnglishEntry.cs`
  - Mod initializer.
  - Registers `KaoyanLexicon` with `SharedRelicPool`.
  - Applies Harmony patches.

- `src/Relics/KaoyanLexicon.cs`
  - Main relic model.
  - Owns the per-combat challenge state.
  - Handles combat callbacks, question generation, rewards, penalties, and logging.
  - Overrides relic image paths.

- `src/Patches/KaoyanLexiconPatches.cs`
  - Adds one compendium-visible starter relic entry via `Ironclad.StartingRelics`.
  - Adds the relic to every player's actual starting relics via `Player.PopulateStartingRelics`.
  - Replaces the default locked compendium icon with the Kaoyan Lexicon outline only for this relic.

- `src/Vocab/KaoyanWord.cs`
  - Vocabulary word data model.

- `src/Vocab/KaoyanQuestion.cs`
  - Generated question data model.

- `src/Vocab/KaoyanVocabService.cs`
  - Loads JSON from Godot `res://` resource path.
  - Generates weighted random words and multiple-choice questions.

- `src/UI/KaoyanChallengePopup.cs`
  - Challenge / Decline yes-no popup.

- `src/UI/KaoyanQuestionPopup.cs`
  - A/B/C/D/Skip question popup.

- `src/UI/KaoyanRewardChoicePopup.cs`
  - Correct-answer reward choice popup.

- `src/UI/KaoyanHandCardChoicePopup.cs`
  - Temporary fallback hand-card picker if official hand selection fails.

- `src/UI/KaoyanUiStyle.cs`
  - Shared UI styling and texture-loading helpers.

## Key Resource Files

- `KaoyanEnglishMod/data/kaoyan_words_mod.json`
  - Vocabulary JSON.

- `KaoyanEnglishMod/localization/zhs/relics.json`
  - Relic title, description, and flavor.

- `KaoyanEnglishMod/images/ui/kaoyan/`
  - Question and reward UI background/button images.

- `KaoyanEnglishMod/images/packed/relics/kaoyan_lexicon.png`
  - Relic icon source image.

- `KaoyanEnglishMod/images/packed/relics/kaoyan_lexicon_outline.png`
  - Relic outline/silhouette image.

- `KaoyanEnglishMod/images/atlases/relic_atlas.sprites/kaoyan_lexicon.tres`
  - AtlasTexture resource for the relic icon.

- `KaoyanEnglishMod/images/atlases/relic_outline_atlas.sprites/kaoyan_lexicon.tres`
  - AtlasTexture resource for the relic outline.

- `export_presets.cfg`
  - Must include all JSON, image, and `.tres` resources that should be packed into the PCK.

## Important API Findings

These STS2 APIs were verified through source inspection and in-game testing:

- `ModHelper.AddModelToPool(typeof(SharedRelicPool), typeof(KaoyanLexicon))`
  - Registers the relic model so `ModelDb.Relic<KaoyanLexicon>()` works.

- `Player.PopulateStartingRelics`
  - Patched after original execution to add `KaoyanLexicon` to all characters' actual starting relics.
  - This avoids compendium duplication caused by patching all characters' `StartingRelics` getters.

- `Ironclad.StartingRelics`
  - Patched only once so the compendium starter relic category shows exactly one `KaoyanLexicon`.

- `NRelicCollectionEntry._Ready`
  - Patched only for `KaoyanLexicon` locked entries to show the custom outline instead of default `locked_model.png`.
  - In vanilla STS2, `ModelVisibility.Locked` always shows a generic lock and does not use `RelicModel.IconOutline`.

- `AfterSideTurnStart(CombatSide side, CombatState combatState)`
  - Relic combat callback used to trigger per-turn logic.

- `PowerCmd.Apply<StrengthPower>()` and `PowerCmd.Apply<DexterityPower>()`
  - Used for decline route reward.

- `CardPileCmd.Draw(new BlockingPlayerChoiceContext(), 1m, Owner)`
  - Used for challenge extra draw.

- `CardCmd.Exhaust(new BlockingPlayerChoiceContext(), card)`
  - Used for wrong-answer penalty.

- `CardSelectCmd.FromHand(...)`
  - Used for official hand-card selection for rewards.

- `card.BaseReplayCount++`
  - Adds Replay 1 to the selected card for the current combat.

- `card.SetToFreeThisTurn()`
  - Makes the selected card free this turn.

- `NModalContainer.Instance.Add(...)`
  - Used for modal popups.

- `SceneHelper.Instantiate<NVerticalPopup>("vertical_popup")`
  - Correct way to instantiate official yes-no popup; direct `new NVerticalPopup` caused missing child nodes.

## Gotchas And Lessons Learned

- Changing `.cs` files requires:
  - `dotnet build`
  - copying the built DLL to the game's mod folder.

- Changing images, localization JSON, or other Godot resources requires:
  - exporting a new PCK from Godot
  - copying the PCK to the game's mod folder.

- If text changes appear but code behavior does not, the PCK is updated but the DLL is probably old.

- If code behavior changes but images/text do not, the DLL is updated but the PCK is probably old.

- The compendium starter relic list reads `ModelDb.AllCharacters.SelectMany(c => c.StartingRelics)`.
  - Patching every character's `StartingRelics` creates duplicate compendium entries.
  - Current solution: patch only Ironclad for compendium visibility, and patch `Player.PopulateStartingRelics` for actual all-character gameplay.

- Vanilla locked compendium relics use `NRelicCollectionEntry.lockedIconPath` and do not read custom relic outline assets.
  - Current solution: patch locked `KaoyanLexicon` entries after `_Ready`.

- The official `NVerticalPopup` depends on child nodes from its scene.
  - Do not instantiate it with `new`.

- The official hand selection can leave visual state weird if fallback UI or manual card nodes are used incorrectly.
  - Prefer `CardSelectCmd.FromHand`.
  - Keep `KaoyanHandCardChoicePopup` only as fallback.

- Some logs captured through PowerShell display Chinese as mojibake because of console encoding.
  - The underlying game text can still be correct in-game.

## Build And Deployment

Build:

```powershell
cd E:\slay_spire_mods\kaoyanenglishmod
dotnet build
```

Copy DLL after C# changes:

```powershell
Copy-Item -LiteralPath "E:\slay_spire_mods\kaoyanenglishmod\.godot\mono\temp\bin\Debug\KaoyanEnglishMod.dll" -Destination "E:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\KaoyanEnglishMod\KaoyanEnglishMod.dll" -Force
```

Export PCK after resource changes:

- Open the project in Godot.
- Use the existing export preset.
- Export `KaoyanEnglishMod.pck`.
- Copy it to:

```text
E:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\KaoyanEnglishMod\KaoyanEnglishMod.pck
```

Run from PowerShell using the previously working method in the project/game folder. If launched outside Steam, ensure the correct Steam app id behavior is handled as before.

## Verification Checklist

After a full DLL/PCK update:

- Game starts without crashing.
- Mod loading log includes:
  - `KaoyanEnglishMod loaded.`
  - `KaoyanEnglishMod Harmony patches applied.`
- All five characters start with `考研词典`.
- Compendium starter relic list shows exactly one `考研词典`.
- Relic tooltip has current implemented effect text.
- If `考研词典` is locked/not seen, it should use the dictionary silhouette rather than the generic lock.
- In combat:
  - First player turn shows Challenge / Decline.
  - Decline grants +1 Strength and +1 Dexterity.
  - Challenge draws 1 extra card each player turn.
  - Question UI appears each player turn.
  - Correct answer opens reward choice.
  - Replay reward applies.
  - FreeThisTurn reward applies.
  - Wrong answer exhausts one random hand card.
  - Skip does nothing.

## Suggested Next Work

Recommended next phases:

1. Polish UI layout and text readability.
2. Improve reward selection presentation and official hand-select prompts.
3. Add better localization strings for every popup.
4. Add configurable balancing values.
5. Add automated smoke probes where practical.
6. Build the future ancient version of the Kaoyan Lexicon relic.

Before starting new work, commit the current stable state.

## Files The Next Codex Should Read First

Ask the next Codex session to read:

- `docs/codex_handoff_current.md`
- `CODEX_TASK.md`
- `docs/build_notes.md`
- `docs/api_notes.md`
- `how_to_custom_relic/how_to_custom_relic.md`
- `KaoyanEnglishMod.csproj`
- `src/KaoyanEnglishEntry.cs`
- `src/Patches/KaoyanLexiconPatches.cs`
- `src/Relics/KaoyanLexicon.cs`
- `src/UI/KaoyanChallengePopup.cs`
- `src/UI/KaoyanQuestionPopup.cs`
- `src/UI/KaoyanRewardChoicePopup.cs`
- `src/UI/KaoyanUiStyle.cs`
- `src/Vocab/KaoyanVocabService.cs`
- `KaoyanEnglishMod/localization/zhs/relics.json`
- `export_presets.cfg`

