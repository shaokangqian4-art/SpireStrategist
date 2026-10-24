# Architecture

`SpireStrategist.Core` contains immutable snapshots, card/relic estimates, card/reward/shop policies, route lookahead, action gates and run/streak bookkeeping. It can be tested without loading the game.

`SpireStrategist.Mod` reads native game state, constructs snapshots, displays advice and executes supported interactions through native game UI/actions. Harmony hooks and reflection are version-sensitive. `SolverBridge` integrates the separately installed CombatSolver; it does not implement the solver. Integration fixtures are gated behind an explicit lab environment and marker file.

Route selection uses bounded forward evaluation (up to 12 nodes, a limited set of HP/upgrade representatives per node), not exhaustive full-run search. It estimates damage, gold and rest/upgrade opportunities and replans as live state changes. Hidden-state previews use copied RNG state and checks that the live source remains unchanged.

English is a presentation layer over stable policy messages. Scores, IDs and raw replay records are unchanged by language selection. UI panels and our drawn recommendation caption are translated; other mods' UI and native game localization remain independent.

`tools/Replay` re-evaluates local records; `tools/Evaluate` runs synthetic structural matrices. Neither claims to train a model automatically. Player comments require human review and deliberate code/weight changes.
