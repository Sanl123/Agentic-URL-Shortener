# Legacy URL Shortener — Add Link Expiry

## Context

`LegacyUrlShortener` is a small, already-working URL shortener fixture with a passing baseline test
suite. It supports creating a short link (code + original URL + creation timestamp) and resolving a
code back to its link. It has **no concept of link expiry** today.

## Requirement

Add optional link expiry support:

1. A link may optionally be created with an expiry timestamp.
2. Resolving an **expired** link must report it as not found (the same outward behavior as an
   unknown code), not throw.
3. Resolving a **still-valid** (non-expired) link must continue to succeed.
4. Links created **without** an expiry must continue to resolve exactly as before — this must not
   regress existing behavior or existing call sites.

## Acceptance criteria

- **AC1**: An expired link resolves to not-found.
- **AC2**: A valid (future-expiry) link resolves successfully.
- **AC3**: A link created with no expiry still resolves successfully (regression check).
- **AC4**: All pre-existing baseline tests continue to pass unmodified.

## Constraints

- Work must happen in an isolated copy of the fixture. The original fixture on disk must remain
  unmodified by this scenario.
- Build and test the fixture for real after each change; do not assume success.
