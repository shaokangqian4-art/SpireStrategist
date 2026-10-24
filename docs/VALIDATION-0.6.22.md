# Validation for 0.6.22

Development baseline: Slay the Spire 2 0.111.0, Windows x64, optional CombatSolver 0.50.1.

- Clean public-source build: no compiler warnings or errors.
- 113 core regression tests passed, including localization placeholder and multiline numeric/paragraph preservation checks.
- 14 reflected game-hook checks and 1,000 copied-RNG checks passed.
- 19,800 card cases and 36,720 relic-context cases passed structural checks; no private feedback file is required.
- All 642 extracted player-facing source strings/templates have English catalogue entries.
- Native English UI fixtures passed: card and shop highlights, route rendering/replanning, toggles, manual potion targeting/throwing, layout/collapse and language switching. Actual assistant text at captured UI checkpoints was checked for untranslated Chinese (except the intentionally bilingual language selector).
- Native English main-menu start/stop and saved difficulty, no-solver lock/help, merchant enchantment selection (3, 2 and legal 0 targets), and custom single-run shop takeover passed.

Native fixtures use isolated user-data directories and do not exercise the player's live save. They are deterministic integration scenarios, not new win-rate samples. The game emits resource/RID cleanup warnings on forced fixture exit; these are retained in local test output. No assistant runtime exception was accepted in passing suites. Earlier failed localization iterations were corrected before this release.

Raw run records keep stable original diagnostic strings. Third-party UI, card names and game localization are outside the assistant's translation scope. This validation covers the named version and scenarios, not every possible mod combination.
