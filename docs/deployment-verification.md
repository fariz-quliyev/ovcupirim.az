# Deployment verification

Run this against a deployed environment after every configuration change and before promoting to
production. It checks the settings that only fail in production, and that no test can reach: the
proxy chain, host filtering, secrets, and cache behaviour through the real edge.

Set `BASE` to the environment's public origin.

```bash
BASE=https://staging.ovcuprim.az
```

---

## 1. Secrets and storage fail fast

The host must refuse to start when any of these is missing or wrong. Verified by deliberately
breaking each one on a scratch instance — never on a live one.

| Variable | Expected on start |
|---|---|
| `ConnectionStrings__Default` blank | `InvalidOperationException: Connection string 'Default' is not configured.` |
| `Auth__Jwt__SigningKey` short/blank | `OptionsValidationException: Auth:Jwt:SigningKey must be configured with at least 32 characters.` |
| `Auth__Security__HashingKey` short/blank | `OptionsValidationException: Auth:Security:HashingKey must be configured with at least 32 characters.` |
| `Storage__Local__RootPath` left at the relative default (`wwwroot/uploads`) | `InvalidOperationException: Storage:Local:RootPath ("wwwroot/uploads") is not an absolute path. …` |
| `Payments__Epoint__PublicKey` + `Payments__Epoint__PrivateKey` set, `Payments__FrontendBaseUrl` blank or relative | `InvalidOperationException: Payments:FrontendBaseUrl must be an absolute http(s) origin (e.g. https://ovcuprim.az) when the Epoint gateway is configured. …` |
| `Payments__Epoint__*` blank | Starts; every purchase attempt answers 500 from the refusing placeholder gateway (deliberate — payments are opt-in, see `docs/payments-epoint.md`). |

A host that starts with any of these absent or wrong is misconfigured, not lenient. The storage
check was verified live on 2026-09-03 (Development): a Production-mode run with every other secret
supplied but no `Storage__Local__RootPath` override refused to start with exactly the message
above; the same run with `Storage__Local__RootPath` set to an absolute, writable path started
cleanly. See `StorageSetup`'s remarks for why the check exists — a relative path resolves against
the application's own directory, which most redeploys replace, silently losing every upload.

## 2. Host filtering

```bash
# The real hostname: expect 200.
curl -s -o /dev/null -w '%{http_code}\n' "$BASE/health"

# A hostname the API does not serve: expect 400.
curl -s -o /dev/null -w '%{http_code}\n' -H 'Host: evil.example' "$BASE/health"
```

Startup logs must contain `Host filtering is active for …` listing exactly the intended names. If
the line is absent, `AllowedHosts` was not applied — and outside Development the host should not
have started at all.

## 3. Forwarded headers — two client addresses

**This is the check that cannot be simulated.** Every rate limit partitions on the caller's
address, so the whole limiter design rests on the API seeing the real client rather than the proxy.

From **two genuinely different source addresses** (two machines, or one machine and a mobile
connection — not two terminals on the same host):

```bash
# Address A: exhaust the OTP limit (5 per 15 minutes).
for i in $(seq 1 6); do
  curl -s -o /dev/null -w '%{http_code} ' -X POST "$BASE/api/v1/auth/register" \
    -H 'Content-Type: application/json' \
    -d "{\"phoneNumber\":\"+9945011122$i\",\"fullName\":\"Yoxlama\"}"
done; echo
```

Expected: `200 200 200 200 200 429`.

```bash
# Address B, immediately afterwards: must still be allowed.
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$BASE/api/v1/auth/register" \
  -H 'Content-Type: application/json' \
  -d '{"phoneNumber":"+994501119999","fullName":"Yoxlama"}'
```

Expected: **not** `429`.

- Both addresses throttling together ⇒ the API sees only the proxy. `KnownProxies` /
  `KnownNetworks` are wrong or absent.
- Address B never throttling no matter what ⇒ check `ForwardLimit` against the real chain length.

## 4. Forwarded headers — spoofing is ignored

From an address that is **not** a configured proxy:

```bash
for i in $(seq 1 8); do
  curl -s -o /dev/null -w '%{http_code} ' -X POST "$BASE/api/v1/auth/register" \
    -H "X-Forwarded-For: 203.0.113.$i" \
    -H 'Content-Type: application/json' \
    -d "{\"phoneNumber\":\"+9945011144$i\",\"fullName\":\"Yoxlama\"}"
done; echo
```

Expected: a `429` appears. A different header value per request must **not** buy a fresh bucket —
if it does, forwarded headers are being trusted from an untrusted source and every limit on the API
is bypassable.

## 5. Rate limits through the proxy

| Endpoint | Policy | Check |
|---|---|---|
| `POST /api/v1/auth/register` | 5 / 15 min | §3 |
| `GET /api/v1/listings/by-short-id/{id}/phone` | 20 / hour | 21 requests ⇒ a `429` |
| `GET /api/v1/listings` | 300 / min | normal browsing never throttles |
| `GET /api/v1/categories` | 600 / min floor | only reachable without a CDN |

## 6. Cache and privacy through the edge

```bash
curl -sI "$BASE/api/v1/categories" | grep -i cache-control   # public, max-age=3600
curl -sI "$BASE/api/v1/stores"     | grep -i cache-control   # public, max-age=300
curl -sI "$BASE/api/v1/listings"   | grep -iE 'cache-control|vary'  # private + Vary: Authorization
curl -sI "$BASE/api/v1/admin/audit" | grep -i cache-control  # no-store (401)
```

A user-varying response served as `public` by the edge is a data leak between visitors. If a CDN
sits in front, confirm it honours `private` and `Vary` rather than normalising them away.

## 7. HTTPS

- HTTP redirects to HTTPS.
- `Strict-Transport-Security` present on HTTPS responses.
- The certificate covers every name in `AllowedHosts`.

## 8. Smoke tests

Run after every deploy:

1. `GET /health` → 200, database reported healthy.
2. Homepage renders.
3. A public listing page renders with its images.
4. Search returns results.
5. Sign-in issues a token (OTP received through the real SMS path).
6. An admin can open the moderation queue.
7. An image upload round-trips and the stored file survives a container restart.
8. The payment callback route is reachable from the public internet at exactly the URL configured
   as Epoint's `result_url`:

   ```bash
   curl -s -o /dev/null -w '%{http_code}\n' -X POST "$BASE/api/v1/payments/callback/epoint" \
     -H 'Content-Type: application/x-www-form-urlencoded' --data ''
   ```

   Expected: `400` (signature missing) — the route answered. A `404`, a redirect, or a proxy error
   page means Epoint's deliveries would never arrive and every paid promotion would sit in
   `AwaitingPayment` until the expiry sweep's own re-check rescued it.
9. `GET /api/v1/promotion-packages` returns the live catalog (public, `max-age=900`).

---

## Results log

Record each run: date, environment, operator, and the outcome of §§1–8. A verification that was not
written down did not happen.

| Date | Env | Operator | §1 | §2 | §3 | §4 | §5 | §6 | §7 | §8 (1–9) |
|---|---|---|---|---|---|---|---|---|---|---|
| | | | | | | | | | | |
