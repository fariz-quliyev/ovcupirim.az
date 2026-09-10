# Region dataset — provenance and scope

Resolves the G5 blocker "authoritative production region dataset." Read this alongside
`src/Ovcuprim.Infrastructure/Persistence/Seed/Data/regions.official-2024.json`, which is the
actual importable file.

## Source inspected

**"İnzibati ərazi bölgüsü təsnifatı, 2024"** (Administrative Territorial Division Classification,
2024), Azerbaijan Republic State Statistics Committee (Dövlət Statistika Komitəsi).

- Approved by the Committee's board, decree No. 2/2, 16 February 2024. Agreed with the Apparatus
  of the Milli Məclis (National Assembly).
- Document fetched from `https://www.stat.gov.az/menu/5/classifications/source/Inzibati-1.05.2024.pdf`
  (120 pages, PDF 1.7) on 2026-09-03 and read in full — text extracted from the PDF itself, not
  summarised secondhand, and cross-checked against the document's own table of contents (p. 118–119)
  and its per-chapter section headers (one per city/rayon, each repeating its own code).
- This is the classifier itself, not a secondary or crowd-sourced source (Wikipedia, OpenStreetMap,
  etc. were not used for any name or code in the imported file).

## Coding structure, as stated by the source

An 8-digit code, `XXX XX XX X`:

| Digits | Meaning |
|---|---|
| 1–3 | Level I object (the city, rayon or city district the entry belongs to) |
| 4–5 | Level II object (city, qəsəbə, kənd, or "sahə inzibati ərazi dairəsi") |
| 6–7 | Level III object (qəsəbə or kənd) |
| 8 | Status digit — see below |

Status digit: `1` rayon · `2` city of republic subordination · `3` city district (şəhər rayonu) ·
`4` city of rayon subordination · `5` territorial circle / city area unit · `6` qəsəbə (urban-type
settlement) · `8` kənd (village) · `9` "sahə inzibati ərazi dairəsi" (area administrative circle).

Officially published totals for 2024, which this import's counts were checked against: 64 rayons,
11 cities of republic subordination, 6 cities of rayon subordination, 12 city districts, 262 towns,
190 town area units, 40 area administrative circles, 1,724 village area units, 4,244 villages.

## What was imported, and why only that much

`OvcuPrim`'s `Region` model is deliberately two levels at most, and only "cities and rayons" are
ever offered in the public picker (`Region.IsSelectable`'s own doc comment predates this work and
states this explicitly — this import honours an existing decision, it does not introduce one).
Level I of the official classification — cities of republic subordination and rayons — **is**
exactly that set. Importing further down (city districts, qəsəbələr, kəndlər — the other ~6,200
entries) would add thousands of rows nothing in the product currently surfaces, at a
correspondingly larger risk of a transcription slip going unnoticed. So this import is Level I
only:

- **10 cities of republic subordination** (status digit 2): Bakı, Gəncə, Xankəndi, Lənkəran,
  Mingəçevir, Naftalan, Sumqayıt, Şəki, Şirvan, Yevlax.
- **64 rayons** (status digit 1): 57 on the mainland + 7 within Naxçıvan.
- **Naxçıvan** itself, represented once — see below.

Total: **75 rows**, all `RegionType.City`, `RegionType.Rayon` or `RegionType.AutonomousRepublic`,
all `IsSelectable = true`, all top-level (no `ParentId`).

**Excluded, deliberately, not by oversight:** city districts (şəhər rayonu — e.g. Binəqədi, Xətai,
Xəzər within Bakı), and every qəsəbə/kənd. These exist in the official classification and could be
imported later as `IsSelectable = false` children if a real product need for that precision
appears (the model already supports one more level: `Region.Parent.Depth >= 1` is the only thing
`RegionService.ImportAsync` refuses). Nothing was invented or approximated for them — they are
simply not in this file.

**Naxçıvan.** The classification gives "Naxçıvan Muxtar Respublikası" a grouping code (`10000000`)
that carries no status digit and is not itself a place, then separately lists "Naxçıvan şəhəri"
(`10400002`, a real city-level entry — the eleventh city of republic-equivalent standing) and the
Autonomous Republic's own 7 rayons. This import uses the real `10400002` entry as the single
`AutonomousRepublic`-typed row named "Naxçıvan" — the same slug, name and type the project's
existing Development fixture (`regions.dev.json`) already used, so nothing about how the rest of
the codebase refers to it changes. The 7 Naxçıvan rayons are imported flat, as siblings of the
mainland rayons, not nested under "Naxçıvan" — consistent with every other rayon in this file, and
with the flat picker never asking a seller to drill through an Autonomous-Republic grouping to find
their own rayon.

## Coordinates — remain a separate, unresolved blocker

**The 2024 classification carries no coordinates whatsoever** — verified by reading the document:
it is a hierarchical code-and-name registry, nothing else. `regions.official-2024.json` therefore
omits `latitude`/`longitude` on every row entirely (not sent as explicit `null`, which would clear
a previously-set value — simply absent, which `RegionImportRow`'s `Omittable<T>` fields leave
untouched). Coordinates for these 75 places are still needed for the map discovery view and remain
open: **a geodesic/coordinate source has not been identified or approved**, and per standing
instruction this was not backfilled from OpenStreetMap, Wikipedia, or any other secondary source.
Resolving this is a separate task — identify an authoritative Azerbaijani geodesy or state-mapping
source (not the Statistics Committee's classifier, which does not carry this data), get it approved,
then import coordinates through the same `Omittable<double?>` fields without touching names or
codes.

## Field mapping (official → `RegionImportRow`)

| Official field | Import field | Note |
|---|---|---|
| Name (Azerbaijani) | `nameAz` | Verbatim from the source. |
| — (not present) | `nameRu` | Omitted — the source has no Russian column. Not invented. |
| Status digit → City / Rayon / AutonomousRepublic | `type` | Derived from the status digit and the section the entry appears under (city chapter vs. rayon chapter vs. the Naxçıvan chapter), not from the digit alone. |
| — | `slug` | Omitted — derived by the server from `nameAz` via the existing `AzerbaijaniText.ToSlug`, the same folding every other slug in the taxonomy uses. Verified by hand against every slug already present in `regions.dev.json` for the names in common (Bakı → `baki`, Naxçıvan → `naxcivan`, Qəbələ → `qebele`, etc.) — all match. |
| — | `parentSlug` | Omitted for every row — every imported region is top-level, matching the scope decision above. |
| (product decision, not sourced from the classifier) | `isSelectable` | `true` for every row — these are exactly the "cities and rayons" the picker already says it offers. |
| Document presentation order (cities, then rayons alphabetically, then Naxçıvan's chapter) | `sortOrder` | Preserves the source document's own ordering; not an invented ranking. |
| — (not present in the source) | `latitude` / `longitude` | Omitted — see "Coordinates" above. |

The 8-digit official code itself is **not** stored in the `Region` table — that table has no such
column, and adding one was out of scope for an import (preserving the existing model/contract).
The code↔name mapping this import used is reproduced in full below, so the import remains
independently auditable against the source without needing the code column.

## How to run the import

`regions.official-2024.json` is already shaped as `{ "regions": [...] }`, the exact body
`POST /api/v1/admin/regions/import` expects (`RegionImportRequest`). An Admin can also paste its
contents directly into the textarea on **Admin → Regions** (`AdminRegionsPage`), which posts to the
same endpoint. The import is idempotent — an existing slug is updated, not duplicated — so it is
safe to re-run.

This file is **not** wired into any automatic seed path (`--seed`, `--seed-listings`, or otherwise).
It is picked up only when an operator explicitly runs the import, the same way any other
authoritative-dataset load already worked before this change.

## Full code table (for audit against the source)

### Cities of republic subordination (10) + Naxçıvan (1)

| Code | Name | Type |
|---|---|---|
| 00000002 | Bakı | City |
| 20000002 | Gəncə | City |
| 70400002 | Xankəndi | City |
| 80200002 | Lənkəran | City |
| 90200002 | Mingəçevir | City |
| 51000002 | Naftalan | City |
| 30900002 | Sumqayıt | City |
| 40400002 | Şəki | City |
| 91100002 | Şirvan | City |
| 90100002 | Yevlax | City |
| 10400002 | Naxçıvan | AutonomousRepublic |

### Rayons — mainland (57)

| Code | Name | Code | Name |
|---|---|---|---|
| 30800001 | Abşeron | 40700001 | İsmayıllı |
| 60800001 | Ağcabədi | 60100001 | Kəlbəcər |
| 60900001 | Ağdam | 90600001 | Kürdəmir |
| 90300001 | Ağdaş | 40300001 | Qax |
| 61200001 | Ağdərə | 50100001 | Qazax |
| 50200001 | Ağstafa | 40600001 | Qəbələ |
| 40900001 | Ağsu | 30700001 | Qobustan |
| 80100001 | Astara | 30300001 | Quba |
| 40100001 | Balakən | 60300001 | Qubadlı |
| 60700001 | Beyləqan | 30100001 | Qusar |
| 61000001 | Bərdə | 60200001 | Laçın |
| 80800001 | Biləsuvar | 80300001 | Lerik |
| 60500001 | Cəbrayıl | 80500001 | Masallı |
| 80600001 | Cəlilabad | 80700001 | Neftçala |
| 50600001 | Daşkəsən | 40500001 | Oğuz |
| 60600001 | Füzuli | 90800001 | Saatlı |
| 50500001 | Gədəbəy | 90900001 | Sabirabad |
| 50900001 | Goranboy | 80900001 | Salyan |
| 40800001 | Göyçay | 50700001 | Samux |
| 50800001 | Göygöl | 30500001 | Siyəzən |
| 91000001 | Hacıqabul | 30400001 | Şabran |
| 30200001 | Xaçmaz | 41000001 | Şamaxı |
| 30600001 | Xızı | 50400001 | Şəmkir |
| 70100001 | Xocalı | 70200001 | Şuşa |
| 70300001 | Xocavənd | 61100001 | Tərtər |
| 90700001 | İmişli | 50300001 | Tovuz |
| | | 90400001 | Ucar |
| | | 80400001 | Yardımlı |
| | | 40200001 | Zaqatala |
| | | 60400001 | Zəngilan |
| | | 90500001 | Zərdab |

### Rayons — Naxçıvan Autonomous Republic (7)

| Code | Name |
|---|---|
| 10300001 | Babək |
| 10600001 | Culfa |
| 10800001 | Kəngərli |
| 10700001 | Ordubad |
| 10100001 | Sədərək |
| 10500001 | Şahbuz |
| 10200001 | Şərur |

**Totals: 75 rows — 11 city-level (10 + Naxçıvan) + 64 rayons.** Matches the officially published
2024 totals for these two categories exactly.
