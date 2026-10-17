# Native build hardening

The customer and cleaner apps now resolve their API endpoint from the current
Expo manifest (`extra.apiUrl`). They no longer depend on Metro's cached inline
replacement of `process.env.EXPO_PUBLIC_API_URL`. Changing that variable requires
rebuilding a standalone APK, as with other embedded native configuration.

Normal release startup requires an HTTPS endpoint. Validation startup requires
an explicit local system-test endpoint on port 15238 and fails closed when its
manifest is missing or points elsewhere. Ordinary Metro development retains its
existing platform/host fallback. Six regression cases per app cover these rules.

## Android validation APK

With JDK 17, Android SDK and the isolated backend test host available:

```powershell
$env:EXPO_PUBLIC_API_URL = 'http://10.0.2.2:15238'
npm run build:android:validation
```

This builds an x86_64 debug-signed APK with the `.validation` application ID.
Its JavaScript is bundled and developer support is disabled, so it starts without
a Metro server. This is a disposable emulator artifact, not a signed store release.
The helper verifies the APK's application ID, extracted embedded API manifest and
JavaScript bundle before reporting success. It does not select or install on a
device. `npm run android:validation` retains the interactive Metro workflow.

The opt-in config plugin removes its own generated changes when switching back
to ordinary development/production configuration. Check both variants and repeat
prebuild idempotence with:

```powershell
npm run check:native-config
npm run check:native-prebuild
```

The first command checks production, interactive validation and bundled validation
for both platforms. The second regenerates Android four times to verify mode
switching and repeated generation; run it before a build, not concurrently with one.

## Build storage

Android helpers use ignored `.expo/native-build/gradle` and
`.expo/native-build/tmp` directories in each app, avoiding large Gradle downloads
and temporary extraction on a small system drive. An explicitly supplied
`GRADLE_USER_HOME` takes precedence and can share one cache across sequential app
builds. These are child-process settings; global environment settings and existing
user caches are not changed. Native output, caches, APK inspections, emulator data
and test-account fixtures are ignored by Git.

The shared native UI adapter also avoids accessing React Native's deprecated
SafeAreaView export when it uses the safe-area-context replacement.

## Scope of verification

Customer/cleaner Node suites pass 18/21 tests. Native manifest checks pass all
three modes; Android prebuild switching/idempotence passes for both apps.
Android/iOS bundle exports pass for both apps (customer 1377/1370 modules,
cleaner 1332/1333). Customer bundled validation APK compilation and embedded
configuration verification pass for both apps (customer 7m50s, cleaner 15m39s;
336 Gradle tasks each). SDK deprecation/template warnings remain; successful
APK compilation is not a zero-warning build claim. On the disposable Android 35 emulator, the
customer APK passed real isolated-API login, EN/dark switching while retaining
typed email, private-name preservation, SecureStore cold-start restoration,
VI/light switching and preferences/logout surviving another restart. Accessibility
automation retried transient null-root dumps during Android activity transitions;
each assertion used a freshly generated hierarchy, not an old XML dump.
Cleaner APK passed EN/dark switching with retained email, approved-cleaner login,
accept/start/complete, the cash declaration confirmation and SecureStore cold
restore. Read-back from the isolated API verified Completed/Paid/cash, the original
140000 price and no customer confirmation. Both customer scripts were rerun from
their tracked locations successfully. These checks do not certify iOS installation,
physical devices, release signing or a deployed HTTPS backend.

## Repeat the native smoke checks (Windows)

Build both validation APKs first. From the backend repo, use a separate terminal
to run `scripts/validation/Start-NativeTestHost.ps1`. This foreground host creates
its own SQL database, local web-root marker and fake mail service; it refuses to
start when the shared system-test ports are occupied. Do not run Playwright at
the same time. Once it prints `System tests ready`, in a second terminal:

```powershell
node scripts/validation/seed-native-fixtures.cjs
./scripts/validation/Start-TestEmulator.ps1
```

Wait for Android boot. Install only on the recorded disposable AVD using the
identity-checking helper (these commands refuse a different emulator identity):

```powershell
node -e "const u=require('./scripts/validation/native-ui.cjs');u.adb('install','-r',require('path').resolve('../mobile/android/app/build/outputs/apk/debug/app-debug.apk'))"
node scripts/validation/native-customer-smoke.cjs
node scripts/validation/native-customer-restore.cjs
node -e "const u=require('./scripts/validation/native-ui.cjs');u.adb('install','-r',require('path').resolve('../cleaner-mobile/android/app/build/outputs/apk/debug/app-debug.apk'))"
node scripts/validation/native-cleaner-smoke.cjs
```

The fixture seeder and smoke scripts verify the test-host marker; the device helper
checks the recorded `CleanMateTest_<id>` AVD before using adb. Smoke scripts clear
only their isolated `.validation` package. Cleaner smoke changes its disposable
booking through completion/cash declaration; use a fresh test-host run for another
full lifecycle check. Customer restore runs immediately after customer smoke.
Screenshots, fresh accessibility XML and result JSON are stored under ignored
`CleanMate.Tests/obj/native-validation`, together with synthetic fixture accounts.

Always finish through the test host's shutdown endpoint and verify cleanup:

```powershell
Invoke-WebRequest -UseBasicParsing -Method Post http://127.0.0.1:13000/__system-tests/shutdown
# Wait for the host terminal to exit, then inspect the generated cleanup report.
Get-Content CleanMate.Tests/obj/native-validation/test-results/system-host-cleanup.json
./scripts/validation/Stop-TestEmulator.ps1
```

The report must show `cleaned: true` for a `CleanMateSystemTests_` database.

The tracked host launcher and fixture seeder were also exercised in a separate
fresh run. Both disposable databases were deleted successfully. Occupied-port
and duplicate-fixture guards refused a concurrent run before mutation; both host
and emulator launchers preserve their calling terminal's environment. Test hosts,
Metro servers and disposable emulators were stopped after validation.
