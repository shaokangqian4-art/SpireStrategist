# Contributing

Issues and pull requests are welcome in English or Chinese. Please describe the concrete run state and expected behavior, rather than only proposing a global score increase.

For policy changes, add a focused regression showing the intended recommendation and a counterexample where it should not apply. Preserve deterministic scores and input snapshots. Keep game-version adapters separate from pure policy logic. Run the core tests and evaluation matrix; native UI changes also need game-side testing.

Do not commit game DLLs/assets, other mods' binaries, credentials, private saves, exported feedback, or raw user comments. Game data fixtures should contain only the minimal identifiers/numeric mechanics needed for compatibility. Keep the game and CombatSolver separate dependencies.

For translations preserve placeholders, numbers and original feedback text. Add new player-facing phrases to `localization/en.json` and check actual multiline display. See `docs/LOCALIZATION.md`.

This project accepts contributions under the MIT License. Describe what was tested, and distinguish synthetic fixtures from actual win-rate experiments. AI assistance is welcome; contributors should review and validate its output.
