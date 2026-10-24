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
