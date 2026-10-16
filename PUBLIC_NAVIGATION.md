# Public navigation fixes

Branch: `public-navigation`. Verified 17 October 2026.

Customer footer service links now open the real service catalog; contact opens
the authenticated support area instead of placeholder phone/email details. The
after-service label matches the existing direct-cash workflow. Policy selection
is derived from a validated URL query, so footer links, reload and browser history
show the same section. The page title follows the chosen language. Long names
are truncated in the desktop header; small signed-in screens keep the logo and
navigation usable without overflow. Existing names remain available in Profile.

No policy text, backend behavior or database changes. Customer web build, all
48 React tests and all 70 Playwright cases pass (desktop/mobile, zero failures,
skips or flaky tests); generated SQL test database cleanup was verified. New
coverage exercises policy history, translated support links and signed-in header
widths 320/768/1024/1280px. Prior admin/cleaner regressions remain 46/36 passed.
