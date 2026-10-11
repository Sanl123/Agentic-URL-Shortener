# URL Shortener — Greenfield Requirements

Build a minimal URL shortener service from scratch with the following behavior.

## Functional requirements

1. Create a new short link given an original absolute `http`/`https` URL. The system assigns a
   unique short code automatically — the caller does not choose it.
2. Resolve a known short code back to its original URL (redirect behavior).
3. Reject attempts to create a link with an invalid URL (not absolute, or not `http`/`https`).
4. Looking up an unknown short code must not throw an exception — it must be reported as not found.

## Acceptance criteria

- **AC1**: Creating a link returns a generated code and the original URL.
- **AC2**: A created code resolves back to its original URL.
- **AC3**: Creating a link with an invalid URL is rejected.
- **AC4**: Looking up a code that was never created returns "not found", not an exception.

## Out of scope for this scenario

- Authentication, rate limiting, analytics, and persistence beyond the process lifetime are out of
  scope. This is a minimal vertical slice intended to exercise the greenfield workflow end to end
  (analyze → inspect → decompose → implement → build → test → report), not a production system.
