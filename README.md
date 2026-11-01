# Global Strategy Assistant · 全局战略助手 · 0.6.55

[简体中文](README.zh-CN.md) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3823884252) · [Downloads](https://github.com/shaokangqian4-art/SpireStrategist/releases)

A local strategy assistant for **all five native characters** in **Slay the Spire 2**. It evaluates your deck, health, relics, gold and map to recommend cards, routes, events, Ancient rewards, rest-site actions and shop purchases.

**No online AI account, API key or live AI inference is required.** Decisions use explainable heuristic scores. Scores are not win probabilities.

## 0.6.55 update

Adds the remaining native characters, character-sensitive relic/potion valuation and budget-aware shop scores. Silent now values Runic Pyramid with Bullet Time, avoids redundant Calculated Gamble Retain upgrades under Pyramid, allows useful repeated Gambles in larger decks, and respects established shiv plans when considering forecast poison cards.

Validation: 236 core regressions, 16 native hook bindings and 20 recorded reward replays. These are not new completed gameplay runs or win-rate measurements.

## Features

- Advice with scores and reasons, plus optional takeover of supported interactions.
- Recommended card/shop highlights and a route overlay that updates when the run changes. Both can be hidden independently and do not intercept clicks.
- Optional combat handoff to [CombatSolver by Torch](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961).
- Continuous standard random-seed runs, with immediate stop or stop-after-this-run, saved difficulty/character settings and best streaks by character/difficulty.
- Draggable, collapsible panels; English and Simplified Chinese interface, including strategy explanations. Select the language in Settings. Defaults to following the game language.
- Local feedback exports and run records. Nothing is automatically submitted to the author.

## Install and use

Subscribe on Steam, or download the release ZIP and place its `SpireStrategist` folder under the game's `mods` folder. Enable the mod in game and restart. Keep only one installed copy when switching between manual and Workshop installation.

The assistant starts in advice mode. Click **Take over** for the current run; **Stop** returns control. Continuous runs are separate and require a compatible CombatSolver. A fresh installation defaults to Ascension 0; your chosen difficulty is saved. Unsupported interactions may wait for manual input.

## Compatibility and limitations

- Development baseline: game **0.111.0**, Windows x64, .NET 9; other game versions may change internal APIs.
- Optional tested combat integration: **CombatSolver 0.50.1**. Without it, combat stays manual and continuous runs are unavailable. The solver is a separate project; this mod neither includes nor translates its interface.
- Extra floors are supported through actual map structure. Extra acts and arbitrary mod combinations are not guaranteed.
- Ironclad, Silent, Defect, Regent and Necrobinder have advice and single-player takeover. Regent/Necrobinder use initial mechanism models needing more gameplay feedback.
- Co-op is optional per client, with local advice, highlights and logs only. No takeover, automated combat, looping, cooperative-only card modelling, teammate synergy or co-op reward forecasts. Native handshake checks passed; real two-computer Steam play remains unverified.
- Some hidden information is read through copied RNG state. Previews are conditional on future choices and RNG consumption, not a complete prophecy.
- Policies and relic coverage are incomplete. Known key shop relics receive explicit treatment; remaining affordable relics can be purchased randomly within the budget. Do not interpret this as complete relic optimization.
- Card, relic and event names follow the **game's** language. Raw JSON diagnostics retain stable original strings and identifiers for replay; English display does not change policy inputs or scores.
- Small test batches are not a reliable general win-rate estimate. Automated fixtures validate behavior, not playing strength.

## Build, contribute and report issues

See [BUILD.md](docs/BUILD.md), [architecture](docs/ARCHITECTURE.md), [localization](docs/LOCALIZATION.md), and [CONTRIBUTING.md](CONTRIBUTING.md).

For bugs, include mod/game/solver versions, the screen or action, and reproduction steps. The Feedback tab lets you save a comment and export a ZIP. Review it before attaching it to a public issue: it contains run details and your comments. Feedback is saved locally and is not automatic training. One feedback ZIP is not an entire multi-run batch; see [records](docs/RECORDS.md).

## Credits and license

Author and maintainer: **穷仔** ([shaokangqian4-art](https://github.com/shaokangqian4-art)). Design priorities and strategy tuning were developed through gameplay feedback and tests. Implementation and documentation were developed with substantial AI coding assistance.

Original project code is available under the [MIT License](LICENSE). Slay the Spire 2 belongs to Mega Crit. CombatSolver and other mods remain independent works. Game assemblies, assets, third-party mod binaries, private feedback and player saves are not included; see [NOTICE](NOTICE).
