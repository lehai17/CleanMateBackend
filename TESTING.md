# Backend and system tests

Verified on 16 October 2026: 93 backend tests passed; build completed with zero
warnings/errors. The companion suites passed 25 frontend tests, 32 customer-web
Playwright cases, 10 admin Playwright cases, 16 cleaner-web Playwright cases,
8 cleaner-web Node tests, 7 cleaner-mobile tests and 1 admin server integration test.

From backend/:

See PROJECT_STATUS.md for feature scope, remaining work and the booking-support
workflow. New SQL Server/PostgreSQL migrations add SupportTickets; no application
database was migrated during testing. Support tests cover private ownership,
concurrent retry deduplication, validation, staff version conflicts, atomic
audit/notifications and unchanged booking/cash records after staff resolution.

```powershell
dotnet restore CleanMate.sln
dotnet build CleanMate.sln --no-restore
dotnet test CleanMate.Tests/CleanMate.Tests.csproj --no-build --no-restore
```

Tests use a generated CleanMateTests_<guid> SQL Server database, apply all
migrations, exercise the real API and MVC portal, and delete that database after
completion. Application connection strings and existing data are never used.
The Windows account needs create/drop database permission on the local SQL Server
instance. CLEANMATE_TEST_SQL may override the server and authentication settings;
its database name is always replaced with a generated test-only name.

Both provider models are compared with their migration snapshots. PostgreSQL
migration SQL is generated and checked without connecting to a live PostgreSQL
server. A live PostgreSQL upgrade remains a separate deployment check.

CleanMate.SystemTests.Host binds the API, React production build and MVC portal to
loopback ports 15238, 13000 and 15080. It uses a separate generated database.
The shutdown control exists only in that test executable. See ../web/TESTING.md.

## Management portal

The standalone portal lives in ../admin with its own Git repository. See
../admin/README.md for startup, the local --create-admin bootstrap command and
the role matrix. It shares this API and database. Public registration cannot
grant Admin/Staff; applications require review before cleaner job access.
Staff can review applications and search bookings. Admin alone can change
service prices or manage Staff access. Locked Staff tokens stop working.

The management migrations exist for SQL Server and PostgreSQL. They preserve
existing approved cleaners and seed the previous four service rates. New
applications default to Pending. Prices and duration limits come from the
catalog; existing booking prices do not change. ExpectedPrice conflicts require
the client to refresh its quote. Mutations write audit records in the same
transaction. Tests cover permissions, concurrent reviews, stale price versions,
token invalidation, legacy migration and admin bootstrap.

No production migration/bootstrap was executed by the tests. SQL Server upgrade
was exercised on a generated legacy database; PostgreSQL SQL/snapshots were
checked, but a live PostgreSQL migration still requires deployment validation.

## Direct cash between customer and cleaner

CleanMate connects customers and cleaners. Customers hand cash directly to their
cleaner after work finishes. The platform does not receive, hold, transfer or
refund money, and does not calculate commissions, balances or cleaner payouts.

The assigned cleaner can use PUT /api/cleaner/jobs/{id}/cash-payment only after
Completed. This records a self-reported declaration of receiving the full booking
price. Receipt source is CleanerDeclaration. Paid/paidAt are retained API names
for compatibility: they represent the declaration and server confirmation time,
not a bank-verified transaction or independently verified cash exchange.

Permissions, a unique booking key and idempotent responses prevent duplicate
records and preserve the original confirmation time. Server booking price and
collector identity are authoritative; client amount, identity and time fields
are ignored. No payment record is created by merely completing a job.

GET /api/payment/options always reports cash=true, momo=false, payos=false and
model=DirectCash. Previous provider creation, reconciliation and callback routes
return 410 Gone. Provider services, credentials, fake gateways and checkout code
have been removed. Old enabled settings cannot reactivate those routes.

Earlier provider migrations and nullable schema columns are retained to support
databases that may already have applied those migrations. No existing records are
deleted or silently converted to cash. A legacy non-cash record is labelled for
review and blocks a second cash declaration on the same booking.

## Customer completion and feedback

PUT /api/orders/{id}/confirm-completion accepts rating (1..5) and optional review
(up to 1000 characters). Only the booking owner can confirm a Completed job.
It stores a server UTC CustomerConfirmedAt, CustomerRating and CustomerReview.
Repeated identical submissions preserve the first timestamp; different feedback
returns 409. The review is immutable through this endpoint.

Customer confirmation never changes payment state or creates a cash receipt.
Cleaners see the customer confirmation and rating in their job list. Public
cleaner ratings use the average of confirmed booking ratings, falling back to
an existing profile rating when no confirmed ratings exist.

Tests cover ownership, premature confirmation, invalid ratings/reviews,
concurrent submissions, immutable timestamps, cash independence, retired online
routes, legacy records, lifecycle transitions and cash declaration concurrency.

Migrations add nullable feedback fields without fabricating historical feedback.
SQL Server UTC converters prevent browsers and mobile clients from interpreting
stored UTC timestamps as local time.

## Cleaner scheduling

Accepted and InProgress jobs reserve their scheduled interval. Available jobs
exclude overlaps with the current cleaner's reservations; the acceptance API
rechecks under a database transaction and profile row lock. Both acceptance
routes use this rule. Locks serialize concurrent claims and starts across API
processes, while a conditional booking update arbitrates competing cleaners.

Intervals use UTC start time plus duration, including overnight jobs. Adjacent
intervals are allowed; there is no travel-time buffer. Completed and Cancelled
jobs do not reserve the schedule. Only one job per cleaner may be InProgress,
even when the planned intervals do not overlap. Completing it unlocks starting
another job. This change requires no additional migration.

Tests cover partial/full overlaps, midnight and timezone offsets, adjacent jobs,
concurrent overlapping claims, competing starts and completion releasing a slot.
SQL Server locking is exercised against a real database. PostgreSQL uses FOR
UPDATE; its runtime locking and query execution still require a live PostgreSQL
check. Historical conflicting assignments are not automatically modified.

## Customer contact with the assigned cleaner

GET /api/orders includes a nullable cleaner object containing only name and
phoneNumber. The object is null until a cleaner is assigned. Existing owner
filtering applies before projection; guests cannot access orders and another
account cannot retrieve the owner's booking/contact through this endpoint.
Email, private home address and authentication data are excluded from this object.

Contact details reflect the cleaner's current profile, including after job
completion. A missing phone number is allowed and does not hide the cleaner's
name. No migration is required. Integration tests cover assignment, ownership,
missing phone, profile changes and contact persistence through completion.

## Contact person for each booking

POST /api/bookings accepts optional contactName (1..200 trimmed characters) and
contactPhone (at most 40 characters, containing 6..15 digits with an optional
leading plus and formatting spaces/parentheses/dots/hyphens). Explicit empty or
invalid values return 400 without creating a booking. Web and native mobile send
the contact entered in the booking form rather than discarding it.

For clients omitting these fields, the current account name/phone are copied once
when creating the booking. A missing account phone remains null. Later account
profile changes do not alter that booking's contact or owner. Customer orders
return contactName/contactPhone, and cleaner job endpoints use these values for
customerName/customerPhone. The MVC portal also displays the contact phone.

AddBookingContact and AddBookingContactPostgres add nullable columns without
inventing historical contact details. Older rows with no contact name retain the
previous profile fallback in cleaner lists. SQL Server migrations and the live
API are tested; PostgreSQL schema/model checks do not replace a live upgrade test.
Integration coverage includes separate contact/account identities, profile edits,
old clients, absent phones, legacy rows, invalid input and length boundaries.

## Safe booking retries

POST /api/bookings accepts an optional nonempty UUID requestId. A unique
(UserId, RequestId) database index and a stored SHA-256 fingerprint of normalized
booking input ensure simultaneous requests with the same owner/key create one
booking. An unchanged replay returns its ID, original quote/start and current
status; it never resets a cancelled or progressed booking. Profile changes do
not alter the fingerprint for clients that omitted contact fields.

Reusing a key for different booking input returns 409. Validation failures do
not consume keys. Different owners can use the same key independently, and a
new key allows a deliberate new booking with identical details. Old clients
omitting requestId remain supported but receive no retry deduplication.

AddBookingRequestId and AddBookingRequestIdPostgres add nullable metadata and
the unique index without changing historical orders. The existing owner index
is retained for history queries, including legacy orders with null keys. Tests
exercise SQL Server concurrency, isolation, validation and cancelled replays.
PostgreSQL is checked for model/schema consistency, not live execution.

## Reschedule an unassigned booking

PUT /api/orders/{id}/reschedule accepts startTime and expectedStartTime (the
original UTC date returned by the orders API). Only the owner can reschedule a
Pending, unassigned booking whose current start time is still in the future.
The new time must be in the future and within six months, matching creation rules.
Price, duration, contact, ownership, payment and order ID are unchanged.

The conditional update checks both status and expectedStartTime. Competing
schedule edits from the same old date allow one winner; stale views receive 409.
Cleaner acceptance also compares the start time used for its overlap check,
preventing a concurrent schedule change from bypassing the cleaner's reservation.
Cancellation cannot be undone by rescheduling. Accepted, InProgress, Completed,
Cancelled and expired Pending bookings cannot be rescheduled.

Creation request IDs/hashes are retained. Replaying the original creation request
returns the same booking with its current schedule. This feature adds no schema
migration. SQL Server integration tests cover ownership, UTC offsets, tampered
fields, invalid dates, lifecycle restrictions and concurrent edit/claim/cancel.

## In-app notifications

AddNotifications (SQL Server) and AddNotificationsPostgres create a recipient-
scoped Notifications table with UTC timestamps, booking/recipient foreign keys,
unread/history indexes and a unique (UserId, EventKey). No historical events are
backfilled. Deploy this migration before the new API: writes now include event
notifications in the same database transaction as the booking/payment change.

Customers receive booking creation, assignment, start, completion, changed time,
cancellation and cleaner-declared cash receipt. Cleaners receive assignment and
customer feedback. Cash messages explicitly describe a cleaner declaration.
Creation/cash/feedback/cancellation retries do not duplicate notifications; a
no-op reschedule emits none. The current cleaner portal's API actions generate
the same notifications, available in the React/native inboxes.

Authenticated endpoints:

- GET /api/notifications?beforeId=<id>: newest first, 20 items, nextBeforeId and
  total unreadCount. The cursor is optional; no other user's records are returned.
- GET /api/notifications/unread-count: current recipient's unread count.
- PUT /api/notifications/{id}/read: preserves the first ReadAt on repeat calls;
  missing/other recipients' IDs return 404.
- PUT /api/notifications/read-all with { throughId }: reads only the recipient's
  rows up to that viewed ID, leaving newer arrivals unread.

Integration tests cover recipients, lifecycle delivery, concurrency, replay
deduplication, UTC/read timestamps, paging, bulk-read boundaries and rollback
when a notification insert is deliberately rejected in the isolated test DB.
PostgreSQL model/schema checks remain separate from a live PostgreSQL execution.
There are no email, SMS, device push, browser permission or external delivery
services in this feature.
