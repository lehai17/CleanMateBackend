# Backend and system tests

Verified on 15 October 2026: 58 backend tests passed; build completed with zero
warnings/errors. The companion suite passed 14 frontend and 18 Playwright cases.

From backend/:

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
