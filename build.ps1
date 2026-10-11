$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\LumaCapture\LumaCapture.csproj'
$toolDirectory = Join-Path $projectRoot 'src\LumaCapture\Tools'
$ffmpegSource = Join-Path $toolDirectory 'ffmpeg.exe'
$artifactDirectory = Join-Path $projectRoot 'artifacts'
$publishDirectory = Join-Path $artifactDirectory ('publish-' + [guid]::NewGuid().ToString('N'))
$outputDirectory = Join-Path (Split-Path $projectRoot -Parent | Split-Path -Parent) 'outputs'

if (-not (Test-Path -LiteralPath $ffmpegSource)) { throw "FFmpeg not found: $ffmpegSource" }

New-Item -ItemType Directory -Path $toolDirectory, $artifactDirectory, $publishDirectory, $outputDirectory -Force | Out-Null

dotnet build $projectFile -c Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

$testExecutable = Join-Path $projectRoot 'src\LumaCapture\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\LumaCapture.exe'
Copy-Item -LiteralPath $ffmpegSource -Destination (Join-Path (Split-Path $testExecutable) 'ffmpeg.exe') -Force
$testResults = [Collections.Generic.List[string]]::new()
foreach ($testName in @('--engine-smoke-test', '--child-job-smoke-test', '--resolution-smoke-test', '--audio-smoke-test', '--microphone-smoke-test', '--audio-level-smoke-test', '--process-audio-smoke-test', '--recording-smoke-test', '--display-smoke-test', '--display-reconnect-smoke-test', '--application-capture-smoke-test', '--fullscreen-application-smoke-test', '--application-lifecycle-smoke-test', '--window-application-lifecycle-smoke-test', '--clip-smoke-test', '--tray-smoke-test', '--tray-icon-smoke-test', '--settings-clip-smoke-test', '--localization-smoke-test')) {
    $test = Start-Process -FilePath $testExecutable -ArgumentList $testName -PassThru
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw "Smoke test timed out: $testName" }
    if ($test.ExitCode -ne 0) { throw "Smoke test failed: $testName" }
    $testResults.Add("PASS $testName")
    Write-Host "PASS $testName"
}
$regressionDirectory = Join-Path $artifactDirectory 'regression'
$test = Start-Process -FilePath $testExecutable -ArgumentList "--recording-regression-test `"$regressionDirectory`"" -PassThru
if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'Recording regression timed out.' }
if ($test.ExitCode -ne 0) { throw 'Recording regression failed.' }
$testResults.Add('PASS --recording-regression-test')
foreach ($check in @('dashboard', 'shutdown', 'settings-save')) {
    $checkDirectory = Join-Path $artifactDirectory ($check + '-checks')
    $test = Start-Process -FilePath $testExecutable -ArgumentList "--$check-smoke-test `"$checkDirectory`"" -PassThru
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw "$check check timed out." }
    if ($test.ExitCode -ne 0) { throw "$check check failed." }
    $testResults.Add("PASS --$check-smoke-test")
}

# The capture engine is pinned in Tools; never silently bundle the machine's older FFmpeg.
dotnet publish $projectFile -c Release -r win-x64 --self-contained true -o $publishDirectory -p:Platform=x64 -p:EmbedFfmpeg=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$setupSource = Join-Path $publishDirectory 'LumaCapture.exe'
$setupOutput = Join-Path $outputDirectory 'LumaCapture-Setup.exe'
Copy-Item -LiteralPath $setupSource -Destination $setupOutput -Force
$packageTest = Start-Process -FilePath $setupOutput -ArgumentList '--package-smoke-test' -Wait -PassThru
if ($packageTest.ExitCode -ne 0) { throw 'Packaged dependency smoke test failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputDirectory 'LumaCapture-README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $outputDirectory 'LumaCapture-THIRD-PARTY-NOTICES.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'RECORDING-REVIEW.md') -Destination (Join-Path $outputDirectory 'LumaCapture-RECORDING-REVIEW.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'RELEASE-0.12.md') -Destination (Join-Path $outputDirectory 'LumaCapture-v0.12-Changes.md') -Force
Copy-Item -LiteralPath (Join-Path $artifactDirectory 'dashboard-checks\dashboard-Japanese.png') -Destination (Join-Path $outputDirectory 'LumaCapture-v0.12-RecordingUI.png') -Force
Copy-Item -LiteralPath (Join-Path $artifactDirectory 'dashboard-checks\settings-Japanese-0.png') -Destination (Join-Path $outputDirectory 'LumaCapture-v0.12-Settings.png') -Force
Copy-Item -LiteralPath (Join-Path $toolDirectory 'FFmpeg-LICENSE.txt') -Destination (Join-Path $outputDirectory 'FFmpeg-LICENSE.txt') -Force
$testResults.Add('PASS --package-smoke-test')
$testResults | Set-Content -LiteralPath (Join-Path $outputDirectory 'LumaCapture-TEST-RESULTS.txt') -Encoding utf8
Get-Content (Join-Path $regressionDirectory 'regression-results.txt') | Add-Content -LiteralPath (Join-Path $outputDirectory 'LumaCapture-TEST-RESULTS.txt') -Encoding utf8
Get-Content (Join-Path $artifactDirectory 'shutdown-checks\shutdown-result.txt') | Add-Content -LiteralPath (Join-Path $outputDirectory 'LumaCapture-TEST-RESULTS.txt') -Encoding utf8
Get-Content (Join-Path $artifactDirectory 'settings-save-checks\settings-save-result.txt') | Add-Content -LiteralPath (Join-Path $outputDirectory 'LumaCapture-TEST-RESULTS.txt') -Encoding utf8

$sourceStage = Join-Path $artifactDirectory 'source'
if (-not [IO.Path]::GetFullPath($sourceStage).StartsWith([IO.Path]::GetFullPath($artifactDirectory) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging path' }
if (Test-Path -LiteralPath $sourceStage) { Remove-Item -LiteralPath $sourceStage -Recurse -Force }
New-Item -ItemType Directory -Path $sourceStage -Force | Out-Null
Get-ChildItem -LiteralPath $projectRoot -Force | Where-Object { $_.Name -notin @('artifacts', 'ui-review') } | ForEach-Object {
    if ($_.PSIsContainer) { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $sourceStage $_.Name) -Recurse -Force }
    else { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $sourceStage $_.Name) -Force }
}
Get-ChildItem -LiteralPath $sourceStage -Directory -Recurse -Force | Where-Object { $_.Name -in @('bin', 'obj') } | Sort-Object FullName -Descending | ForEach-Object {
    if (-not $_.FullName.StartsWith($sourceStage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid source cleanup path' }
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}
$stagedFfmpeg = Join-Path $sourceStage 'src\LumaCapture\Tools\ffmpeg.exe'
if (Test-Path -LiteralPath $stagedFfmpeg) { Remove-Item -LiteralPath $stagedFfmpeg -Force }
$sourceZip = Join-Path $outputDirectory 'LumaCapture-Source.zip'
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip -Force }
Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceZip -CompressionLevel Optimal

Get-FileHash -Algorithm SHA256 -LiteralPath $setupOutput, $sourceZip | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath (Join-Path $outputDirectory 'LumaCapture-SHA256.txt') -Encoding utf8

Write-Host "Created: $setupOutput"
