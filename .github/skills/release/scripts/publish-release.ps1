#Requires -Version 7.0
# PS 5.1 не годиться: там Set-Content -Encoding utf8 пише BOM, і той поїхав би в анотацію тега,
# а звідти — у текст релізу.

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
    .\publish-release.ps1 -Bump patch -PrepareNotes  # скласти й показати ченджлог, нічого не змінюючи
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

    # Скласти й показати текст ченджлогу — і на цьому спинитися: ні версії, ні комітів, ні тега.
    # Погодження цим НЕ дається: його дає тільки людина відповіддю на запит у самому релізі.
    [switch]$PrepareNotes,

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
# Усе, що пишемо у файли, зводимо до CRLF: текст склеюється з різних джерел (git, наш `n, файл
# погодженого тексту), і без цього в CHANGELOG.md опинилися б змішані переноси.
function ToCrLf([string]$Text) { ($Text -replace "`r`n", "`n") -replace "`n", "`r`n" }

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
    Write-Host "    1. взяти погоджений текст із installer\out\release-notes-$tag.md (без нього — стоп)"
    Write-Host "    2. $bumpScript -Set $next"
    Write-Host "    3. додати розділ $tag угору CHANGELOG.md"
    if (-not $SkipTests) { Write-Host '    4. dotnet test KeySwitcher.slnx' }
    Write-Host '    5. installer\build-installer.ps1'
    Write-Host "    6. git commit «Версія $next» (Directory.Build.props + CHANGELOG.md)"
    Write-Host "    7. синхронізувати $publicPath з HEAD і запушити main"
    Write-Host "    8. git tag $tag з погодженим текстом і push — далі workflow Release збирає інсталятор"
    # Саме exit 0, а не return: остання нативна команда перед цим — git rev-parse --verify на тезі,
    # якого ще немає. Вона законно виходить із кодом 1, і PowerShell віддав би цю одиницю як код
    # завершення процесу — успішний dry-run виглядав би як провал.
    exit 0
}

# Чернетка ченджлогу зі списку комітів — саме чернетка: нічого не пише й нікуди не їде. Джерело —
# коміти **робочого** репозиторію від попереднього релізу: саме вони описують зміни людською мовою.
# Публічна копія не годиться (там кожен реліз — один коміт «KeySwitcher X.Y.Z»), а генератор GitHub
# для ще неіснуючого тега віддає самий рядок Full Changelog (перевірено).
function New-DraftNotes {
    $prevTag = & git -C $repoRoot tag --list 'v*' --sort=-v:refname | Select-Object -First 1
    $range = if ($prevTag) { "$prevTag..HEAD" } else { 'HEAD' }

    $lines = @(& git -C $repoRoot log --no-merges --reverse --pretty='- %s' $range)
    # Коміт самої версії в списку змін — шум: користувачеві він нічого не каже.
    $lines = @($lines | Where-Object { $_ -notmatch '^- Версія \d+\.\d+\.\d+$' })

    $compareUrl = if ($prevTag) { "https://github.com/$slug/compare/$prevTag...$tag" }
    else { "https://github.com/$slug/commits/$tag" }

    return (@('## Що змінилося', '') + $lines + @('', "**Повний список змін**: $compareUrl")) -join "`n"
}

$notesPath = if ($NotesFile) { $NotesFile } else { Join-Path $repoRoot "installer\out\release-notes-$tag.md" }
# git з -C міняє теку, тож відносний шлях до файлу в його аргументах поїхав би не туди.
$notesPath = [System.IO.Path]::GetFullPath($notesPath)

# Показати чернетку й спинитися. Не змінюється нічого — ні версія, ні коміти, ні теги, ні файли.
if ($PrepareNotes) {
    Write-Step 'Чернетка ченджлогу (нічого не змінено)'
    Write-Host ''
    Write-Host ('-' * 78) -ForegroundColor DarkGray
    New-DraftNotes | Write-Host
    Write-Host ('-' * 78) -ForegroundColor DarkGray
    Write-Host ''
    Write-Host '    Це чернетка — у реліз вона сама собою не піде.'
    Write-Host '    Реліз бере текст лише звідси:'
    Write-Host "      $notesPath"
    Write-Host '    Покласти туди текст може й агент, але ТІЛЬКИ після того, як людина його погодила.'
    Write-Host "    Далі:  .\publish-release.ps1 -Set $next"
    exit 0
}

# --- 3. погоджений текст ченджлогу --------------------------------------------------------------

Write-Step 'Ченджлог'

# Реліз їде ТІЛЬКИ з погодженим текстом, і носій погодження — цей файл. Немає файлу — значить текст
# ніхто не читав, і публікувати нічого. Автогенерації тут навмисно немає: інакше скрипт сам вигадав
# би текст і сам же його опублікував, а сенс погодження в тому, що його дає людина.
if (-not (Test-Path $notesPath)) {
    throw @"
Немає погодженого тексту ченджлогу: $notesPath

Спершу склади чернетку й погодь її з людиною:
    .\publish-release.ps1 -Set $next -PrepareNotes
потім поклади погоджений текст у цей файл і повтори запуск.
"@
}

$notesBody = (Get-Content $notesPath -Raw -Encoding utf8).Trim()
if (-not $notesBody) { throw "Файл тексту порожній: $notesPath" }

Write-Host ''
Write-Host ('-' * 78) -ForegroundColor DarkGray
Write-Host $notesBody
Write-Host ('-' * 78) -ForegroundColor DarkGray
Write-Ok "текст узято з $notesPath"

# --- 4. версія в коді --------------------------------------------------------------------------

Write-Step 'Піднімаю версію'

# Без перевірки $LASTEXITCODE: його виставляють лише нативні exe, а виклик .ps1 його не чіпає — тут
# лишався б код останньої команди, що відпрацювала всередині скрипта, тобто перевірка ні про що.
# Помилки й так долітають сюди: bump-version.ps1 кидає throw, а $ErrorActionPreference = 'Stop' його
# не глушить.
& $bumpScript -Set "$next"

# --- 5. CHANGELOG.md ----------------------------------------------------------------------------

Write-Step 'CHANGELOG.md'

# Накопичувальна історія версій у репозиторії, найсвіжіший розділ зверху. Той самий погоджений текст
# іде і сюди, і в анотацію тега — тож опис у файлі та в GitHub Release збігається слово в слово.
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$section = "## $tag — $(Get-Date -Format 'yyyy-MM-dd')`n`n$notesBody`n"

if (Test-Path $changelogPath) {
    $existing = Get-Content $changelogPath -Raw -Encoding utf8
    if ($existing -match ('(?m)^## ' + [regex]::Escape($tag) + '\b')) {
        Write-Ok "$tag уже є в CHANGELOG.md — пропускаю (повторний запуск тієї ж версії)"
    }
    else {
        # Новий розділ — перед першим наявним «## ». Якщо розділів ще немає, у кінець.
        $anchor = [regex]::Match($existing, '(?m)^## ')
        $body = if ($anchor.Success) {
            $existing.Substring(0, $anchor.Index) + $section + "`n" + $existing.Substring($anchor.Index)
        }
        else { $existing.TrimEnd("`r", "`n") + "`n`n" + $section }

        Set-Content -Path $changelogPath -Value (ToCrLf $body) -Encoding utf8 -NoNewline
        Write-Ok "розділ $tag додано вгорі"
    }
}
else {
    $header = "# Changelog`n`nІсторія версій KeySwitcher. Складається з комітів при релізі й погоджується перед публікацією.`n`n"
    Set-Content -Path $changelogPath -Value (ToCrLf ($header + $section)) -Encoding utf8 -NoNewline
    Write-Ok "CHANGELOG.md створено, розділ $tag"
}

# --- 6. тести ----------------------------------------------------------------------------------

if ($SkipTests) {
    Write-Step 'Тести пропущено (-SkipTests)'
}
else {
    Write-Step 'Тести'
    & dotnet test (Join-Path $repoRoot 'KeySwitcher.slnx') --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Тести не пройшли — реліз зупинено.' }
    Write-Ok 'тести пройшли'
}

# --- 7. інсталятор -----------------------------------------------------------------------------

Write-Step 'Інсталятор'

# Стару версію треба спинити, інакше вона тримає власні файли під час publish.
if (Get-Process KeySwitcher.UI -ErrorAction SilentlyContinue) {
    Stop-Process -Name KeySwitcher.UI -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# $LASTEXITCODE тут не перевіряємо — див. коментар про .ps1 вище. build-installer.ps1 сам звіряє коди
# після dotnet publish і ISCC, а на помилці кидає throw.
& (Join-Path $repoRoot 'installer\build-installer.ps1')

$setup = Get-ChildItem (Join-Path $repoRoot 'installer\out\KeySwitcherSetup-*.exe') | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $setup) { throw 'Інсталятор не знайдено в installer\out' }
if ($setup.Name -notlike "*$next*") { throw "Інсталятор $($setup.Name) не містить версію $next — перевір Directory.Build.props." }
Write-Ok "$($setup.Name) ($([math]::Round($setup.Length / 1MB, 1)) МБ)"

# Назад застосунок НЕ запускаємо. Одразу після релізу ним ставлять свіжий інсталятор, а працююча
# тулза тримає власні файли — і встановлення падає на «DeleteFile збій; код 5» (спіймано на 1.0.2).
# Інсталятор тепер знімає процес сам (installer/KeySwitcher.iss, StopRunningApp), але залишати
# запущеною стару версію, яку через хвилину перезапишуть, усе одно немає сенсу: нову запустить
# галочка на останній сторінці майстра.

# --- 8. коміт версії ---------------------------------------------------------------------------

Write-Step 'Коміт версії'

& git -C $repoRoot add Directory.Build.props CHANGELOG.md
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

# --- 9. публічна копія -------------------------------------------------------------------------

Write-Step 'Публічна копія'

# Формат zip, а розпаковує Expand-Archive: зовнішній tar брати не можна. Якщо в PATH стоїть Git for
# Windows, `tar` — це GNU tar, і шлях `C:\...` він читає як «хост C, тека ...» → `Cannot connect to C:`
# і порожня тека. Так реліз 1.0.4 закомітив видалення всього репозиторію.
$archive = Join-Path $env:TEMP 'keyswitcher-release.zip'
Remove-Item $archive -Force -ErrorAction SilentlyContinue
& git -C $repoRoot archive --format=zip HEAD -o $archive
if ($LASTEXITCODE -ne 0) { throw 'git archive не вдався' }

Get-ChildItem $publicPath -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
Expand-Archive -LiteralPath $archive -DestinationPath $publicPath -Force
Remove-Item $archive -Force

# Порожнє дерево — це не «нова версія без файлів», це збій розпакування. Краще впасти тут, ніж
# запушити коміт, який зносить репозиторій.
if (-not (Get-ChildItem $publicPath -Force | Where-Object { $_.Name -ne '.git' })) {
    throw 'публічна копія порожня після розпакування — синхронізація зупинена'
}

# add -A обов'язковий: файли не переписуються на місці, а зносяться й розпаковуються заново, тож у
# коміт мають потрапити і зміни, і **видалення** того, чого в новій версії немає.
& git -C $publicPath add -A
if ($LASTEXITCODE -ne 0) { throw 'git add у публічній копії не вдався' }

# Автор публічних комітів — нейтральний, щоб особиста пошта не їхала на GitHub.
# Порожнього коміту не робимо: коли реліз повторюють тією ж версією, у публічній копії вже все, що є в
# HEAD, і git commit упав би на «nothing to commit».
if ((& git -C $publicPath diff --cached --name-only)) {
    & git -c user.name='KeySwitcher' -c user.email='KeySwitcher@users.noreply.github.com' `
        -C $publicPath commit -q -m "KeySwitcher $next"
    if ($LASTEXITCODE -ne 0) { throw 'git commit у публічній копії не вдався' }
}
else {
    Write-Ok 'публічна копія вже збігається з HEAD — коміт не потрібен'
}

& git -C $publicPath push -q origin main
if ($LASTEXITCODE -ne 0) { throw 'git push main не вдався' }
Write-Ok "main оновлено ($((Get-ChildItem $publicPath -Recurse -File | Where-Object { $_.FullName -notmatch '\\\.git\\' } | Measure-Object).Count) файлів)"

# --- 10. тег ------------------------------------------------------------------------------------

Write-Step "Тег $tag"

# Тег анотований, і його анотація — це той самий погоджений текст: workflow кладе в Release саме її
# (gh release create --notes-file), тож опубліковане слово в слово збігається з погодженим.
#
# --cleanup=verbatim обов'язковий: без нього git чистить повідомлення як коміт і вирізає рядки, що
# починаються з '#', тобто всі markdown-заголовки. Перевірено живцем на релізі 1.0.1 — у реліз поїхав
# текст без «## Що нового» і «## Під капотом», хоч у файлі вони були.
& git -C $publicPath tag -a --cleanup=verbatim -F $notesPath $tag
if ($LASTEXITCODE -ne 0) { throw 'git tag не вдався' }

& git -C $publicPath push -q origin $tag
if ($LASTEXITCODE -ne 0) { throw 'git push тега не вдався' }
Write-Ok "тег $tag запушено — workflow Release почав збірку"

# Той самий тег локально — це якір для тексту наступного релізу (див. крок 8). У робочий репозиторій
# він нікуди не їде: у нього немає remote, тому локальний тег бачить тільки ця машина.
& git -C $repoRoot tag $tag
if ($LASTEXITCODE -ne 0) { Write-Warning "Локальний тег $tag не створено — текст наступного релізу буде з усієї історії." }

if ($NoWait) { Write-Host "`nРеліз створиться за кілька хвилин: https://github.com/$slug/releases/tag/$tag"; return }

# --- 11. чекаємо на реліз ----------------------------------------------------------------------

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
