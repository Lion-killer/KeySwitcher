<#
.SYNOPSIS
    Піднімає версію застосунку в Directory.Build.props — єдиному місці, де вона задана.

.DESCRIPTION
    Версія з Directory.Build.props їде і в exe, і в інсталятор (KeySwitcher.iss отримує її через
    /DMyAppVersion), і в назву GitHub Release. Тому змінювати її треба рівно тут — руками або цим
    скриптом, а не в чотирьох місцях.

    Формат — три числа без суфіксів (1.2.3): стільки розуміє і Windows (Version полів exe), і Inno
    Setup. Для pre-release використовуй тег із суфіксом (v1.2.0-beta.1) — workflow Release позначить
    такий реліз як pre-release.

.EXAMPLE
    .\bump-version.ps1 -Bump patch        # 1.0.0 → 1.0.1
    .\bump-version.ps1 -Bump minor -Commit
    .\bump-version.ps1 -Set 2.0.0
    .\bump-version.ps1 -Bump patch -DryRun
#>
[CmdletBinding(DefaultParameterSetName = 'Bump')]
param(
    [Parameter(ParameterSetName = 'Bump', Mandatory)]
    [ValidateSet('major', 'minor', 'patch')]
    [string]$Bump,

    [Parameter(ParameterSetName = 'Set', Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Set,

    # Комітити зміну (окремим комітом, щоб у історії було видно, коли версія змінилась).
    [switch]$Commit,

    # Показати, що буде зроблено, і нічого не писати.
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $repoRoot) { throw 'Не вдалося знайти корінь репозиторію (git rev-parse).' }
$repoRoot = $repoRoot.Trim()

$propsPath = Join-Path $repoRoot 'Directory.Build.props'
if (-not (Test-Path $propsPath)) { throw "Немає $propsPath" }

$text = [System.IO.File]::ReadAllText($propsPath)
$match = [regex]::Match($text, '<Version>([^<]+)</Version>')
if (-not $match.Success) { throw 'У Directory.Build.props немає <Version>…</Version>' }

$current = [version]$match.Groups[1].Value.Trim()

$next = if ($PSCmdlet.ParameterSetName -eq 'Set') {
    [version]$Set
}
else {
    switch ($Bump) {
        'major' { [version]"$($current.Major + 1).0.0" }
        'minor' { [version]"$($current.Major).$($current.Minor + 1).0" }
        'patch' { [version]"$($current.Major).$($current.Minor).$($current.Build + 1)" }
    }
}

if ($next -eq $current) {
    Write-Host "Версія вже $current — нічого не змінюю."
    return
}

if ($DryRun) {
    Write-Host "[dry-run] Directory.Build.props: $current → $next"
    return
}

[System.IO.File]::WriteAllText($propsPath, $text.Replace($match.Value, "<Version>$next</Version>"))
Write-Host "Directory.Build.props: $current → $next"

if ($Commit) {
    & git -C $repoRoot add Directory.Build.props
    & git -C $repoRoot commit -q -m "Версія $next"
    if ($LASTEXITCODE -ne 0) { throw 'git commit не вдався' }
    Write-Host "Закомічено: Версія $next"
}
