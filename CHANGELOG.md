# 0.6.55 — 2026-11-01

## 0.6.72 — 2026-11-03

Source catch-up from 0.6.55, including intervening policy and compatibility fixes.

- Recognize and protect invested Regent generation and strength/multi-hit plans; do not treat unrelated Sword cards as existing synergy.
- Value marginal cards and spare Stars; calibrate Regent priors with documented community evidence and retain contextual scoring.
- Improve multi-hit/enchantment choices and temporary-deck/duplicate handling.
- Audit mechanism-sensitive upgrades across characters, including generated cards, energy reduction and opening access.
- Reduce healthy Stone Humidifier rest bias, restrict Large Mushroom choices, and modestly adjust Regent Act 1 elite preference.
- Resume Scroll Boxes when takeover starts after acquisition; buy Book of Five Rings before an already-desired card when the whole sequence fits the budget.
- Validation: 270 core tests, 8 release-specific native workflow checks, 1,000 copied-RNG checks and 19,800 policy matrix checks. No new win-rate claim.


- Added Defect, Silent, Regent and Necrobinder strategy integration since 0.6.22, including single-player takeover and looping with CombatSolver.
- Added optional-per-client co-op advice (no automation), bounded reward/shop foresight, expanded native highlights, and character-sensitive item evaluation.
- Fixed Pyramid/Bullet Time synergy, redundant Gamble Retain upgrades, deck-size-aware Gamble copies, and late-game shiv/poison forecast conflicts.
- 236 core regressions and 16 hook bindings passed; 20 recorded reward snapshots replayed. No new win-rate claim.

# Changelog

## 0.6.22

- Added English presentation for assistant controls, strategy reasons, statuses, recommendations and feedback instructions; automatic language detection and saved override.
- Kept native game names, raw diagnostic records and CombatSolver's own interface independent of assistant translation.
- Added localization coverage, multiline value-preservation and native presentation checks.
- Prepared a public MIT source repository with bilingual documentation and reproducible local build steps. Removed private review-file dependencies from structural tests.

## 0.6.21

- Fixed merchant-relic enchantment selection invoking an incompatible method overload; added Adroit target evaluation and legal empty-selection handling.
- Allowed assistant panels to move throughout the game viewport; main-menu panel can collapse and save its layout.

## 0.6.20

- Introduced Advice, Run, Loop, Display and Feedback tabs.
- Moved loop controls away from the potion HUD and added non-interactive item/route overlays with independent display switches.

## 0.6.19

- Unified supported single-run shop takeover with continuous-run shop behavior.
- Separated normal takeover delays from faster loop mode; persisted difficulty/character choice and best streaks.
