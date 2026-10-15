# Backend and system tests

Verified on 15 October 2026: all 27 backend tests passed, including a fresh SQL
Server migration. Backend build completed with zero warnings and zero errors.
The companion frontend Playwright suite passed all 14 desktop/mobile browser cases.

```powershell
dotnet restore CleanMate.sln
dotnet build CleanMate.sln --no-restore
dotnet test CleanMate.Tests/CleanMate.Tests.csproj --no-build --no-restore
```

The integration tests require a local SQL Server default instance and Windows
authentication with permission to create and drop databases. Alternatively set
`CLEANMATE_TEST_SQL` in the test terminal; its database name is always replaced by
a generated test-only name. Application database settings are never used.

The suite applies migrations to a new `CleanMateTests_<guid>` database, exercises
the real API and MVC portal, and deletes only that generated database afterward.
The PostgreSQL model and generated migration script are checked without a live
PostgreSQL connection.

`CleanMate.SystemTests.Host` is a local test executable for the frontend Playwright
suite. It binds only loopback addresses and uses a separate
`CleanMateSystemTests_<guid>` database. Its shutdown endpoint is present only in
this test executable, not the application API. Keep the sibling frontend checkout
at `../web` and follow `../web/TESTING.md` to build and run the browser tests.

## Migration corrections

- SQL Server adds the missing nullable `Users.Address` column.
- PostgreSQL adds missing address, phone and booking status columns, and marks
  existing assigned bookings as `Accepted`.
- Provider snapshots now describe their actual native schema. The PostgreSQL
  provider uses a stable version matching the EF Core 9 dependency family.
- Existing booking price precision is preserved; migrations do not round prices
  or rewrite existing price columns.
- Lifecycle migrations add nullable `Bookings.StartedAt` and `Bookings.CompletedAt`
  columns for SQL Server and PostgreSQL.
- Application startup now fails when migrations fail instead of serving requests
  against an incomplete schema.

No migration is applied to an existing application database by these test commands.
A live PostgreSQL upgrade and existing deployment data still need separate
verification before deployment.

## Work progress

The assigned cleaner manually confirms work using these authenticated endpoints:

| Endpoint | Required current state | Result |
| --- | --- | --- |
| `PUT /api/cleaner/jobs/{id}/accept` | Unassigned `Pending` | `Accepted` |
| `PUT /api/cleaner/jobs/{id}/start` | Own `Accepted` booking | `InProgress`, records `StartedAt` |
| `PUT /api/cleaner/jobs/{id}/complete` | Own `InProgress` booking | `Completed`, records `CompletedAt` |

Every update checks state and ownership in a single database statement. Repeated
or out-of-order progress updates return 409; a different cleaner or customer gets
403, anonymous requests get 401, and an authenticated cleaner requesting a missing
booking gets 404. Completed and cancelled jobs cannot restart. Pending cancellation
rules remain unchanged.

`StartTime` is the planned appointment. `StartedAt` and `CompletedAt` are actual
server-recorded times. Progress is a manual confirmation, not triggered by the
appointment clock. These nullable UTC fields are included in cleaner job and
customer order responses. SQL Server values retain their UTC kind on read so
clients display the correct local time. Existing bookings keep null actual times
until a real transition occurs; no historical times are fabricated.

The React web, native mobile job list/calendar and MVC portal expose the next
permitted action and reload actual server state after a command or conflict.
Job completion records work progress; payment processing remains a separate step.
