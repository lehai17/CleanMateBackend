$ErrorActionPreference = 'Stop'
foreach ($port in @(13000, 15080, 15238)) {
    $probe = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
    try { $probe.Start() }
    catch { throw "Test port $port is unavailable. Stop the existing test run before starting another." }
    finally { $probe.Stop() }
}
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$root = Join-Path $repo 'CleanMate.Tests\obj\native-validation'
$site = Join-Path $root 'site'
New-Item -ItemType Directory -Path $site -Force | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$marker = 'CleanMate native test ' + [Guid]::NewGuid().ToString()
[IO.File]::WriteAllText((Join-Path $root 'host-marker.txt'), $marker, $utf8)
[IO.File]::WriteAllText((Join-Path $site 'index.html'), "<!doctype html><title>CleanMate native validation</title><p>$marker</p>", $utf8)
$fixtures = Join-Path $root 'fixtures.json'
if (Test-Path -LiteralPath $fixtures) { Remove-Item -LiteralPath $fixtures }
$cleanup = Join-Path $root 'test-results\system-host-cleanup.json'
if (Test-Path -LiteralPath $cleanup) { Remove-Item -LiteralPath $cleanup }
# Foreground: use a second terminal for fixtures/device checks. Shutdown through
# POST localhost:13000/__system-tests/shutdown so the disposable SQL DB is removed.
$previousSql = $env:CLEANMATE_TEST_SQL
$previousWebRoot = $env:CLEANMATE_E2E_WEB_ROOT
try {
    if (!$env:CLEANMATE_TEST_SQL) {
        $env:CLEANMATE_TEST_SQL = 'Server=localhost;Integrated Security=true;Encrypt=false;TrustServerCertificate=true'
    }
    $env:CLEANMATE_E2E_WEB_ROOT = $site
    & dotnet run --project (Join-Path $repo 'CleanMate.SystemTests.Host\CleanMate.SystemTests.Host.csproj') --no-launch-profile
    $hostExit = $LASTEXITCODE
}
finally {
    $env:CLEANMATE_TEST_SQL = $previousSql
    $env:CLEANMATE_E2E_WEB_ROOT = $previousWebRoot
}
exit $hostExit
