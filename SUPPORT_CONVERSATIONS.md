# Private support conversations

Branch: `support-conversations`.

Customers and assigned cleaners can open **Xem trao đổi** on their own support
reports, send follow-up messages and load earlier messages. Admin/Staff use the
same action in the management portal to read and reply. Messages are plain text;
HTML is escaped. The original report and latest processing response remain visible.

## API and privacy

- GET/POST `/api/support/{id}/messages`: only the ticket's original reporter.
  The other booking party cannot read or reply to that report's conversation.
- GET/POST `/api/management/support/{id}/messages`: active Admin/Staff, checked
  from the database. Staff cannot reply through management to their own report.
- GET accepts optional positive `beforeId`. It returns up to 20 messages in
  chronological order and `nextBeforeId` for older messages. Authors are labelled
  reporter/staff; user IDs, names, emails and request IDs are not exposed.
- POST accepts `{ body, requestId }`, with 1..2000 trimmed characters and a UUID.
  Identity and staff attribution come from the authorized route, not the payload.
  The response contains `id`, current `version` and `status`.

Retrying the same author/request ID, ticket and body returns the existing message.
Reusing the ID with different content/ticket returns 409. Web/native drafts and
the request ID survive a failed send while the component remains mounted. They
are cleared after acknowledgement, and are not persisted across navigation or
app restart. Each account has a shared, process-local quota of 20 sends/minute across both routes;
429 includes retry instructions. Validation failures also consume quota.

## Workflow and consistency

Each append advances the ticket version and updated time. A reporter's message
reopens a Resolved ticket as Open, preserving its last response and all history.
Staff must explicitly reopen a resolved ticket before sending a new reply.
Competing status edits receive 409 instead of overwriting a newly advanced version.
The management page uses the version acknowledged by its message send.

New/different nonempty processing responses are also appended to history. Existing
reports/responses are preserved as-is; migrations do not invent historical
messages for old overwritten responses. The original description/latest response
remain available outside the supplementary message list.

Message insertion, version/status update, audit and staff-reply notification are
one transaction. Audit stores message ID, length, origin and reopen flag, not the
message body. A staff reply creates one generic notification for the reporter;
retry does not duplicate it. New reporter messages move the ticket to the top of
the management list on refresh. The later staff-notifications feature adds alerts
and a management inbox for incoming reports/messages; see STAFF_NOTIFICATIONS.md.

Messages do not alter booking status, prices, cash declarations or payment records.
Threads refresh explicitly with **Tải lại trao đổi**; inbox notifications use the
existing foreground polling. This is not background push or a live WebSocket chat.
There are no attachments, message editing/deletion, read receipts or staff assignment.

## Schema and validation

SQL Server `AddSupportMessages` and PostgreSQL `AddSupportMessagesPostgres` add
`SupportMessages`, restrictive ticket/author foreign keys, a unique author/request
index and a ticket/message pagination index. The API applies its selected provider's
migrations at normal startup. Only generated test databases were migrated here.

Integration tests cover both reporter roles, the other booking party's exclusion,
staff attribution, UTC dates, concurrent replay, mismatched retry payloads,
pagination, validation/rate limits, reopening and unchanged booking/cash state.
A deliberate notification-write failure verifies rollback and a later successful
retry. SQL migration/model tests and PostgreSQL generated-script/model checks pass;
a live PostgreSQL upgrade still requires a separate environment.

Browser tests cover lost-response retry, customer/cleaner history and staff replies,
notification navigation and resolving after a reply without a stale version.
React tests cover drafts/request IDs, text escaping and older-message pagination.
Both native apps export Android bundles; no physical-device UI test was performed.

Updated repositories: CleanMateBackend, CleanMateFrontend, CleanMateMobile,
CleanMateAdmin, CleanMateCleanerWeb and CleanMateCleanerMobile. Commit text:

```text
Add private support conversations and reply history
```
