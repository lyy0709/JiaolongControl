param([string]$DotNet = 'dotnet')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$releaseRepo = Split-Path -Parent $PSScriptRoot
[xml]$projectXml = Get-Content -LiteralPath (Join-Path $releaseRepo 'JiaoLongControl/JiaoLongControl.csproj') -Raw
$releaseVersion = [string]$projectXml.Project.PropertyGroup.AppVersion
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+-safe\.\d+$') { throw 'Unexpected experimental version.' }
$packageName = "JiaoLongControl-$releaseVersion-win-x64"
$releaseRoot = Join-Path $releaseRepo "bin/releases/$releaseVersion"
if (Test-Path -LiteralPath $releaseRoot) { throw "Refusing to overwrite an existing release: $releaseRoot" }
$packageDir = Join-Path $releaseRoot $packageName
New-Item -ItemType Directory -Path $packageDir | Out-Null

Push-Location $releaseRepo
try {
    & $DotNet run --project tests/SafeTuning.Tests -c Release -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Backend regression tests failed.' }
    & $DotNet publish JiaoLongControl/JiaoLongControl.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $packageDir --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Backend publish failed.' }
    Push-Location (Join-Path $releaseRepo 'JiaoLongControl/Client')
    try {
        & npm.cmd test
        if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed.' }
        & npm.cmd exec -- vue-tsc --build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend typecheck failed.' }
        & npm.cmd exec -- vite build --outDir (Join-Path $packageDir 'WebRoot')
        if ($LASTEXITCODE -ne 0) { throw 'Frontend production build failed.' }
    } finally { Pop-Location }

    Copy-Item -LiteralPath (Join-Path $releaseRepo 'Doc/RELEASE_FIRST_RUN.md') -Destination (Join-Path $packageDir 'READ-ME-FIRST.md')
    Copy-Item -LiteralPath (Join-Path $releaseRepo 'Doc/RELEASE_FIRST_RUN.md') -Destination $packageDir
    Copy-Item -LiteralPath (Join-Path $releaseRepo 'Doc/CURVE_COMPATIBILITY_4060.md') -Destination $packageDir
    Copy-Item -LiteralPath (Join-Path $releaseRepo 'Doc/SAFE_TUNING.md') -Destination $packageDir
    Copy-Item -LiteralPath (Join-Path $releaseRepo 'LICENSE.md') -Destination $packageDir
    Copy-Item -LiteralPath (Join-Path $releaseRepo 'JiaoLongControl/Drivers/PawnIO/SETUP-NOTICE.md') -Destination (Join-Path $packageDir 'Drivers/PawnIO')

    $requiredFiles = @('JiaoLongControl.exe', 'JiaoLongControl.dll', 'JiaoLongControl.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'Microsoft.Web.WebView2.Core.dll', 'WebRoot/index.html', 'Drivers/Blding/JiaoLongDriver64.dll', 'Drivers/Blding/JiaoLongDriver64.sys', 'Drivers/PawnIO/PawnIO.LICENSE.txt', 'Drivers/PawnIO/SETUP-NOTICE.md', 'READ-ME-FIRST.md', 'SAFE_TUNING.md', 'LICENSE.md')
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $packageDir $relativePath) -PathType Leaf)) { throw "Missing package file: $relativePath" }
    }
    $files = @(Get-ChildItem -LiteralPath $packageDir -Recurse -File)
    if (-not ($files | Where-Object Name -eq 'WebView2Loader.dll')) { throw 'Missing WebView2 native loader.' }
    if ($files | Where-Object { $_.Name -match '(?i)\.dmp$|config\.yaml|safe-tuning-preview|\.pdb$' -or $_.FullName -match '[\\/](node_modules|\.git|logs)[\\/]' }) { throw 'Unexpected private/development file in package.' }
    $indexHtml = Get-Content -LiteralPath (Join-Path $packageDir 'WebRoot/index.html') -Raw
    $assetRefs = [regex]::Matches($indexHtml, '(?:src|href)="(/assets/[^"?]+)"')
    if ($assetRefs.Count -lt 2) { throw 'Missing production JS/CSS references.' }
    foreach ($asset in $assetRefs) {
        if (-not (Test-Path -LiteralPath (Join-Path $packageDir ('WebRoot' + $asset.Groups[1].Value)))) { throw "Broken web asset: $asset" }
    }
    $zipPath = Join-Path $releaseRoot "$packageName.zip"
    Compress-Archive -LiteralPath $packageDir -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $packageName.zip" | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "PACKAGE=$zipPath"
    Write-Output "SHA256=$hash"
    Write-Output "FILES=$($files.Count)"
} finally { Pop-Location }
