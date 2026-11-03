# Release validation: 0.6.72

Development baseline: Windows x64, Slay the Spire 2 0.111.0, .NET 9.

- 270 core regression tests passed.
- 1,000 copied-RNG checks and 19,800 structural policy checks passed.
- Eight release-specific native checks covered Scroll Boxes continuation and previews, Book of Five Rings ordering, camp upgrades and related UI/mechanics paths.
- The public source tree was separately compiled against the local game assemblies and its 270 core tests passed. Three existing nullable warnings remain in the isolated AutopilotLab test harness.

Real recorded snapshots reproduced the Regent colorless-generation errors: an unrelated Smith recommendation and a Stardust purchase that reused Stars already needed by the deck. Re-evaluation removes the false Sword component bonus and accounts for spare Stars. Counterexamples retain genuinely supported Sword and Star investments.

These are regression and replay results, not new natural full-run win-rate measurements. Different decisions change subsequent RNG consumption; snapshot comparisons do not establish what an entire alternative run would have achieved.

Public fixtures contain selected decision state, not original feedback packages. Native card descriptions are omitted from public data; identifiers, names, numeric mechanics and authored policy explanations remain. This changes embedded-resource bytes, so a public rebuild is not claimed to have the identical DLL hash as the development build. Scoring does not use card description text.

The published Workshop DLL SHA-256 is `4BABEDEEFA03C9CE58E306C9BFF28D73FC899AA1B51D1B194C8AF21B1A71A044`.
