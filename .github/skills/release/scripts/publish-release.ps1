<#
.SYNOPSIS
    Випускає реліз KeySwitcher: версія → тести → інсталятор → публічна копія → тег → GitHub Release.

.DESCRIPTION
    Один вхід — один реліз. Кроки, які скрипт робить сам:
      1. перевіряє, що дерево робочого репозиторію чисте й gh авторизований;
      2. піднімає версію в Directory.Build.props (єдине місце, де вона задана);
      3. проганяє тести — тег не ставимо на зламаній збірці;
      4. збирає інсталятор локально (перевірка, що Inno компілює, і exe під рукою);
      5. комітить версію;
      6. синхронізує публічну копію репозиторію (у ній немає ні історії робочого репозиторію,
         ні нотаток) і пушить її в main;
      7. генерує текст release notes і питає згоду: поки людина не підтвердить текст, тег не
         ставиться, тож у реліз потрапляє тільки прочитаний текст (див. -NotesFile);
      8. ставить тег vX.Y.Z, у анотації якого лежить погоджений текст, і пушить його — це запускає
         workflow Release, який на раннері збирає інсталятор і створює Release із прикріпленим
         файлом і тим самим текстом;
      9. дочікується релізу й показує посилання.

    Версія з дефісом (1.2.0-beta.1) неможлива через -Set: формат три числа. Для pre-release
    скористайся тегом із суфіксом — workflow позначить такий реліз як pre-release.

.EXAMPLE
    .\publish-release.ps1 -Bump patch              # 1.0.1, повний цикл
    .\publish-release.ps1 -Bump patch -DryRun      # показати план і нічого не робити
    .\publish-release.ps1 -Set 1.1.0 -SkipTests
    .\publish-release.ps1 -Set 1.1.0               # повторити реліз 1.1.0 після відмови від тексту
#>
[CmdletBinding(DefaultParameterSetName = 'Bump')]
param(
    [Parameter(ParameterSetName = 'Bump', Mandatory)]
    [ValidateSet('major', 'minor', 'patch')]
    [string]$Bump,

    [Parameter(ParameterSetName = 'Set', Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Set,

    # Тека чистої публічної копії (та, що пушиться на GitHub). Типово — сусідня з робочим репозиторієм.
    [string]$PublicRepoPath = '',

    # Файл із текстом релізу. Типово — installer\out\release-notes-v<версія>.md (тека в .gitignore,
    # тож файл не бруднить дерево). Наявний файл використовується як є: після відмови від тексту
    # його можна доробити руками й повторити запуск.
    [string]$NotesFile = '',

    # Скласти текст заново, навіть якщо файл уже є (правки, зроблені минулого разу, буде втрачено).
    [switch]$RegenerateNotes,

    [switch]$SkipTests,

    # Не чекати на GitHub Release (workflow усе одно його створить).
    [switch]$NoWait,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$script:repoRoot = ''

# Типове кодування консолі Windows — не UTF-8, і без цього рядка український текст релізу на екрані
# перетворюється на знаки питання (разом із ним — і текст, який ми читаємо з gh та git).
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

function Write-Step([string]$Text) { Write-Host "`n=== $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "    $Text" -ForegroundColor Green }

$script:repoRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $script:repoRoot) { throw 'Не вдалося знайти корінь репозиторію (git rev-parse).' }
$script:repoRoot = $script:repoRoot.Trim()
$repoRoot = $script:repoRoot

$publicPath = if ($PublicRepoPath) { $PublicRepoPath } else { Join-Path (Split-Path $repoRoot -Parent) 'KeySwitcher-public' }
$bumpScript = Join-Path $PSScriptRoot 'bump-version.ps1'

# --- 1. оточення -------------------------------------------------------------------------------

Write-Step 'Перевірка оточення'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Немає gh (GitHub CLI) — без нього реліз не опублікується.' }
& gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw 'gh не авторизований — виконай gh auth login.' }

$dirty = (& git -C $repoRoot status --porcelain)
if ($dirty) { throw "Робоче дерево не чисте — спершу закоміть або відкинь зміни:`n$($dirty -join "`n")" }
Write-Ok 'gh авторизований, робоче дерево чисте'

if (-not (Test-Path (Join-Path $publicPath '.git'))) {
    throw "Немає публічної копії з git у $publicPath — створи її (див. .github/skills/release/SKILL.md, крок «перший раз»)."
}
$originUrl = (& git -C $publicPath remote get-url origin 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $originUrl) { throw "У публічної копії $publicPath немає origin." }
$originUrl = $originUrl.Trim()
$slug = ($originUrl -replace '^.*github\.com[:/]', '' -replace '\.git$', '')
Write-Ok "публічна копія: $publicPath → $slug"

# --- 2. версія ---------------------------------------------------------------------------------

Write-Step 'Версія'

[xml]$props = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
$current = [version]$props.Project.PropertyGroup.Version
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

$tag = "v$next"
if (& git -C $publicPath rev-parse -q --verify "refs/tags/$tag" 2>$null) {
    throw "Тег $tag уже існує в публічній копії — вибери іншу версію."
}
if (& git -C $repoRoot rev-parse -q --verify "refs/tags/$tag" 2>$null) {
    throw "Локальний тег $tag уже є в робочому репозиторії (він лишається там після кожного релізу — як якір для тексту наступного). Схоже, версія $next уже випускалася."
}

Write-Ok "$current → $next (тег $tag)"

if ($DryRun) {
    Write-Step 'План (dry-run, нічого не змінено)'
    Write-Host "    1. $bumpScript -Set $next"
    if (-not $SkipTests) { Write-Host '    2. dotnet test KeySwitcher.slnx' }
    Write-Host '    3. installer\build-installer.ps1'
    Write-Host "    4. git commit «Версія $next» у робочому репозиторії"
    Write-Host "    5. синхронізувати $publicPath з HEAD і запушити main"
    Write-Host '    6. згенерувати текст релізу й дочекатися згоди (без згоди тег не ставиться)'
    Write-Host "    7. git tag $tag з погодженим текстом і push — далі workflow Release збирає інсталятор"
    return
}

# --- 3. версія в коді --------------------------------------------------------------------------

Write-Step 'Піднімаю версію'

& $bumpScript -Set "$next"
if ($LASTEXITCODE -ne 0) { throw 'bump-version.ps1 завершився з помилкою' }

# --- 4. тести ----------------------------------------------------------------------------------

if ($SkipTests) {
    Write-Step 'Тести пропущено (-SkipTests)'
}
else {
    Write-Step 'Тести'
    & dotnet test (Join-Path $repoRoot 'KeySwitcher.slnx') --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Тести не пройшли — реліз зупинено.' }
    Write-Ok 'тести пройшли'
}

# --- 5. інсталятор -----------------------------------------------------------------------------

Write-Step 'Інсталятор'

$wasRunning = [bool](Get-Process KeySwitcher.UI -ErrorAction SilentlyContinue)
if ($wasRunning) {
    Stop-Process -Name KeySwitcher.UI -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

& (Join-Path $repoRoot 'installer\build-installer.ps1')
if ($LASTEXITCODE -ne 0) { throw 'build-installer.ps1 завершився з помилкою' }

$setup = Get-ChildItem (Join-Path $repoRoot 'installer\out\KeySwitcherSetup-*.exe') | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $setup) { throw 'Інсталятор не знайдено в installer\out' }
if ($setup.Name -notlike "*$next*") { throw "Інсталятор $($setup.Name) не містить версію $next — перевір Directory.Build.props." }
Write-Ok "$($setup.Name) ($([math]::Round($setup.Length / 1MB, 1)) МБ)"

# Застосунок закривався лише на час збірки — повертаємо його відразу, а не після релізу: далі його
# ніщо не чіпає, а якщо реліз доведеться відкласти, тулза не лишиться вимкненою на цілу сесію.
if ($wasRunning) {
    Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\KeySwitcher\KeySwitcher.UI.exe') -ErrorAction SilentlyContinue
}

# --- 6. коміт версії ---------------------------------------------------------------------------

Write-Step 'Коміт версії'

& git -C $repoRoot add Directory.Build.props
# Порожнього коміту не робимо: після відмови від тексту реліз повторюють тією ж версією (-Set), а тоді
# bump-version.ps1 уже нічого не міняє — і git commit на незміненому файлі просто впав би.
if ((& git -C $repoRoot diff --cached --name-only)) {
    & git -C $repoRoot commit -q -m "Версія $next"
    if ($LASTEXITCODE -ne 0) { throw 'git commit не вдався' }
    Write-Ok "робочий репозиторій: Версія $next"
}
else {
    Write-Ok "версію $next уже закомічено раніше (повторний запуск тієї ж версії)"
}

# --- 7. публічна копія -------------------------------------------------------------------------

Write-Step 'Публічна копія'

$archive = Join-Path $env:TEMP 'keyswitcher-release.tar'
Remove-Item $archive -Force -ErrorAction SilentlyContinue
& git -C $repoRoot archive HEAD -o $archive
if ($LASTEXITCODE -ne 0) { throw 'git archive не вдався' }

Get-ChildItem $publicPath -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
& tar -xf $archive -C $publicPath
Remove-Item $archive -Force

# Автор публічних комітів — нейтральний, щоб особиста пошта не їхала на GitHub.
& git -c user.name='KeySwitcher' -c user.email='KeySwitcher@users.noreply.github.com' `
    -C $publicPath commit -q -m "KeySwitcher $next"
if ($LASTEXITCODE -ne 0) { throw 'git commit у публічній копії не вдався' }

& git -C $publicPath push -q origin main
if ($LASTEXITCODE -ne 0) { throw 'git push main не вдався' }
Write-Ok "main оновлено ($((Get-ChildItem $publicPath -Recurse -File | Where-Object { $_.FullName -notmatch '\\\.git\\' } | Measure-Object).Count) файлів)"

# --- 8. release notes: генерація і згода --------------------------------------------------------

Write-Step 'Release notes'

$notesPath = if ($NotesFile) { $NotesFile } else { Join-Path $repoRoot "installer\out\release-notes-$tag.md" }
# git з -C міняє теку, тож відносний шлях до файлу в його аргументах поїхав би не туди.
$notesPath = [System.IO.Path]::GetFullPath($notesPath)
$notesDir = Split-Path $notesPath -Parent
if ($notesDir -and -not (Test-Path $notesDir)) { New-Item -ItemType Directory -Path $notesDir -Force | Out-Null }

if ((Test-Path $notesPath) -and -not $RegenerateNotes) {
    Write-Ok "беру текст, збережений раніше: $notesPath (-RegenerateNotes — скласти заново)"
}
else {
    # Джерело тексту — коміти **робочого** репозиторію від попереднього релізу: саме вони описують
    # зміни людською мовою. Публічна копія для цього не годиться — там кожен реліз це один коміт
    # «KeySwitcher X.Y.Z». Генератор GitHub теж не помічник: для тега, якого ще не існує, він
    # віддає самий лише рядок Full Changelog (перевірено), а порядок «спершу тег, потім текст»
    # суперечив би самій ідеї погодження.
    $prevTag = & git -C $repoRoot tag --list 'v*' --sort=-v:refname | Select-Object -First 1
    $range = if ($prevTag) { "$prevTag..HEAD" } else { 'HEAD' }

    $lines = @(& git -C $repoRoot log --no-merges --reverse --pretty='- %s' $range)
    # Коміт самої версії в списку змін — шум: користувачеві він нічого не каже.
    $lines = @($lines | Where-Object { $_ -notmatch '^- Версія \d+\.\d+\.\d+$' })

    $compareUrl = if ($prevTag) { "https://github.com/$slug/compare/$prevTag...$tag" }
    else { "https://github.com/$slug/commits/$tag" }

    $body = (@('## Що змінилося', '') + $lines + @('', "**Повний список змін**: $compareUrl")) -join "`n"
    Set-Content -Path $notesPath -Value $body -Encoding utf8

    if ($prevTag) { Write-Ok "текст згенеровано від $prevTag — $($lines.Count) пунктів" }
    else { Write-Ok "текст згенеровано (перший реліз — уся історія): $($lines.Count) пунктів" }
}

# Тег ставиться тільки з текстом, який прочитала людина: без згоди реліз не публікується.
#   y — публікувати показаний текст;
#   e — правити у редакторі (файл лишається на диску, тож після відмови правки не пропадають);
#   n або Enter — не публікувати нічого.
$approved = $false
while (-not $approved) {
    Write-Host ''
    Write-Host ('-' * 78) -ForegroundColor DarkGray
    Get-Content $notesPath -Encoding utf8 | Write-Host
    Write-Host ('-' * 78) -ForegroundColor DarkGray
    Write-Host "    файл: $notesPath"

    $answer = Read-Host 'Публікувати цей текст? [y] так / [e] правити / [n] ні'

    switch -Regex ($answer.Trim().ToLowerInvariant()) {
        '^(y|yes|так|д)$' { $approved = $true }
        '^(e|edit|ред)$' {
            $editor = if ($env:VISUAL) { $env:VISUAL } elseif ($env:EDITOR) { $env:EDITOR } else { 'notepad.exe' }
            & $editor $notesPath
        }
        default {
            Write-Host ''
            Write-Warning "Реліз не опубліковано: тег $tag не поставлено, Release не створено."
            Write-Host "    Текст лишився у: $notesPath"
            Write-Host "    Версія $next уже закомічена, main у публічній копії оновлено — це код, а не реліз."
            Write-Host "    Доробити текст і повторити:  .\publish-release.ps1 -Set $next"
            Write-Host "    Скласти текст заново:        .\publish-release.ps1 -Set $next -RegenerateNotes"
            Write-Host "    Відкотити коміт версії:      git -C `"$repoRoot`" reset --soft HEAD~1"
            exit 1
        }
    }
}

# --- 9. тег ------------------------------------------------------------------------------------

Write-Step "Тег $tag"

# Тег анотований, і його анотація — це той самий погоджений текст: workflow кладе в Release саме її
# (gh release create --notes-file), тож опубліковане слово в слово збігається з погодженим.
& git -C $publicPath tag -a -F $notesPath $tag
if ($LASTEXITCODE -ne 0) { throw 'git tag не вдався' }

& git -C $publicPath push -q origin $tag
if ($LASTEXITCODE -ne 0) { throw 'git push тега не вдався' }
Write-Ok "тег $tag запушено — workflow Release почав збірку"

# Той самий тег локально — це якір для тексту наступного релізу (див. крок 8). У робочий репозиторій
# він нікуди не їде: у нього немає remote, тому локальний тег бачить тільки ця машина.
& git -C $repoRoot tag $tag
if ($LASTEXITCODE -ne 0) { Write-Warning "Локальний тег $tag не створено — текст наступного релізу буде з усієї історії." }

if ($NoWait) { Write-Host "`nРеліз створиться за кілька хвилин: https://github.com/$slug/releases/tag/$tag"; return }

# --- 10. чекаємо на реліз ----------------------------------------------------------------------

Write-Step 'Чекаю на GitHub Release (до 10 хвилин)'

$releaseUrl = $null
for ($i = 1; $i -le 40; $i++) {
    Start-Sleep -Seconds 15
    $json = (& gh release view $tag --repo $slug --json url,assets 2>$null)
    if ($LASTEXITCODE -eq 0 -and $json) {
        $release = $json | ConvertFrom-Json
        if ($release.assets.Count -gt 0) {
            $releaseUrl = $release.url
            break
        }
    }
    Write-Host "    …$($i * 15) с" -NoNewline
    Write-Host "`r" -NoNewline
}

if (-not $releaseUrl) {
    Write-Warning "Реліз ще не готовий. Подивись: gh run list --repo $slug --workflow release.yml"
    return
}

Write-Step 'Готово'
Write-Host "    Реліз:      $releaseUrl"
Write-Host "    Інсталятор: $($setup.Name)"
Write-Host "    Тег:        $tag"
Write-Host "    Локально:   $($setup.FullName)"
