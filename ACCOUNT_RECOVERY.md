# Password recovery and email verification

All four roles use the shared recovery API. Customer web/mobile, cleaner
web/mobile and the admin/staff portal provide **Quên mật khẩu?** before login,
and **Xác minh email** in the security page after login.

## API and security

- `POST /api/auth/forgot-password` accepts `{email}`. Configured delivery returns
  the same 202 message for existing, unknown, inactive and Google-only accounts.
  Account lookup and email delivery run in a bounded background queue, so the HTTP
  request does not wait for an SMTP operation only for known users.
- `POST /api/auth/reset-password` accepts `{email,code,newPassword}`. The new
  password needs 12+ characters and at most 72 UTF-8 bytes. Confirmation remains
  a client check. Google-only accounts continue recovering through Google.
- `POST /api/auth/request-email-verification` requires a current JWT and sends only
  to that account's stored email. Already verified accounts return their status.
- `POST /api/auth/verify-email` accepts `{email,code}` and proves possession of the
  delivered verification code. Verification does not grant a cleaner approval,
  change roles, or replace staff document checks.

Codes contain 32 cryptographically random bytes encoded as 64 hex characters.
Only SHA-256 hashes are stored. Codes are bound to account, purpose and token
version, expire after 30 minutes, require successful recorded delivery and are
single use. A unique account/purpose/two-minute issuance bucket bounds resends
across API instances. Changing a password or revoking all sessions invalidates
earlier codes through the existing token version.

Reset atomically consumes the code, changes the password, increments the token
version and writes audit/notification records. Concurrent resets have one winner.
Verification atomically consumes its code, marks EmailVerifiedAt and writes its
audit/notice. Failed writes roll back the entire operation. A successful reset
requires explicit login and queues a best-effort password-changed email notice.

Request endpoints share 10 attempts/600 seconds/IP; consume endpoints share
30 attempts/900 seconds/IP. `RateLimits:RecoveryRequest` and `RecoveryConsume`
support the same PermitLimit/WindowSeconds settings as the existing auth quotas.
429 includes Retry-After. The existing trusted-proxy rules apply.

Raw codes never appear in production API responses, application logs, storage
preferences, URLs or notification previews. The test mailbox is compiled only into
the test executables; its loopback test routes are absent from CleanMate.Api.
Existing users remain able to log in and book; verification is not retroactively
required. A validated Google login marks its verified email as verified.

## SMTP deployment prerequisite

No external email was sent during implementation or tests. Configure a real SMTP
relay and verify delivery before advertising recovery as available in production.
Missing/obviously invalid configuration returns 503 for every email address.
The API uses environment variables (or equivalent ASP.NET configuration):

```text
Email__Enabled=true
Email__From=no-reply@your-domain.example
Email__Smtp__Host=smtp.your-domain.example
Email__Smtp__Port=587
Email__Smtp__Username=your-smtp-user
Email__Smtp__Password=your-secret-from-deployment-secret-storage
```

The built-in transport requires STARTTLS and always enables TLS. Port 465 implicit
TLS is not supported by this transport. Use a relay supporting SMTP authentication
over STARTTLS or a trusted STARTTLS relay without authentication. A username
requires a password. Secrets belong in deployment secret storage, never Git.
Authentication/delivery failure is logged without address, code or SMTP exception
details. Failed delivery never activates a code; retry in a later issuance bucket.

The queue is in memory, bounded at 100 requests per API process. Queued requests
can be lost on restart; SMTP success and the delivered marker can also be
interrupted. In those cases request a fresh code in the next bucket. This version
does not provide a durable mail outbox or delivery-provider monitoring. Confirmed
reset does not roll back if its later informational email cannot be sent.

Both database providers have AddAccountRecovery migrations. Only generated SQL
databases were migrated for tests; live PostgreSQL/production upgrades remain a
deployment check. Both Android exports validate bundles, not physical-device UI.

The implementation follows the [OWASP forgot-password guidance](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html)
on uniform responses and expiring single-use codes; transport behavior follows
[Microsoft's SMTP documentation](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient.sendmailasync?view=net-8.0).

Branch: `account-recovery`. All six repositories are affected. Commit:

```text
Add email password recovery and account verification for all roles
```
