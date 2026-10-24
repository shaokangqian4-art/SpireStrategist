# Local records and privacy

On Windows, records normally live under `%APPDATA%/SlayTheSpire2/SpireStrategist`. The exact location follows Godot's game user-data directory.

- `session-*.jsonl`: appended decisions, state and activity for a session.
- Latest-state JSON and continuous-run summary files: convenient current summaries; latest snapshots do not replace historical JSONL records.
- `feedback-exports/`: ZIPs made only when Export is clicked. A normal in-run feedback ZIP contains that session's record, current state and readme. The last-run menu export uses the last recorded run. Neither guarantees an entire multi-run batch.
- Language, layout, display and climb settings are stored locally. Best streaks are separated by character/difficulty; assisted/interrupted runs are not full automatic streak samples.

Nothing is automatically uploaded to the author. Agree/Disagree does not automatically retrain or change weights. Before sharing publicly, inspect the archive and remove private content from comments or other records. Include mod/game/solver versions and steps to reproduce. No GPT account or private contact details are required.

For a batch review, keep the relevant session records and continuous-run summaries. A seed alone may not reproduce a modded game with different game, solver, mod versions or configuration.
