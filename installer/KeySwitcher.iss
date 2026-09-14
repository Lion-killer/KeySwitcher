; KeySwitcher — інсталятор на Inno Setup 6.
;
; Збирається не вручну, а скриптом: installer\build-installer.ps1 (він публікує застосунок у
; installer\publish і передає версію через /DMyAppVersion). Вручну:
;   "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.0.0 KeySwitcher.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

; Тека з опублікованим застосунком, відносно цього файлу.
#ifndef PublishDir
  #define PublishDir "publish"
#endif

; Framework-dependent збірці потрібен .NET Desktop Runtime 10 (4 МБ інсталятора замість 51 МБ);
; self-contained несе рантайм у собі. Скрипт збірки передає /DNeedsDesktopRuntime для першого варіанта
; і мовчить для другого — разом із перевіркою зникає й завантаження. Саме #ifdef, а не #if:
; значення define-а з командного рядка має тип-рядок, і арифметика з ним непевна, а наявність — однозначна.

; Посилання на CDN указується напряму, а не через aka.ms: вбудований завантажувач Inno не йде за
; 301-редиректом (aka.ms віддає 301, після чого Download падає з 12007 «ім'я не розв'язано» —
; перевірено). Версію патча можна піднімати руками: підходить будь-яка 10.0.x.
#ifndef DesktopRuntimeUrl
  #define DesktopRuntimeUrl "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.12/windowsdesktop-runtime-10.0.12-win-x64.exe"
#endif
#ifndef DesktopRuntimeHelpUrl
  #define DesktopRuntimeHelpUrl "https://dotnet.microsoft.com/download/dotnet/10.0"
#endif

[Setup]
; AppId — ідентичність застосунку для Windows; він мусить лишатися незмінним між версіями, інакше
; нова версія поставиться поруч як окрема програма.
AppId={{8B2E4C11-3F5A-4C7E-9D2A-6B1F0E7C4A93}
AppName=KeySwitcher
AppVersion={#MyAppVersion}
AppVerName=KeySwitcher {#MyAppVersion}
AppPublisher=KeySwitcher
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\KeySwitcher
DisableProgramGroupPage=yes
DefaultGroupName=KeySwitcher
UninstallDisplayName=KeySwitcher {#MyAppVersion}
UninstallDisplayIcon={app}\KeySwitcher.UI.exe
SetupIconFile=..\KeySwitcher.UI\app.ico
OutputDir=out
OutputBaseFilename=KeySwitcherSetup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Застосунок за природою «свій для кожного користувача»: налаштування в %AppData%, автозапуск у HKCU.
; Тому без UAC за замовчуванням, але адміністратор може вибрати машинне встановлення у вікні майстра.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Запущена тулза тримає свої файли відкритими: Restart Manager закриє її перед заміною файлів.
; Перезапуск бере на себе галочка «Запустити KeySwitcher» — інакше після апгрейду застосунок
; стартував би двічі й другим екземпляром показав «KeySwitcher вже запущено».
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ukrainian.AdditionalIcons=Додаткові ярлики:
english.AdditionalIcons=Additional icons:
ukrainian.DesktopIcon=Створити ярлик на робочому столі
english.DesktopIcon=Create a desktop shortcut
ukrainian.StartupGroup=Автозапуск
english.StartupGroup=Startup
ukrainian.AutoStart=Запускати KeySwitcher разом із Windows
english.AutoStart=Start KeySwitcher with Windows
ukrainian.LaunchApp=Запустити KeySwitcher
english.LaunchApp=Launch KeySwitcher
ukrainian.RemoveUserData=Видалити також налаштування, власні слова й логи?%n%nТак — програма зникне повністю, разом із %1. Ні — файли лишаться, і наступне встановлення підхопить їх.
english.RemoveUserData=Also remove your settings, personal words and logs?%n%nYes — KeySwitcher disappears completely, including %1. No — the files stay, and a later install picks them up.
ukrainian.DesktopRuntimePrompt=Для роботи KeySwitcher потрібен .NET Desktop Runtime 10, якого на цій машині немає.%n%nЗавантажити й встановити його зараз (~57 МБ)? Під час встановлення може з'явитися запит UAC.
english.DesktopRuntimePrompt=KeySwitcher needs the .NET Desktop Runtime 10, which is not installed on this machine.%n%nDownload and install it now (~57 MB)? A UAC prompt may appear.
ukrainian.DesktopRuntimeDeclined=Без .NET Desktop Runtime 10 KeySwitcher не запуститься.%n%nВстановіть його з %1 і запустіть інсталятор ще раз.
english.DesktopRuntimeDeclined=Without the .NET Desktop Runtime 10 KeySwitcher cannot start.%n%nInstall it from %1 and run the installer again.
ukrainian.DesktopRuntimePageTitle=Завантаження .NET Desktop Runtime
english.DesktopRuntimePageTitle=Downloading the .NET Desktop Runtime
ukrainian.DesktopRuntimePageText=KeySwitcher потребує .NET Desktop Runtime 10 — завантажуємо його один раз.
english.DesktopRuntimePageText=KeySwitcher needs the .NET Desktop Runtime 10 — downloading it once.
ukrainian.DesktopRuntimeDownloadFailed=Не вдалося завантажити .NET Desktop Runtime:%n%n%1%n%nЗавантажте його вручну з %2 і запустіть інсталятор ще раз.
english.DesktopRuntimeDownloadFailed=Could not download the .NET Desktop Runtime:%n%n%1%n%nDownload it manually from %2 and run the installer again.
ukrainian.DesktopRuntimeInstallFailed=Не вдалося встановити .NET Desktop Runtime (код %1).%n%nВстановіть його вручну з %2 і запустіть інсталятор ще раз. Якщо був запит UAC — потрібні права адміністратора.
english.DesktopRuntimeInstallFailed=Could not install the .NET Desktop Runtime (code %1).%n%nInstall it manually from %2 and run the installer again. If a UAC prompt appeared, administrator rights are required.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
; Автозапуск не вмикаємо мовчки: фонова тулза не має з'являтися в автозапуску без згоди.
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:StartupGroup}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Умови ліцензії їдуть разом із застосунком не для краси: PolyForm Noncommercial вимагає, щоб кожен, хто
; отримав копію, отримав і текст умов (§Notices), а MPL 1.1 (словник uk.txt) — щоб разом із файлом їхав
; текст ліцензії (§3.1).
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[InstallDelete]
; Inno при апгрейді не прибирає файли, яких немає в новій версії. А перехід між framework-dependent і
; self-contained збірками міняє склад теки радикально: 7 файлів проти 254. Без цього рядка після
; апгрейду в теці лишався б цілий рантайм .NET (165 МБ), якого вже ніхто не використовує.
; Тека — наша від початку й до кінця, користувацьких файлів у ній не буває (налаштування живуть
; в %AppData%). Файли деінсталятора Inno перепише свої.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{group}\KeySwitcher"; Filename: "{app}\KeySwitcher.UI.exe"
Name: "{group}\{cm:UninstallProgram,KeySwitcher}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\KeySwitcher"; Filename: "{app}\KeySwitcher.UI.exe"; Tasks: desktopicon

[Run]
; Автозапуск — це налаштування застосунку, а не ключ у реєстрі: App на кожному старті приводить
; Run-ключ до AppSettings.AutoStart. Тому інсталятор не править settings.json сам, а просить застосунок —
; той запише файл своїм серіалізатором і поставить Run-ключ. Заодно застосунок лишається запущеним:
; автозапуск і передбачає, що він працює.
Filename: "{app}\KeySwitcher.UI.exe"; Parameters: "--enable-autostart"; Tasks: autostart; Flags: runhidden nowait
; Якщо вище ми вже запустили застосунок, другий показував би «KeySwitcher вже запущено».
Filename: "{app}\KeySwitcher.UI.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent; Check: not WizardIsTaskSelected('autostart')

[Code]
// Дві пастки, на яких уже спіткнулися. Перша: у [Code] діє Pascal, і «;» тут не коментар, а порожній
// оператор — коментарі тільки «//». Друга: рядок, перший непробільний символ якого — «[», компілятор
// приймає за заголовок секції («Invalid section tag»), тому масиви параметрів FmtMessage починаються
// з «[» у кінці попереднього рядка, а не на початку свого.
var
  RemoveUserData: Boolean;
#ifdef NeedsDesktopRuntime
  DownloadPage: TDownloadWizardPage;
#endif

const
  // Підходить будь-яка 10.0.x: застосунок зібраний під net10.0-windows.
  DesktopRuntimeMajor = '10.0';
  DesktopRuntimeFileName = 'windowsdesktop-runtime.exe';

// Знімаємо працюючий застосунок перед тим, як чіпати його файли.
//
// CloseApplications=yes покладається на Restart Manager, а той закриває лише застосунки, які
// відповідають на запит завершення сеансу. KeySwitcher — фонова тулза: вікна верхнього рівня в неї
// немає, відповідати нікому, тож RM здається, файли лишаються зайнятими, і встановлення падає на
// «DeleteFile збій; код 5. Access is denied» (спіймано на оновленні 1.0.1 -> 1.0.2). Оскільки
// застосунок стартує разом із Windows, під час оновлення він працює майже завжди — тобто це не
// виняток, а типовий випадок.
//
// Код виходу taskkill навмисно не перевіряємо: «процес не знайдено» — теж нормальний результат.
procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/im KeySwitcher.UI.exe /f', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Дескриптори звільняються не тієї ж миті, коли процес зникає.
  Sleep(500);
end;

// Іконку в треї сюди винести не можна — перевірено дослідом, не додавай.
//
// Windows 11 кладе кожну нову іконку в переповнення, і підтриманого способу винести її на панель у
// застосунку чи інсталятора немає: це зроблено навмисно, щоб програми не билися за місце біля
// годинника. Витягує іконку лише користувач — мишкою або перемикачем у «Параметрах».
//
// Спокуса є: стан лежить у HKCU\Control Panel\NotifyIconSettings\<хеш>, значення IsPromoted, і його
// видно очима. Ми його виставляли в 1 — не працює НІ в якому вигляді:
//   * при працюючому Explorer — ігнорується;
//   * після перезапуску Explorer — ігнорується;
//   * після повного перезавантаження машини — ігнорується, причому значення 1 у реєстрі вціліло;
//   * а перемикач у «Параметрах» пересуває іконку МИТТЄВО, не змінюючи в реєстрі жодного байта.
// Тобто живий стан тримає Explorer у себе, а цей ключ — його власний кеш, і запис ззовні нічого не
// вирішує. Хеш до того ж рахується зі шляху до exe, тож у кожної теки збірки свій запис.

function InitializeUninstall(): Boolean;
var
  DataFolder: string;
begin
  Result := True;
  RemoveUserData := False;

  if not UninstallSilent then
  begin
    // Шлях у змінну: рядок, що починається з «[», Inno приймає за заголовок секції.
    DataFolder := ExpandConstant('{userappdata}\KeySwitcher');
    RemoveUserData := MsgBox(FmtMessage(CustomMessage('RemoveUserData'), [DataFolder]),
      mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    // Той самий Restart Manager не закриє фонову тулзу й тут — без цього видалення лишило б
    // зайняті файли в теці застосунку.
    StopRunningApp();

    // Мертвий шлях у ключі автозапуску не має пережити програму: запис робить застосунок (Run-ключ
    // з'являється, коли ввімкнено autoStart), тому видаляємо безумовно.
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'KeySwitcher');
  end;

  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
    DelTree(ExpandConstant('{userappdata}\KeySwitcher'), True, True, True);
end;

#ifdef NeedsDesktopRuntime
// Чи є на машині .NET Desktop Runtime 10.0.x.
function IsWindowsDesktopRuntimeInstalled(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  FindRec: TFindRec;
begin
  Result := False;

  // Версії рантаймів .NET лежать значеннями в 32-бітному view реєстру: на цій машині
  // HKLM\SOFTWARE\dotnet\... у 64-бітному view не існує, а HKLM\SOFTWARE\WOW6432Node\dotnet\... існує.
  // Наш setup 64-бітний (ArchitecturesInstallIn64BitMode), тож читає 64-бітний view і мусить називати
  // WOW6432Node явно. Шлях без WOW6432Node лишається про всяк випадок.
  if RegGetValueNames(HKLM, 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) or
     RegGetValueNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, Length(DesktopRuntimeMajor) + 1) = DesktopRuntimeMajor + '.' then
      begin
        Result := True;
        Exit;
      end;

  // Страховка: самі теки рантайму (реєстр міг лишитися не від нашого інсталятора, і навпаки).
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\' + DesktopRuntimeMajor + '.*'), FindRec) then
  try
    Result := (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0;
  finally
    FindClose(FindRec);
  end;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(CustomMessage('DesktopRuntimePageTitle'),
    CustomMessage('DesktopRuntimePageText'), nil);
end;

// Порожній рядок = продовжити встановлення, непорожній = показати як помилку й спинитися.
function EnsureDesktopRuntime(): String;
var
  RuntimePath: String;
  ResultCode: Integer;
begin
  Result := '';

  if IsWindowsDesktopRuntimeInstalled() then
  begin
    Log('desktop runtime 10: already installed, skipping the download');
    Exit;
  end;

  Log('desktop runtime 10: not installed');

  // У тихому режимі MsgBox не показується, і питати нема кого — ставимо потрібне молча.
  if not WizardSilent then
    if MsgBox(CustomMessage('DesktopRuntimePrompt'), mbConfirmation, MB_YESNO) <> IDYES then
    begin
      Result := FmtMessage(CustomMessage('DesktopRuntimeDeclined'), ['{#DesktopRuntimeHelpUrl}']);
      Exit;
    end;

  DownloadPage.Clear;
  DownloadPage.Add('{#DesktopRuntimeUrl}', DesktopRuntimeFileName, '');
  if not WizardSilent then
    DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      Result := FmtMessage(CustomMessage('DesktopRuntimeDownloadFailed'), [
        GetExceptionMessage, '{#DesktopRuntimeHelpUrl}']);
      Exit;
    end;
  finally
    if not WizardSilent then
      DownloadPage.Hide;
  end;

  RuntimePath := ExpandConstant('{tmp}\') + DesktopRuntimeFileName;
  Log('desktop runtime 10: installing ' + RuntimePath);

  // Манифест рантайму — asInvoker, тобто він підвищує права сам і показує UAC, якщо треба:
  // підвищувати сам інсталятор заради цього не потрібно.
  if not Exec(RuntimePath, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := FmtMessage(CustomMessage('DesktopRuntimeInstallFailed'), [
      SysErrorMessage(ResultCode), '{#DesktopRuntimeHelpUrl}']);
    Exit;
  end;

  // 3010 — «потрібен перезапуск»: рантайм при цьому вже працює, а ми перезапускати не вимагаємо.
  Log('desktop runtime 10: installer exit code ' + IntToStr(ResultCode));
  if (ResultCode = 0) or (ResultCode = 3010) then
  begin
    if not IsWindowsDesktopRuntimeInstalled() then
      Result := FmtMessage(CustomMessage('DesktopRuntimeInstallFailed'), [
        IntToStr(ResultCode), '{#DesktopRuntimeHelpUrl}']);
  end
  else
    Result := FmtMessage(CustomMessage('DesktopRuntimeInstallFailed'), [
      IntToStr(ResultCode), '{#DesktopRuntimeHelpUrl}']);
end;
#endif

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;
  StopRunningApp();
#ifdef NeedsDesktopRuntime
  Result := EnsureDesktopRuntime();
#endif
end;
