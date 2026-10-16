# Admin and staff notification inbox

Branch: `staff-notifications`. Updated repositories: CleanMateBackend and
CleanMateAdmin. Customer/cleaner clients keep using the same compatible API.

Active Admin/Staff receive in-app notifications for:

- A cleaner application submitted during signup or from an existing customer.
- Each successful resubmission after NeedsInfo/Rejected.
- A new customer/cleaner support report.
- A reporter's follow-up message, including reopening a resolved report.

Recipients are captured from stored roles/active flags before taking write locks.
Public signup cannot grant these roles. The reporter is excluded from manager
delivery for their own reports/messages. Staff replies continue to notify the
reporter; they do not echo to the whole management team. Notification previews
contain resource codes and generic descriptions, not application/contact details
or message/report bodies. New staff accounts do not receive historical backfill.

The business change and all its notification rows commit in one transaction.
Cleaner signup saves manager notifications individually in recipient ID order to
avoid bulk-insert FK validation contending with other newly inserted users.
Registration now retries a SQL Server deadlock victim at most twice, rebuilding
the entire user/profile/notification unit after rollback. Other write failures
still fail atomically. This regression was found while testing rebooking; see
[REBOOKING.md](REBOOKING.md).
Support request/message retries preserve the existing event, and competing
application resubmissions have one winner. A later resubmission cycle creates a
fresh event. A failed notification insert rolls back the originating signup,
profile submission/resubmission, ticket or message. No background delivery worker
or external messaging service runs as part of this feature.

## Admin portal

The **Thông báo** navigation item shows an unread badge (99+ for larger counts).
Counts refresh on navigation/focus, when becoming visible and every 30 seconds
while foregrounded. Logged-out/hidden pages do not poll. Account/token and request
sequence checks discard stale counts and stale unauthorized responses. Read
acknowledgements invalidate older pending badge requests.

The inbox supports refresh, older pages, individual read and bulk read. Opening
an item does not mark it read. Bulk read keeps the newest ID from the first viewed
page; loading older pages never advances that boundary, so arrivals after opening
the inbox remain unread. Read state is stored on the server per recipient.
Failures retain available state and allow retry; other recipients' read IDs
return 404 through the existing API.

**Xem hồ sơ** filters the application list to exactly `HS{id}` with status All,
so an already processed application still opens correctly. **Xem hỗ trợ** filters
to exactly `HT{id}`. These are fixed portal destinations, not URLs supplied by
notification content. Existing Profile/security notifications are also supported.
Unknown targets have no action. These filters are local portal state, not new
shareable deep-link URLs.

## API and schema

Existing `/api/notifications` endpoints remain recipient-scoped. Their list
response adds nullable `targetId`; old clients and historical rows remain valid.
`TargetId` is interpreted together with `Target`, not as a universal foreign key.
The management application search now accepts an exact positive `HS` code in
addition to existing name/email/city searches. Management permissions still apply.

SQL Server `AddNotificationTargets` and PostgreSQL `AddNotificationTargetsPostgres`
add only the nullable Notifications.TargetId column. Normal API startup applies
the configured provider's migrations. Tests use generated SQL databases only;
no application database was migrated here. Live PostgreSQL upgrade verification
still needs a separate environment.

Tests cover recipient roles/active flags, exact resource navigation, exclusion of
report content, concurrent signup and resubmission, event deduplication and rollback
for all five event entry points. Admin browser tests cover read persistence,
pagination, a newer arrival during bulk read and exact resource opening. Node
tests cover stale account/count responses, hidden/logged-out polling and the proxy.
Customer and cleaner browser suites run against the updated shared API as regressions.

This remains foreground in-app polling, not browser/device background push or
real-time delivery. Support assignment, push delivery, email verification/recovery
and cleaner document uploads are separate work.

```text
Add staff notification inbox and operational event alerts
```
