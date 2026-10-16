# Vietnamese/English and appearance preferences

All five clients expose language and appearance controls before sign-in and in
the authenticated workspace. Defaults are Vietnamese and the device appearance.
Users may choose English and force light or dark appearance. The choice is local
to the browser origin/device and survives sign-out. It is not an account setting.

Browser clients use `cleanmate_ui_preferences` in localStorage; native clients
use the same key in AsyncStorage. Only validated language/theme values are used.
Malformed data falls back to defaults. Unavailable storage keeps preferences in
memory for the current session. No password, email code or account token is
stored in this preference record.

React clients use context and render-time text adapters. Changing preferences
does not remount the application or mutate React-managed DOM. Typed values and
unfinished forms remain intact. Private content is rendered through `Raw`.
React Native adapters translate labels/placeholders/alerts and apply semantic
colors to existing styles; navigation headers and tabs consume the same context.
Native preference controls use safe-area insets and keep their selected state
available to accessibility services.

The two standalone portals translate text/accessible attributes as DOM nodes are
inserted. Source text is tracked separately from rendered text so switching back
does not translate an English result as if it were Vietnamese. Observer writes
are idempotent. `data-ui-raw` protects names, addresses, support messages and other
private content. Input values, textarea contents and business attributes are
never translated. Selects retain original option values.

The catalog contains authored interface phrases, not machine translation. Whole
phrases and explicit numeric templates are translated; unknown content remains
in its original language. Service identifiers, API enum values, VND amounts and
Vietnam business dates do not change. Configurable service names, staff replies
and other user-authored text are not automatically translated. Server messages
without a catalog entry also retain their original language. Google/native OS
dialogs follow their own SDK/OS locale.

Runtime copies live in each independent client's `src/ui/`; building a client
does not require the backend checkout. Maintainers can update the common sources
under `backend/scripts/ui/` and run `node backend/scripts/ui/install.cjs` from the
parent workspace to synchronize adapters and the catalog across all five repos.
Review and test each changed repository before publishing. Changes to the shared
logic test template should be copied to each client's tests folder too.

Validation covers preference parsing, whole-phrase lookup, unknown/prototype keys,
system theme resolution and 4.5:1 semantic text contrast. React tests cover form
preservation, private text and remount persistence. Playwright covers both browser
viewports, reload, route changes, system appearance changes, an English cash
booking with server prices and private data, staff support and pending cleaner
access. Existing role, pricing, security and lifecycle suites remain required.
Android Expo export checks bundle compatibility; physical-device UI remains a
separate release check. No live production database migration is needed.
