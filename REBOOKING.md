# Rebook a completed or cancelled order

Customer web and mobile offer **Đặt lại** on Completed/Cancelled orders.
The action opens an editable booking draft. The customer chooses a new time,
reviews the contact/address/notes and current quote, then explicitly confirms.
The existing booking and its cash declaration remain unchanged.

`GET /api/orders/{id}/rebooking` is authenticated and owner-only. Missing orders,
another customer's orders and orders assigned to the caller as cleaner return 404;
Admin/Staff receive no ownership bypass. Pending, Accepted and InProgress orders
return 409. This read creates no bookings, payments, notifications or assignments.

The response includes source ID, owner ID, service key, duration, address,
booking-time contact and plain-text notes. It excludes the previous price, schedule,
cleaner, request ID, rating and cash state. The service key comes from the existing
stored note prefix only when it matches a catalog entry, including an inactive one.
Only that first marker is removed. Unrecognized legacy text is preserved in full
and the client asks the user to choose a service.

Both clients fetch the current active catalog for each new repeat draft. Service
identity uses its key and survives a display-name change. Old hours are clamped to
current bounds with a visible notice; the customer can review/change them. A removed
or inactive service returns the draft to service selection with an explanation.
Contact/address/notes come from the original booking, with profile fallback for
missing values. A draft belonging to another logged-in account is ignored.

The schedule starts with no selected time. Price comes entirely from the current
catalog; submission uses the existing server-checked ExpectedPrice and a fresh
request ID. Duplicate-click protection and retry deduplication continue to apply.
The new booking starts Pending/Unpaid with no cleaner. The customer pays cash
directly to the cleaner after work. Rebooking does not promise the previous cleaner.

No new database columns or migrations are needed for rebooking. Tests use generated
SQL databases. API tests cover ownership, statuses, read-only behavior, original
contacts and legacy note markers. Browser tests cover changed price/name/duration,
retired service, mandatory new schedule, draft review and preservation of the source.
Native helper tests cover safe copied fields, current limits and account boundaries.
An Android bundle export checks compilation; physical-device UI testing remains.

## Registration regression found during verification

The full suite exposed SQL Server deadlock error 1205 while concurrent cleaner
signups wrote multiple manager notifications: FK validation acquired shared Users
locks while another signup held its newly inserted user. Notifications during
signup are now saved one recipient at a time, in ID order, in the same transaction,
avoiding the bulk MERGE foreign-key scan. This adds one save per recipient.
Registration also retries that
rolled-back operation at most twice with brief jitter. Each attempt rebuilds the
user/profile entities and notification IDs in a new transaction; password hashing
is performed once. Other errors, including deliberate notification constraint
failures, are not retried. PostgreSQL does not use this SQL Server retry. The larger regression
case registers eight cleaners concurrently with eight additional staff recipients,
then verifies one user/profile and one event per recipient for every signup.

Repositories: CleanMateBackend, CleanMateFrontend and CleanMateMobile.
Branch: `rebook-orders`.

```text
Add safe rebooking drafts to customer web and mobile
```
