# Localization

The assistant supports `auto`, `zh`, and `en`, stored in `SpireStrategist/language.json` under the game's user-data directory. Auto uses Chinese for `zhs`/`zht`, English otherwise. In-run Display settings and the main-menu picker share the same preference.

`localization/en.json` maps original Chinese strings/templates to English. Preserve numbered `{0}` placeholders exactly. Presentation translates whole phrases before nested fragments; multiline, indented and bulleted explanations are tested. Do not mutate policy messages or persisted decision IDs to translate the display. Player-written feedback must remain unchanged.

Run:

```powershell
dotnet run --project tools/LocalizationCatalog -c Release -- . --check
dotnet run --project tests/SpireStrategist.Tests -c Release
```

The catalogue audit is a source-coverage check, not proof that every runtime combination is correct. Native UI fixtures inspect actual rendered text and screenshots, and check manual potion use and recommendation overlays.

Names of cards/events/relics come from game localization. The assistant's English override does not change the game's language. CombatSolver owns its own localization; do not translate or patch its UI here. Raw JSON records retain original diagnostic text for stable replay; the feedback readme and export filenames follow the assistant language.
