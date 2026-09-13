# Screening: what shipped, and what is deliberately missing

The screening mechanism (`IContentScreener`, `IScreeningPolicy`, `CompositeContentScreener`) is
complete and live. It never rejects a listing by itself — Rule 6 (nothing goes live without a
moderator) is enforced once, upstream of every screener, in `ListingPublishService`. A policy's
only job is to leave a flag for a moderator to see.

## Shipped

**Duplicate detection** (`DuplicateListingScreeningPolicy`) — flags a submission whose folded title
exactly matches another listing the same seller already has live or awaiting a decision in the same
category, within a configurable lookback window (`Listings:Screening:DuplicateLookbackDays`, default
30 days). Deliberately an exact match on the same folded text the search key already uses, not a
similarity score: a fuzzy threshold is itself a policy decision about how similar is "too similar,"
and that is exactly the kind of duplicate-enforcement judgement this was asked to ship without
inventing. A near-duplicate a human would recognise on sight is what the moderation queue is for.

## Not shipped, and why

**Prohibited-item classification.** No policy inspects a listing's title, description or category
against a list of disallowed goods. This is legal content — what may not be sold, in which
categories, under what exceptions — not mechanism, and this repository has no authority to invent
it. The seam is ready: a second `IScreeningPolicy` (for example `ProhibitedItemScreeningPolicy`)
registered alongside the duplicate one in `DependencyInjection.cs` is the entire integration cost.
Nothing about `CompositeContentScreener`, `ListingPublishService`, the moderation queue's flag
display, or the audit trail changes to add it.

### What a future prohibited-item policy needs, concretely

- **The list itself.** Categories, keywords, or category-level restriction rules — sourced from
  whoever owns marketplace legal policy for OvcuPirim.az, not authored here.
- **A decision on where it lives.** Configuration (a JSON list, versioned in source control) is
  the shape every other policy input in this codebase uses (see `ScreeningOptions`,
  `ConfigurationListingQuotaPolicy`) and is the natural first choice; a database table only earns
  its complexity if the list needs to change without a deploy.
- **Confirmation that flag-only is still correct at launch**, and under what condition (if any)
  it should tighten from "flag" to "reject." B-2 (the original product decision) set flag-only for
  launch; that decision is not re-litigated here, only recorded as the current state.
- **Wording for the flag message** a moderator will read, and — if it should ever surface to a
  seller — wording that does not assert a legal claim OvcuPirim.az has not actually decided to make.

## Already-existing category restriction, and how it relates

`RestrictionStatus` (`Unrestricted` / `Restricted` / `Unclassified`) on `Category` already exists
and already routes a listing to the *strict* moderation queue (Phase 3). That is a coarser,
already-shipped mechanism — "this category needs closer attention" — and is unrelated to, and not a
substitute for, item-level prohibited-content classification. The two can coexist: a category being
`Restricted` says nothing about whether a specific listing inside it violates a rule that has not
been written down yet.
