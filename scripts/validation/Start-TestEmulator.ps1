param([int]$Port = 5580, [ValidateSet('auto-no-window', 'swiftshader_indirect', 'angle_indirect', 'host')][string]$GpuMode = 'auto-no-window')
$ErrorActionPreference = 'Stop'
if ($Port -lt 5554 -or $Port -gt 5682 -or $Port % 2 -ne 0) { throw 'Use an even emulator console port between 5554 and 5682.' }
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
$emulator = Join-Path $sdk 'emulator\emulator.exe'
$image = Join-Path $sdk 'system-images\android-35\default\x86_64'
if (!(Test-Path -LiteralPath (Join-Path $image 'system.img'))) { throw 'Install the Android 35 AOSP x86_64 emulator image first.' }
if (Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue) { throw "Port $Port is already in use; no existing emulator will be touched." }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$root = Join-Path $repo 'CleanMate.Tests\obj\native-validation'
$avds = Join-Path $root 'avds'
$name = 'CleanMateTest_' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$data = Join-Path $avds ($name + '.avd')
New-Item -ItemType Directory -Path $data -Force | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $avds ($name + '.ini')), "avd.ini.encoding=UTF-8`npath=$data`ntarget=android-35`n", $utf8)
$configuration = @"
AvdId=$name
avd.ini.encoding=UTF-8
avd.ini.displayname=CleanMate disposable validation device
abi.type=x86_64
image.sysdir.1=$image\
tag.id=default
hw.cpu.arch=x86_64
hw.cpu.ncore=4
hw.ramSize=3072
hw.lcd.width=720
hw.lcd.height=1600
hw.lcd.density=280
hw.keyboard=yes
hw.mainKeys=no
hw.gpu.enabled=yes
hw.gpu.mode=auto
hw.audioInput=no
hw.sdCard=no
disk.dataPartition.size=2048M
showDeviceFrame=no
fastboot.forceColdBoot=yes
"@
[IO.File]::WriteAllText((Join-Path $data 'config.ini'), $configuration, $utf8)
$previousAvdHome = $env:ANDROID_AVD_HOME
try {
    $env:ANDROID_AVD_HOME = $avds
    $process = Start-Process -FilePath $emulator -ArgumentList @('-avd', $name, '-port', "$Port", '-gpu', $GpuMode, '-no-window', '-no-audio', '-no-snapshot', '-no-boot-anim') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'emulator.log') -RedirectStandardError (Join-Path $root 'emulator-error.log')
}
finally { $env:ANDROID_AVD_HOME = $previousAvdHome }
$state = @{ pid = $process.Id; serial = "emulator-$Port"; avd = $name; dataDirectory = $data; avdRoot = $avds }
$state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'emulator-state.json') -Encoding UTF8
$state | ConvertTo-Json
