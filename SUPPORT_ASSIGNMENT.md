# Support responsibility and queues

Admin/Staff can filter `GET /api/management/support` with `assignment=All`,
`Mine` or `Unassigned`, combined with the existing status, search and pagination.
`Mine` uses the authenticated database user, never a client-supplied identity.
Management rows include assigned user ID/name, account activity and UTC assigned
time. Public support lists and conversations do not disclose these staff details.
Existing reports remain unassigned after the nullable-column migration.

`PUT /api/management/support/{id}/assignment` accepts `assignedToId` (positive
user ID or null to release) and the currently displayed ticket `version`.

- Staff can claim an unassigned report for themselves or release their own report.
  They cannot take another person's report or assign work to a colleague.
- Admin can assign, reassign or release reports. Targets must be active Admin/Staff.
  `GET /api/management/support-assignees` is Admin-only and returns IDs/names.
- Managers cannot assign their own reports; nobody can assign a report to its
  reporter. Existing private reporter-versus-management conversation rules remain.
- Assignment coordinates responsibility. All authorized managers may still reply
  or update status so colleagues can assist. Assignment does not auto-change status,
  close/reopen tickets or modify booking/payment records.

Each actual assignment change increments the same version used by messages and
status updates. Concurrent claims cannot both succeed. A stale form must reload;
an unchanged assignment with the current version is a no-op, without a new audit
or duplicate notification. Releasing clears the assignment time. Disabled assignees
remain visibly assigned until Admin reassigns/releases them; they are excluded from
the picker and lose API access under existing account security rules.

Assignment, audit metadata (before/after IDs and version) and the new assignee's
notification commit in one transaction. Notification failure rolls everything back.
Assigning another manager sends a generic `SupportAssigned:{ticketId}:{version}`
notification targeting that exact support report. Self-claims and releases do not
send an assignment notification. Later reassignment does not delete earlier inbox
history. Notifications contain report/booking codes, not message bodies or contact
details. The assignee opens the report to see its current assignment.

The admin portal provides queue filters, claim/release confirmation, an Admin
assignee picker and inline conflict/error messages. SQL Server and PostgreSQL
have `AddSupportAssignment` migrations. Tests apply migrations only to generated
SQL databases; PostgreSQL models/scripts are checked without a live server.
There is no SLA, automatic routing, evidence attachment or background push here.

Affected repositories: CleanMateBackend and CleanMateAdmin.
Branch: `support-assignment`.

```text
Add support assignment, staff queues, and assignment notifications
```
