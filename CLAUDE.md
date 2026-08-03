
# General rules

## Code

 - Keep code simple and readable, modular and extendable.
 - Avoid overengineering; avoid useless functions used only once; avoid useless abstractions.
 - Avoid duplication of code.

## Commits

 - When committing, don't add yourself as co-author.
 - Keep commit messages coincise.
 - Use commit messages starting with "feat: ", "fix: ", "docs: ", "chore: ", and so on.

## Versioning

 - The version lives in `<Version>` in `Leauge Auto Accept/Leauge Auto Accept.csproj`. It follows `MAJOR.MINOR.PATCH`.
 - Bump the PATCH part (e.g. 3.7.1 -> 3.7.2) for fixes and small changes; bump MINOR/MAJOR for larger changes.
 - Keep patch 0 as two parts (`3.7`, not `3.7.0`); the app only shows the patch part when it's non-zero.
 - The GitHub release tag must exactly equal `v` + the displayed version (e.g. version `3.7.1` -> tag `v3.7.1`), since the update check is an exact string match.

## Documentation

 - Keep the README "Enhanced edition" section updated: list new features under **Added:** and bug fixes under **Fixed:**, each as a concise one-liner.
