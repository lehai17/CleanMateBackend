# Dependency and responsive layout maintenance

Reviewed 17 October 2026 (Asia/Bangkok). Branch: `dependency-hardening`.

## Changes

- Customer web: replace the incompatible Tailwind 4 integration with Tailwind 3,
  which the existing Create React App 5 build supports. Name the configuration
  `tailwind.config.js` and compile the three Tailwind layers. The previous artifact
  contained unprocessed directives and did not include flex/grid utilities. Remove
  the runtime Tailwind CDN script so layout no longer relies on an external compiler.
- Add an expandable customer web navigation menu for phone/tablet widths. Keep
  guest registration reachable and close the menu on navigation or Escape.
- Replace 852 explicit web runtime dependencies with the seven packages actually
  imported by the app. Declare build/test packages separately. Node 22.19 is the
  validated local toolchain; dependency resolution remains committed in lockfiles.
- Apply compatible npm dependency fixes without `--force`. Both native apps now
  resolve `tar` 7.5.22 and `shell-quote` 1.12.0. Align React Native 0.81.5,
  Worklets 0.5.1 and Expo module patch versions with installed Expo SDK 54.
- Backend: remove obsolete `Microsoft.AspNetCore.Session` 2.3.0 from the .NET 8
  cleaner portal. Session support belongs to its existing ASP.NET shared framework.
  Update xUnit 2.5.3 to 2.9.3 and the v2 test adapter to 2.8.2, removing the old
  NETStandard.Library dependency chain. No business schema/API changes.
- Admin/cleaner web have no dependency updates; their regression checks verify
  behavior against the updated backend. Their copies of this note record scope.

Tailwind's [official CRA guide](https://v3.tailwindcss.com/docs/guides/create-react-app)
documents this integration and CRA's lack of custom PostCSS configuration support.

## Audit scope and remaining findings

Current registry reports after this batch:

| Scope | Low | Moderate | High | Critical |
| --- | ---: | ---: | ---: | ---: |
| Customer web, all dependencies | 3 | 32 | 63 | 0 |
| Customer web, `--omit=dev` | 0 | 0 | 0 | 0 |
| Customer mobile, all dependencies | 0 | 16 | 28 | 0 |
| Cleaner mobile, all dependencies | 0 | 16 | 28 | 0 |
| Admin / cleaner web | 0 | 0 | 0 | 0 |
| NuGet, five projects including transitives | 0 | 0 | 0 | 0 |

These are advisory counts, not a claim that an application is secure. npm includes
build/tooling chains in native dependency reports; findings must be assessed
individually. Unresolved CRA/Jest/glob/selector tooling and Expo dependencies need
planned migration or upstream fixes. Several audit suggestions require breaking
upgrades to Tailwind 4 or newer Expo SDKs; they were not applied automatically.
Avoid processing untrusted projects, glob patterns or archives with build tools.
Native SDK upgrades require another compatibility, build and device test cycle.

Recheck from each client with `npm audit --json`; customer runtime separately with
`npm audit --omit=dev --json`. From backend:

```powershell
dotnet list CleanMate.sln package --vulnerable --include-transitive --format json --source https://api.nuget.org/v3/index.json
```

Audit reports are local ignored artifacts, not application configuration. This
batch does not validate the installed .NET runtime, operating system, production
hosting or externally configured services. Android bundle exports are distinct
from an APK build or emulator/physical-device UI validation.

## Validation

Backend build: zero warnings/errors; 189 tests passed. Customer web: 48 React
tests and 64 Playwright cases passed, including compiled grid/flex styles without
the CDN and navigation at 320/768/1024px. Admin 46 and cleaner web 36 Playwright
cases passed: 146 browser cases total, zero failures, skips or flaky tests. All
system-test database cleanup markers were verified. Portal Node suites pass
10/14; native logic suites 12/15; actual native auth-provider harness 12.
Android exports pass at 1376/1331 modules; iOS exports at 1369/1332 modules
(customer/cleaner). These are bundles, not APKs/IPAs or device UI tests. Offline
Expo checks against installed SDK metadata report aligned dependencies; native
config introspection preserves automatic Android/iOS appearance.
