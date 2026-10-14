# Security review — what is preserved, what is eliminated

## Protections from the hardening branch, and their rebuild equivalent

| Legacy protection (REVIEW-REPORT ref) | Rebuild |
|---|---|
| S2 PBKDF2 passwords + lazy upgrade | ASP.NET Core Identity v3 hasher (PBKDF2-HMAC-SHA512, 100k iterations). Legacy `v1` hashes verified then re-hashed; plaintext hashed at import (never accepted at runtime). |
| S3 deny-by-default area filter | Fallback authorisation policy = authenticated; every endpoint group declares a policy (`Admin`, `Publisher`, `Member`) or explicit `AllowAnonymous`. Frontend route guards are UX only. |
| S4 global CSRF filter | Antiforgery middleware validates every unsafe `/api` request (`X-CSRF-TOKEN` header, issued by `GET /api/auth/me`, vs. the HttpOnly `erh.af` antiforgery cookie, bound to the user); `SameSite=Lax` auth cookie as a second layer. |
| S5 fail-closed permission check | Policies fail closed; unknown role → 403. |
| S6 application approval → publisher, own password | Same, plus role is a constant in code and approval is a conditional update (cannot run twice). |
| S7/S8 ownership + field whitelists | Ownership is part of every query (`WHERE PublisherId = @me`); request DTOs only contain editable fields, so over-posting is impossible by construction. |
| S9 review/notification ownership | Queries filter by owner; foreign rows return 404. |
| S10 upload validation | Extension allow-list per purpose, size caps, magic bytes, random names, storage **outside** the web root, private files only through an authorised endpoint, `nosniff` + sandbox CSP + attachment disposition for non-images. SVG no longer accepted. |
| S11 HTML sanitiser | `HtmlSanitizer` (Ganss.Xss) on write; React renders text by default, sanitised HTML only through one component. |
| S13 password change requires old password | Kept; also rotates the security stamp → other sessions are signed out. |
| S14 no error details, HttpOnly cookies, headers | ProblemDetails without exception data; HSTS, CSP, `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options`/`frame-ancestors`, `Permissions-Policy`; cookies HttpOnly + Secure (outside Development) + SameSite. |
| S15 throttle + signed single-use reset links | Per-IP rate limiter on auth endpoints **plus** persistent per-account lockout (5 failures → 15 min) in the database (fixes L16). Reset tokens: 256-bit random, only SHA-256 stored, 1 h, claimed with a conditional UPDATE (single use under concurrency), superseded by new requests, identical response for unknown e-mails, timing equalised with a dummy hash. |
| M1 locked bid transaction | Same `UPDLOCK, ROWLOCK` pattern in EF Core; clock read after the lock; integration-tested with concurrent requests on SQL Server. |
| M2–M4 wallet integrity | Conditional status UPDATE + atomic balance UPDATE + append-only ledger with a unique (transaction, reason) index, in one transaction; idempotency key on top-up requests. |
| M5 price terms frozen once bids exist; no delete with bids | Kept (and also freezes insurance and start time). |

## Legacy weaknesses eliminated

* L1 publisher reading any user's data → bidder details only for users who bid on *my* auctions, and only contact + those bids.
* L2/L3/L4 mass assignment and hashes in HTML → DTO whitelists; password hashes never leave the API (not even to admins).
* L5 wrong bidder list, L6 auction flip, L7 winner e-mail spam + `ex.ToString()` disclosure, L8/L9 broken CMS writes, L11 public customer pictures (opt-in now), L14 unvalidated contact form (validated + rate-limited), L15 anonymous settings dump (removed).
* Secrets: none in the repository. Configuration from environment / user-secrets; `.env.example` and `appsettings.Development.json` contain only local, non-secret values. E-mail defaults to a local pickup folder.
* Menu permission tables that "failed open" are not carried over.

## Threat-specific notes

* **IDOR**: integration tests change ids on every owner-scoped endpoint (auctions, images, reviews, bidders, notifications, transactions, files).
* **Admin access**: `/api/admin/*` requires the `Admin` policy; the admin login endpoint refuses other roles with the generic error.
* **File access**: `GET /api/files/{id}` returns 404 (not 403) for files the caller may not read, so ids cannot be probed.
* **Password reset / enumeration**: same body, status and approximate timing for known and unknown addresses.
* **Session invalidation**: security stamp checked on every request (cookie validation event); deactivating a user, changing or resetting the password ends sessions immediately.
* **Clickjacking**: `frame-ancestors 'none'` on the app, files served with `sandbox`.

## Residual risks / owner actions

* Rotate the SendGrid keys and SQL password that remain in the **legacy** git history (unchanged by this rebuild).
* Rate limits are per API instance (in-memory partitions). Account lockout is persistent; for a multi-instance deployment add a distributed limiter at the reverse proxy.
* `CardNumber` / `AccountNumber` are stored as plain text, as in the legacy system. They are not used for payments; if they are real card numbers (PAN) the business is in PCI-DSS scope — recommend removing the field or encrypting it (Always Encrypted / column encryption). Not changed without business approval.
* Malware scanning of uploads is not implemented (no scanner available); files are type-checked and never executed or served inline as HTML.
