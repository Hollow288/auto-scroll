[CmdletBinding()]
param(
    [switch]$RunTests,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = $PSScriptRoot
$version = ([IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION'))).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must have the form major.minor.patch.' }
$source = Join-Path $projectRoot 'src\AutoScrollWheel.cs'
$sourceVersion = [regex]::Match([IO.File]::ReadAllText($source), 'AssemblyVersion\("([0-9.]+)"\)').Groups[1].Value
if ($sourceVersion -ne "$version.0") { throw 'VERSION and AssemblyVersion do not match.' }

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'The .NET Framework C# compiler was not found. Build this project on Windows.' }

$artifacts = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$exe = Join-Path $artifacts 'AutoScrollWheel.exe'
$commonArguments = @('/nologo', '/platform:anycpu', '/optimize+', '/codepage:65001', '/utf8output', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll')
$manifest = Join-Path $projectRoot 'src\app.manifest'
& $compiler @commonArguments '/target:winexe' "/win32manifest:$manifest" "/out:$exe" $source
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Host "Built AutoScrollWheel.exe v$version"

if ($RunTests) {
    $testExe = Join-Path $artifacts 'BackgroundScrollTests.exe'
    $testSource = Join-Path $projectRoot 'tests\BackgroundScrollTests.cs'
    & $compiler @commonArguments '/target:exe' '/main:BackgroundScrollTests' "/out:$testExe" $source $testSource
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Background scrolling regression checks failed.' }
}

if ($Package) {
    $usage = Join-Path $artifacts 'USAGE.txt'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\USAGE.txt') -Destination $usage -Force
    $archive = Join-Path $artifacts "AutoScrollWheel-v$version-win.zip"
    Compress-Archive -LiteralPath @($exe, $usage) -DestinationPath $archive -Force
    $checksums = foreach ($file in @($exe, $archive)) {
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($file))"
    }
    [IO.File]::WriteAllLines((Join-Path $artifacts 'SHA256SUMS.txt'), [string[]]$checksums, [Text.Encoding]::ASCII)
    Write-Host "Packaged $([IO.Path]::GetFileName($archive))"
}
