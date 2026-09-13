# SMS gateway — Poctgoyercini

`PoctgoyerciniSmsSender` (`backend/src/Ovcuprim.Infrastructure/Sms/PoctgoyerciniSmsSender.cs`) is
an `ISmsSender` adapter for the `poctgoyercini.com` gateway — the same provider Bumer.az's own site
already sends through. This document is the provenance for the request/response contract it
implements, and the record of what is still an open question with the provider rather than a
decided fact.

## What this is, and is not

The adapter is a pure transport: given a phone number and a fully-composed message, it sends that
message and returns, or throws `SmsDeliveryException`. It does not generate a one-time code, does
not decide how long one is valid, does not track resend cooldowns or verification attempts, and
does not create or sign in a user. All of that remains exactly what it was —
`Ovcuprim.Application.Auth.OtpService` and `AuthService` — unmodified. Nothing about the
authentication architecture changed to add this.

## The gateway contract (source: Bumer.az's own integration)

Read directly from Bumer.az's `wmvaction.php` during a read-only audit — not from the provider's
own documentation, which was not available to inspect.

- **Endpoint:** `POST https://www.poctgoyercini.com/api_json/v1/Sms/Send_1_N`
- **Request body (JSON, exact casing):**
  ```json
  { "Username": "...", "Password": "...", "Message": "...", "Receivers": ["994501234567"] }
  ```
  `Receivers` entries are digits only — no leading `+`. OvcuPrim's own `PhoneNumber.Normalize`
  produces the E.164 form (`+994...`); the adapter strips the leading `+` before sending, matching
  what Bumer's integration does, and introduces no second phone-validation path to get there.
- **Success:** the response body is JSON with an integer `StatusCode` field; `200` means accepted.
  Any other value, a non-2xx HTTP status, or a body that is not parseable JSON are all treated as
  failure.
- **No SDK.** Bumer's integration is a raw HTTP POST with no vendor library, and so is this adapter.

## Configuration

| Key | Environment variable | Required outside Development |
|---|---|---|
| `Sms:Poctgoyercini:Username` | `Sms__Poctgoyercini__Username` | Yes, to activate this gateway |
| `Sms:Poctgoyercini:Password` | `Sms__Poctgoyercini__Password` | Yes, to activate this gateway |

Both are read into `PoctgoyerciniSmsOptions`. No sender ID or account ID is configured separately —
the gateway's `Send_1_N` endpoint takes only these two credentials plus the message and receiver.

**Activation is opt-in, not automatic.** `AddInfrastructure`
(`backend/src/Ovcuprim.Infrastructure/DependencyInjection.cs`) registers `PoctgoyerciniSmsSender`
only when *both* values are present and non-blank outside Development. If either is missing, the
existing `UnconfiguredSmsSender` placeholder stays registered — the same "throw loudly on the first
OTP request rather than silently deliver nothing" behaviour this API already had. Development is
unaffected either way: it always gets `DevelopmentSmsSender`, regardless of whether gateway
credentials happen to be present in its configuration.

## What the Bumer.az audit confirmed

A separate, read-only audit of Bumer.az's own codebase and live server (not OvcuPrim's) established
two facts worth recording here, since they bear directly on what goes into the configuration above:

- **The account username is `bumer_s`.** This is a non-secret identifier — safe to write down, the
  same way any username is — and is recorded here purely as provenance. It is not, on its own,
  evidence that OvcuPrim should or may use it; see the open question below.
- **The password paired with that username was found exposed in Bumer's historical source and
  configuration files, and must be treated as compromised.** Bumer's own migration notes
  independently reached the same conclusion and call for rotating it. **Whatever value eventually
  goes into `Sms__Poctgoyercini__Password` here must not be that historical value** — either a
  freshly rotated password for the `bumer_s` account, or the credential for a separate account
  obtained for OvcuPrim, but never a credential copied out of Bumer's old source or config. No such
  value has been, or should be, copied into this repository at any point — a guardrail test
  (`NoBumerCredentialLeakageTests`) exists specifically to keep it that way.

## What is still an open question with the provider

Deliberately not assumed anywhere in this adapter or its configuration:

- **Whether Bumer.az's existing `poctgoyercini.com` account may be reused for OvcuPirim.az**, or
  whether a separate account is required. Knowing the account's username (above) is not the same as
  knowing the answer to this — nothing in Bumer's code or Bumer's own audit says either way. This is
  an account/contract question for whoever holds that relationship, and for the provider.
- **Sender ID.** Bumer's request payload carries no sender-name field at all, meaning whatever name
  a recipient sees is configured on the provider's account dashboard, tied to the account's
  `Username` — not something this adapter, or any request it sends, controls.
- Any IP/domain whitelist, volume cap, or contract term tied to the account.

The adapter is built so either answer plugs in the same way: point
`Sms__Poctgoyercini__Username`/`Password` at whichever account is actually approved for OvcuPrim —
the existing Bumer one (**after** its password is rotated), or a newly obtained one — and nothing
else changes.

## Before setting real values in production

- [ ] Reuse (same account) vs. a separate account has been confirmed with whoever holds the Bumer
      relationship and/or the provider — not assumed by whoever is deploying.
- [ ] Sender ID for OvcuPirim.az specifically has been confirmed.
- [ ] The password being configured is a **current, freshly issued or rotated** credential — never
      a value found in Bumer's historical source or configuration.
- [ ] `Sms__Poctgoyercini__Username`/`Password` are set as environment variables (or another secret
      store), never committed to source — the same rule every other production secret in this
      project already follows.
