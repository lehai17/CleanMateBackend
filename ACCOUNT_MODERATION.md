# Customer and cleaner account access

The management portal has an Admin-only Account area. It lists only Customer and
Cleaner accounts, with search, role/access filters and 20-item pagination. Staff,
customers and cleaners cannot use these endpoints, including through direct URLs.
Staff account administration remains in the separate Staff area; Admin accounts
cannot be locked through either of these public management routes.

- `GET /api/management/accounts?role=All&status=All&search=&page=1`
  accepts roles All/Customer/Cleaner and states All/Active/Locked. Search matches
  name, email, phone or numeric ID (`#123` is supported). Page must be 1–100000 and
  search is limited to 100 characters. Responses contain ID, name, email, phone,
  role, access state, accepted/in-progress job count and pending customer order
  count. Password hashes, session versions, addresses and email codes are omitted.
  Account responses use Cache-Control: no-store; the portal also disables caching.
- `PUT /api/management/accounts/{id}/access` takes
  `{isActive, expectedActive, reason}`. The trimmed reason must have 10–1000
  characters. Unknown/privileged targets return 404, stale state returns 409 and
  unauthorized roles return 403 (guests receive 401).

An actual transition atomically updates IsActive, increments TokenVersion and
writes AccountAccess audit with the reason, target role and session revocation
flag. Locking blocks current sessions and sign-in. Unlocking requires fresh
sign-in; old JWTs and pre-lock email codes stay invalid. Same-state requests do
not increment the version or create duplicate audits. Concurrent identical
transitions have one winner. An audit failure rolls back access and revocation.

The portal requires a reason and explicit confirmation, and displays unfinished
order counts and an operational warning before changing access. A stale form
refreshes the list without overwriting the newer state. Private account names
and the administrator's typed reason remain unchanged when switching languages.
The interface supports Vietnamese/English and the selected appearance.

Locking does not cancel, reassign or rewrite bookings. It does not change cash
receipts, prices, reviews, passwords, roles or staff assignments. Admins must
coordinate unfinished work through the existing order/support tools and contact
details. The counts are informational snapshots, not a reservation or automatic
incident-resolution workflow. No platform money movement is introduced.

Validation uses generated SQL databases. Coverage includes role isolation,
protected targets, pagination/filtering, response privacy, unfinished job counts,
customer/cleaner session revocation, historical booking/cash preservation, reasons,
stale/no-op updates, concurrency, email-code invalidation and audit rollback.
Browser checks cover both target roles, English labels/private names, required
reason/confirmation, unfinished-job warning, staff/direct-route restrictions and
stale forms on desktop/mobile. No application database migration is required.
