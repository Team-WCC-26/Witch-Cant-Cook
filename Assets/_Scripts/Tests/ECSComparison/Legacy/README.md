# Historical ECS baseline

These files preserve the previous ingredient spawn and paired Entity pool implementations for CSV comparisons.
`IngredientSpawnSystem` and `IngredientEntityCleanupSystem` have `DisableAutoCreation`; only the dedicated
comparison component installs them. They are not installed by normal game initialization.

The normal pool contains optional lifecycle notifications with no gameplay subscribers. Legacy comparison
initialization subscribes to those notifications to reproduce the previous Entity preparation/return behavior.
The request Entity type also lives here. Production uses `IngredientSpawnCommand`, which is a plain value.

`baseline-source.sha256` records the three source files immediately before migration. The spawn algorithm
and paired pool algorithm are preserved; activation and pool observation are now explicit test setup.
Entities packages remain for these historical comparisons and existing unrelated prototype code.
