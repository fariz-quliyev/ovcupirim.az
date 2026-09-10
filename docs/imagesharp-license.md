# ImageSharp licence — unresolved business-status condition

Resolves the documentation half of the G5 blocker "ImageSharp 3.1.11 licence confirmation." The
code is **unchanged** — this is a record of the licence terms and the one fact this repository
cannot supply, not a decision.

## What is actually in use

`SixLabors.ImageSharp` **3.1.11**, referenced directly by `Ovcuprim.Infrastructure`
(`Ovcuprim.Infrastructure.csproj`), which decodes, bounds-checks and re-encodes every listing and
store image (`ImageSharpProcessor.cs`). This is a **direct package dependency** in **closed-source**
software, which is the specific combination the licence terms below turn on.

## Licence terms, as currently published by Six Labors

**Six Labors Split License, version 1.0 (June 2022).** Source:
[`LICENSE`](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE) in the ImageSharp
repository, and Six Labors' own summary at
[sixlabors.com/posts/license-changes](https://sixlabors.com/posts/license-changes/) and
[sixlabors.com/pricing](https://sixlabors.com/pricing/). Checked 2026-09-03.

A work is licensed under **Apache License 2.0** — no commercial licence needed — when it is
consumed:

- in software itself licensed Open Source or Source Available, **or**
- as a **transitive** dependency (pulled in indirectly by some other package), **or**
- as a **direct** dependency by a for-profit company/individual with **less than USD 1,000,000
  annual gross revenue**, **or**
- as a direct dependency by a non-profit organisation or registered charity.

Outside all four of those, i.e. **a direct dependency, in closed-source software, by a for-profit
entity at or above USD 1,000,000 annual gross revenue** — a **commercial licence must be purchased**
(pricing at the link above; not reproduced here, as it is a commercial term this repository does not
set).

## The one fact this repository does not have

**OvcuPrim.az's actual annual gross revenue and its legal entity status.** That determines which
side of the threshold applies, and it is business information no engineering artifact in this
repository has visibility into or authority to state. This document deliberately does not guess it.

## The specific misconception this corrects

Earlier working notes on this project treated ImageSharp 3.1.11 as the version chosen specifically
*to avoid* a paid licence, on the assumption that only 4.x carried the commercial requirement. That
assumption does not hold: **Six Labors' own announcement states the Split License applies to
ImageSharp starting at v3.0.0 "going forward"** — meaning 3.1.11 is licensed under exactly the same
terms as 4.x, not under the plain Apache 2.0 terms an earlier version would have carried. Using
3.1.11 has no bearing on which licence condition applies; only OvcuPrim's revenue/entity status does.

## What resolves this

Someone with authority over OvcuPrim.az's business status confirms which condition the company
meets and, if the revenue/entity conditions are not met, purchases a commercial licence before this
dependency ships in a production build the company distributes or operates commercially. Until then
this is correctly listed as a blocker, not silently assumed clear.
