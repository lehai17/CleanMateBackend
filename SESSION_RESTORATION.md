# Session restoration and asynchronous identity guards

All clients keep saved credentials through a temporary profile/network failure.
The restore screen offers a retry and an explicit choice to clear the session and
use another account. A real 401 invalidates the saved session. Customer web no
longer trusts/parses the cached profile before checking the current identity, so
a corrupt cached profile cannot crash startup. Language and appearance preferences
remain independent of authentication and survive these flows.

React web/native use a token plus operation generation to ignore old restore
results. Unauthorized responses are acted on only when the request's bearer token
matches the current token. Public login/Google/register failures do not expire a
separate valid session. Callbacks retained by an older screen cannot sign out,
refresh the profile of, or replace a newer login. Profile updates also check user
identity before persisting. Native SecureStore mutations run in sequence; queued
old logout/profile writes cannot overwrite newer credentials. Storage errors do
not poison subsequent queued operations. Native logout-storage failure offers a
retry instead of silently declaring persisted credentials cleared.

Customer web reacts to token changes from another tab and validates the new
identity before showing private screens. Its request guard refuses dispatch when
stored/current identities disagree; outgoing requests bind the validated token
rather than silently attaching another tab's newly written token to an old form.
The two standalone portals reject responses captured under an obsolete session
token and preserve credentials on a startup connection outage. Their sessionStorage
remains tab-local; customer web's existing localStorage token is shared by tabs.

Shared guard/queue source is in `scripts/ui/session.js`; copies are runtime-local
in the React/native repositories. The native provider source template is
`scripts/ui/AuthContext.native.js`; replace its USER_KEY placeholder with the
appropriate customer/cleaner key when synchronizing. The UI catalog sync script
does not overwrite authentication providers. No token or profile is printed by
these guards, and no server/API/schema change is introduced by this batch.

React tests cover corrupt cache, offline retry, obsolete restore/401 responses,
cross-tab identity changes, old callbacks and private profile ownership. Native
logic tests cover operation invalidation, request-token matching and serialized
writes including storage failure. Portal tests exercise stale responses with the
real API adapter. Browser tests use real test accounts plus controlled network
failures/late 401s; they cover retry, choosing another account, invalid tokens,
cross-tab identity and display-preference persistence. Android export verifies
bundle compatibility, not physical-device behavior.

Run `node --test backend/scripts/validation/native-session.test.cjs` from the
parent workspace for 12 integration checks against the actual two native auth
providers. This harness uses React DOM from the neighboring web checkout and
platform storage/network fakes; it does not simulate native screen rendering.
It covers offline retry, stale callbacks/401/restore, in-flight logout ordering,
failed persisted logout and bad Google sign-in versus genuine session expiry.

Native app configuration also enables automatic system appearance and installs
the SDK-compatible expo-system-ui module. Native configuration introspection is
checked independently of the JavaScript bundle. See the
[Expo appearance documentation](https://docs.expo.dev/develop/user-interface/color-themes/).
