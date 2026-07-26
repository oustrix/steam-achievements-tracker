# Design reference

`steam-achievements-tracker.dc.html` is the design mockup this UI was built
from, exported from Claude Design. It is a reference document, not a build
artifact: it is read as text for palette values, copy, spacing and layout.

It will not render on its own — it needs the Claude Design runtime
(`support.js`), which is deliberately not vendored here. Nothing in the build
or the tests depends on this file.

## Anonymization

This file is verbatim from Claude Design except for account identifiers in the
onboarding mockup block: the real SteamID64 and username have been replaced with
placeholder values (`76561190000000000` and `Your Steam account`) to comply with
the repository's credential policy.
