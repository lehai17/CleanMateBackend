# Staff access changes revoke sessions permanently

Previously, changing a staff account to inactive blocked its JWTs only while
the account stayed inactive. Unlocking restored access to the same old JWTs.
This could restore a session held on a lost device or by a former operator.

`PUT /api/management/staff/{id}/access` now increments the existing TokenVersion
on every actual active/inactive transition. The state change, version increment
and StaffAccess audit commit in the same transaction. Unlocking permits a fresh
password/Google sign-in; pre-change JWTs never become valid again. Pre-change
email recovery/verification codes are invalidated by the same version boundary.
Passwords, roles, assignments and booking/cash state are unchanged.

Authorization remains Admin-only and targets remain Staff-only. ExpectedActive
continues to reject stale updates. Concurrent identical transitions produce one
success and one conflict. A request whose requested/expected state already matches
the target returns success without changing the session version or writing an
audit. A mismatched no-op returns conflict. No schema migration is needed beyond
the existing AddTokenVersion migration.

An integration regression reproduced the old behavior (expected 401 after unlock,
actual 200) before the fix. Coverage includes permanent revocation and fresh
sign-in, concurrent transitions, stale/no-op requests, pre-lock email codes and
rollback when the access audit insert is deliberately rejected. The admin browser
test locks and unlocks a newly created staff account, verifies the old session is
still rejected and confirms a new login works. Generated test databases are used;
no production account is locked or modified during validation.
