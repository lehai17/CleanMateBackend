# Authentication limits

Branch: `auth-rate-limits`. No database migration is needed.

All roles use the same public authentication endpoints. Fixed-window limits are
per client IP, per endpoint and per API process:

| Endpoint | Default quota | Configuration section |
| --- | --- | --- |
| POST /api/auth/login | 30 requests / 300 seconds | RateLimits:Login |
| POST /api/auth/register | 10 requests / 3600 seconds | RateLimits:Registration |
| POST /api/auth/google | 30 requests / 300 seconds | RateLimits:Google |

Each section accepts `PermitLimit` (1..10000) and `WindowSeconds` (1..86400).
For example set environment variable `RateLimits__Login__PermitLimit=60`.
Successful, failed and invalid requests all consume quota. Changing the email,
password or role does not reset the IP's quota. No account is locked and no
credential, token version or signed-in session is changed by throttling.

Exhaustion returns HTTP 429, `Retry-After` and JSON `{ message, retryAfterSeconds }`.
The message includes the wait in seconds and contains no credential details.
The existing per-user password-change/logout-all quota remains separate.
These process-local limits reset on restart; distributed attack protection,
CAPTCHA and shared Redis quotas are not implemented. Users sharing an IP share
the corresponding quota. Set deployment limits for expected NAT/proxy traffic.

## Proxy trust

The API processes one `X-Forwarded-For` hop only from `Proxy:KnownProxies`, an
explicit IP list, defaulting to IPv4/IPv6 loopback for the local Node portals.
For a portal on another server, configure its exact backend-facing IP, for example
`Proxy__KnownProxies__0=192.0.2.10`. Unknown proxies cannot override the socket IP.
Do not configure a public client address as a trusted proxy.

The admin/cleaner Node portals overwrite incoming forwarding headers with their
socket peer IP. Browser-supplied fake headers cannot bypass the quota. They
preserve the backend's status, JSON body and `Retry-After`. A reverse proxy in
front of a Node portal is therefore counted as that portal's peer; its users
share quota. Configure edge throttling and deployment quotas for that topology.

Reference: [Microsoft's forwarded headers documentation](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).

## Password bounds

New customer/cleaner passwords still require at least 8 characters; staff/admin
creation requires 12. All new password hashes accept at most 72 UTF-8 bytes,
including registration, staff creation, admin bootstrap and password changes.
ASCII and accented text therefore have different maximum character counts.
Invalid creation requests create no account. Existing credentials are unchanged;
login accepts up to 128 characters for compatibility with existing accounts.

## Verification and clients

Dedicated real-HTTP/SQL integration tests use separate API instances with quotas
of two, check endpoint/IP separation, retry instructions, untrusted forwarding
headers, expiry and unchanged credentials. The databases are generated test
databases. General functional fixtures raise login/registration quotas to 10000
because hundreds of legitimate operations share one test-runner IP.

React, admin and cleaner web system tests inject a 429 response, verify readable
wait instructions and cleared login passwords, then retry against the real API.
Both native apps show the API's wait message through their existing error UI;
Android export validates compilation only, not physical-device interaction.

Repositories: CleanMateBackend, CleanMateFrontend, CleanMateMobile, CleanMateAdmin,
CleanMateCleanerWeb and CleanMateCleanerMobile. Commit separately in each:

```text
Add authentication rate limits and password input safeguards
```
