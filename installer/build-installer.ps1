<#
.SYNOPSIS
    Публікує KeySwitcher і збирає інсталятор (Inno Setup 6).

.DESCRIPTION
    Два кроки, кожен можна виконати окремо:
      1. dotnet publish → installer\publish (за замовчуванням framework-dependent: 7 файлів / 12 МБ,
         інсталятор ~4 МБ — .NET Desktop Runtime 10 інсталятор довантажує й ставить сам, якщо його
         нема на машині; -SelfContained дає самодостатній варіант — 254 файли / 165 МБ, інсталятор
         ~51 МБ, зате .NET на цільовій машині не потрібен);
      2. ISCC.exe KeySwitcher.iss → installer\out\KeySwitcherSetup-<версія>.exe.

    Версія береться з Directory.Build.props (єдине місце, де вона задана) і передається в скрипт
    інсталятора: щоб номер у назві файлу й у «Програмах і засобах» не розходився зі збіркою.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -SelfContained
    .\build-installer.ps1 -SkipPublish      # тільки перепакувати наявний publish
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $PSScriptRoot 'publish'
$issPath = Join-Path $PSScriptRoot 'KeySwitcher.iss'
$outputDir = Join-Path $PSScriptRoot 'out'

function Get-ProjectVersion {
    [xml]$props = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
    $version = ($props.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
    if (-not $version) { throw 'Version is not set in Directory.Build.props' }
    return $version.Trim()
}

function Get-InnoCompiler {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    # Шукаємо будь-яку встановлену версію (6 або 7) у трьох місцях, де її ставлять інсталятор і winget.
    $roots = @("${env:ProgramFiles(x86)}", $env:ProgramFiles, "$env:LOCALAPPDATA\Programs") |
        Where-Object { $_ -and (Test-Path $_) }

    $found = foreach ($root in $roots) {
        Get-ChildItem -Path $root -Directory -Filter 'Inno Setup*' -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'ISCC.exe' }
    }

    $installed = @($found | Where-Object { Test-Path $_ } | Sort-Object)
    if ($installed.Count -gt 0) { return $installed[-1] }   # «Inno Setup 7» сортується після «Inno Setup 6»

    # Немає компілятора — це не помилка скрипта, а відсутній інструмент: підказуємо, як його поставити.
    throw @"
Не знайдено ISCC.exe (компілятор Inno Setup).
Встанови Inno Setup 6:  winget install -e --id JRSoftware.InnoSetup
або завантаж із https://jrsoftware.org/isdl.php — потім запусти цей скрипт ще раз.
"@
}

$version = Get-ProjectVersion
Write-Host "KeySwitcher $version → інсталятор ($Configuration, $Runtime)"

if (-not $SkipPublish) {
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    $publishArgs = @(
        'publish', (Join-Path $repoRoot 'KeySwitcher.UI\KeySwitcher.UI.csproj'),
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', $SelfContained.ToString().ToLowerInvariant(),
        '-o', $publishDir,
        '-p:DebugType=embedded',   # символи для стек-трейсів в error.log, але без окремих .pdb
        '--nologo'
    )

    Write-Host "dotnet $($publishArgs -join ' ')"
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершився з кодом $LASTEXITCODE" }

    $size = [math]::Round((Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host "Опубліковано: $publishDir ($size МБ)"
}

$iscc = Get-InnoCompiler
if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

Write-Host "ISCC: $iscc"
# Ознака «framework-dependent» їде в скрипт інсталятора define-ом: за нею той вирішує, чи перевіряти
# наявність .NET Desktop Runtime 10 і чи довантажувати його (див. [Code] у KeySwitcher.iss).
$isccArgs = @("/DMyAppVersion=$version")
if (-not $SelfContained) { $isccArgs += '/DNeedsDesktopRuntime' }
& $iscc @isccArgs $issPath
if ($LASTEXITCODE -ne 0) { throw "ISCC завершився з кодом $LASTEXITCODE" }

$setup = Get-ChildItem $outputDir -Filter 'KeySwitcherSetup-*.exe' |
    Sort-Object LastWriteTime | Select-Object -Last 1
Write-Host "Готово: $($setup.FullName) ($([math]::Round($setup.Length / 1MB, 1)) МБ)"
