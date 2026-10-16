$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$stateFile = Join-Path $repo 'CleanMate.Tests\obj\native-validation\emulator-state.json'
if (!(Test-Path -LiteralPath $stateFile)) { throw 'No CleanMate test-emulator state exists.' }
$state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
if ($state.serial -notmatch '^emulator-\d+$' -or $state.avd -notmatch '^CleanMateTest_[0-9a-f]{10}$') { throw 'Unexpected emulator identity; refusing to stop it.' }
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
$adb = Join-Path $sdk 'platform-tools\adb.exe'
$identity = & $adb -s $state.serial emu avd name
if ($LASTEXITCODE -ne 0 -or $identity[0].Trim() -ne $state.avd) { throw 'Device does not match the disposable test AVD; no device was stopped.' }
& $adb -s $state.serial emu kill
if ($LASTEXITCODE -ne 0) { throw 'Could not stop the test emulator.' }
Write-Output "Stopped $($state.serial) ($($state.avd)). Generated test files remain under CleanMate.Tests/obj/native-validation."
