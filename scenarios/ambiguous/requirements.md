Make redirects fast and analytics accurate.

## Considerations (intentionally unresolved — do not assume answers)

- What is the target redirect latency, and how will it be measured (p50/p95/p99, at which tier)?
- What does "accurate" mean for analytics — exact counts, a bounded error margin, or eventual reconciliation?
- Should click recording happen synchronously on the redirect path, or asynchronously out-of-band?
- Is eventual consistency between a redirect and its recorded analytics event acceptable, and for how long?
- What data-loss window (if any) is acceptable for analytics events during a failure?
- What is the expected traffic volume/scale this needs to support?
- Is changing infrastructure or the existing database schema permitted to achieve this, or must it stay within current constraints?

This requirement is deliberately under-specified. It must not be used to justify a concrete
implementation decision (latency targets, synchronous vs. asynchronous recording, schema changes,
etc.) without the considerations above being resolved and explicitly approved by a human.
