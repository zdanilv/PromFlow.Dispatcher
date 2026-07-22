# Сборка установщиков для Windows 10/11 и ALT Linux 11.1

## 1. Что получится в конце

Эта инструкция описывает сборку desktop-приложения `PromFlow Dispatcher`, а не
создание загрузочного ISO-образа операционной системы.

После успешной сборки версии `1.0.0` в каталоге
`artifacts/release/1.0.0` должны находиться:

```text
PromFlow.Dispatcher-1.0.0-win-x64-setup.exe
PromFlow.Dispatcher-1.0.0-win-x64-setup.exe.sha256
promflow-dispatcher-1.0.0-alt1.x86_64.rpm
promflow-dispatcher-1.0.0-alt1.x86_64.rpm.sha256
```

Оба варианта собираются только для 64-разрядных процессоров x86-64. Приложение
публикуется как self-contained: на компьютере конечного пользователя не требуется
отдельно устанавливать .NET Runtime. Внутри установщика находится многофайловый
publish-каталог. Это надёжнее для текущих Avalonia, Skia, Modbus и OPC UA
зависимостей, чем trimming или single-file с распаковкой нативных библиотек.

Сборка Windows выполняется на Windows, а RPM — непосредственно на чистой или
тестовой ALT Linux 11.1. Так RPM проверяется инструментами и репозиториями той ОС,
для которой он предназначен.

## 2. Поддерживаемые системы и ограничения

| Система | Назначение проверки |
|---|---|
| Windows 10 Enterprise LTSC 2021 x64 | основной поддерживаемый Windows 10 target |
| Windows 10 Home/Pro 22H2 x64 | технический smoke-тест; сама ОС уже вне основной поддержки Microsoft |
| Windows 11 24H2 или новее x64 | основной Windows 11 target |
| ALT Workstation 11.1 x86_64 | сборка RPM и обязательный runtime smoke-тест |

ALT Linux отсутствует в официальной матрице дистрибутивов .NET, а у Avalonia
относится к категории других Linux-дистрибутивов. Поэтому фраза «поддерживается
ALT Linux 11.1» означает, что именно собранный RPM прошёл описанную ниже проверку
на чистой VM.

Текущая версия Avalonia использует на Linux X11. В Wayland-сессии должен работать
XWayland. Для запуска нужны графическая сессия и непустая переменная `DISPLAY`.
RPM включает зависимость `xterm`: она нужна, чтобы запуск admin-режима из графического
ярлыка показывал консоль с логами. В user-режиме desktop entry остаётся без терминала.

Официальные источники:

- [.NET 10: поддерживаемые ОС](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md);
- [поддерживаемые платформы Avalonia](https://docs.avaloniaui.net/docs/supported-platforms);
- [публикация .NET-приложения](https://learn.microsoft.com/dotnet/core/tools/dotnet-publish);
- [self-contained и single-file](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview);
- [руководство ALT Workstation 11.1](https://docs.altlinux.org/ru-RU/alt-workstation/11.1/html-single/alt-workstation/index.html);
- [документация компилятора Inno Setup](https://jrsoftware.org/ishelp/topic_compilercmdline.htm).

## 3. Что уже настроено в репозитории

- `global.json` запрещает preview SDK и выбирает последний установленный патч
  feature band `10.0.3xx`.
- `Configurator.Boot` создаёт исполняемый файл `PromFlow.Dispatcher`.
- Publish-профили `win-x64` и `linux-x64` включают self-contained и отключают
  trimming, single-file, ReadyToRun и PDB.
- Базовый `appsettings.json` публикуется рядом с программой, но изменения
  пользователя сохраняются вне защищённого каталога установки.
- Windows-скрипт собирает EXE через Inno Setup.
- Linux-скрипт создаёт ALT RPM через `rpmbuild`.
- На первой Windows-установке Inno Setup создаёт запись автозапуска для
  устанавливающего пользователя. Далее приложение синхронизирует `Startup.Enabled`
  с Windows Run или XDG Autostart при каждом старте и не перезаписывает существующий
  пользовательский конфигурационный файл при обновлении.

Пользовательские файлы не должны появляться в `Program Files`, `/opt` или корне
репозитория:

| Данные | Windows | ALT Linux |
|---|---|---|
| Общие настройки | `%LOCALAPPDATA%\Configurator\appsettings.json` | `$XDG_DATA_HOME/Configurator/appsettings.json` или `~/.local/share/Configurator/appsettings.json` |
| Настройки окна | `%LOCALAPPDATA%\Configurator\user_settings.json` | `$XDG_DATA_HOME/Configurator/user_settings.json` или `~/.local/share/Configurator/user_settings.json` |
| RouteMap | `%LOCALAPPDATA%\Configurator\RouteMap\route-map.json` | `$XDG_DATA_HOME/Configurator/RouteMap/route-map.json` или `~/.local/share/Configurator/RouteMap/route-map.json` |
| Логи | `%LOCALAPPDATA%\Configurator\logs\app-YYYYMMDD.log` | `$XDG_DATA_HOME/Configurator/logs/app-YYYYMMDD.log` или `~/.local/share/Configurator/logs/app-YYYYMMDD.log` |

Если новая версия ещё не создала пользовательский `user_settings.json`, она
проверит прежний относительный файл из текущего каталога или каталога portable-
приложения, перенесёт настройки в новый путь и оставит исходный файл на месте.

Деинсталляторы намеренно не удаляют эти данные. Это позволяет обновить или
переустановить программу без потери настроек.

## 4. Правила выбора версии

Оба скрипта требуют версию строго в формате:

```text
MAJOR.MINOR.PATCH
```

Примеры: `1.0.0`, `1.4.2`, `2.0.0`.

Не используйте `v1.0.0`, `1.0`, `1.0.0-beta` или пробелы. Одна версия должна
использоваться для EXE и RPM. Перед выпуском убедитесь, что изменения сохранены в
Git и запишите commit:

```powershell
git status --short
git rev-parse HEAD
```

## 5. Сборка Windows-установщика

### 5.1. Что установить на компьютер сборки

Нужна 64-разрядная Windows 10/11 с доступом в интернет и правами на установку
программ. Запустите обычный PowerShell. Администратор нужен только для установки
инструментов, но не для самой сборки.

Если доступен `winget`, выполните:

```powershell
winget install --id Git.Git --exact
winget install --id Microsoft.DotNet.SDK.10 --exact
winget install --id JRSoftware.InnoSetup --exact
```

Если пакет Inno Setup не находится в `winget`, скачайте последнюю стабильную
версию с [официального сайта](https://jrsoftware.org/isdl.php) и установите её в
предлагаемый каталог. Скрипт умеет находить Inno Setup 6 и 7.

Для необязательной цифровой подписи дополнительно установите Windows SDK с
`signtool.exe` и импортируйте сертификат Code Signing в хранилище сертификатов.

Закройте и снова откройте PowerShell, затем проверьте инструменты:

```powershell
git --version
dotnet --list-sdks
dotnet --version
Get-Command ISCC.exe -ErrorAction SilentlyContinue
```

`dotnet --version` должен выводить стабильную версию `10.0.3xx`, без слова
`preview`. Если `ISCC.exe` не находится через `Get-Command`, это ещё не ошибка:
скрипт также проверит стандартные каталоги `Inno Setup 7` и `Inno Setup 6`.

### 5.2. Получить проект

Если репозиторий ещё не скачан:

```powershell
git clone https://github.com/zdanilv/PromFlow.Dispatcher.git
Set-Location .\PromFlow.Dispatcher
```

Если проект уже есть:

```powershell
Set-Location C:\путь\к\PromFlow.Dispatcher
git status --short
```

Не запускайте скрипт из каталога `Configurator.Boot`. Рабочим каталогом должен
быть корень, где находится `DesktopTemplate.slnx`.

### 5.3. Создать unsigned установщик

Скрипт сам выполнит restore, Release build, все тесты, publish и Inno Setup:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Configurator.Boot\Packaging\Build-WindowsInstaller.ps1 -Version 1.0.0
```

Предупреждение об отсутствии `CertificateThumbprint` ожидаемо: тестовый
установщик будет без Authenticode-подписи.

Успешный финал содержит строки, похожие на:

```text
Installer: ...\artifacts\release\1.0.0\PromFlow.Dispatcher-1.0.0-win-x64-setup.exe
SHA-256:  ...\artifacts\release\1.0.0\PromFlow.Dispatcher-1.0.0-win-x64-setup.exe.sha256
```

Если build или хотя бы один тест завершился с ошибкой, установщик не должен
создаваться. Исправьте первую ошибку и повторите ту же команду.

### 5.4. Проверить SHA-256

```powershell
$installer = '.\artifacts\release\1.0.0\PromFlow.Dispatcher-1.0.0-win-x64-setup.exe'
Get-FileHash $installer -Algorithm SHA256
Get-Content "$installer.sha256"
```

Длинные шестнадцатеричные значения должны совпадать без учёта регистра.

### 5.5. Подписать приложение и установщик

Сначала найдите thumbprint сертификата:

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Select-Object Subject, Thumbprint, NotAfter
```

Повторите полную сборку с thumbprint:

```powershell
.\Configurator.Boot\Packaging\Build-WindowsInstaller.ps1 `
    -Version 1.0.0 `
    -CertificateThumbprint 'ВАШ_THUMBPRINT'
```

Скрипт подписывает сначала `PromFlow.Dispatcher.exe`, затем готовый установщик и
только после этого пересчитывает SHA-256. Закрытый ключ и пароль нельзя добавлять
в Git или в текст скрипта.

Проверка:

```powershell
Get-AuthenticodeSignature `
    '.\artifacts\release\1.0.0\PromFlow.Dispatcher-1.0.0-win-x64-setup.exe' |
    Format-List Status, StatusMessage, SignerCertificate
```

Для подписанного production-файла ожидается `Status: Valid`.

### 5.6. Установить и запустить на Windows

1. Скопируйте EXE на чистую тестовую VM без отдельно установленного .NET Runtime.
2. Дважды нажмите установщик.
3. Подтвердите UAC: программа устанавливается для компьютера в
   `C:\Program Files\PromFlow Dispatcher`.
4. При необходимости отметьте создание ярлыка рабочего стола.
5. После установки запустите `PromFlow Dispatcher` из меню «Пуск».
6. Измените настройку окна или RouteMap, закройте приложение обычной кнопкой и
   запустите повторно. Настройка должна сохраниться.
7. Проверьте появление лога в `%LOCALAPPDATA%\Configurator\logs`.

Тихая установка для администратора:

```powershell
.\PromFlow.Dispatcher-1.0.0-win-x64-setup.exe `
    /VERYSILENT /SUPPRESSMSGBOXES /NORESTART `
    /LOG="$env:TEMP\PromFlow-setup.log"
```

Для удаления используйте «Установленные приложения» Windows или
`unins000.exe` из каталога приложения. Настройки в `%LOCALAPPDATA%\Configurator`
останутся. Удаляйте этот каталог вручную только если действительно требуется
полный сброс профиля.

### 5.7. Обновление Windows-версии

Соберите новую версию, например `1.0.1`, и запустите новый установщик поверх
`1.0.0`. Стабильный Inno Setup `AppId` позволяет распознать установленный продукт.
После обновления проверьте:

- в каталоге программы нет старых DLL, отсутствующих в новом publish;
- версия в «Установленных приложениях» стала `1.0.1`;
- пользовательские настройки сохранились;
- приложение запускается из старого ярлыка.

## 6. Сборка RPM на ALT Linux 11.1

### 6.1. Подготовить чистую VM

Рекомендуется ALT Workstation 11.1 x86_64, а не минимальный серверный образ: GUI
требует X11/XWayland. В терминале проверьте:

```bash
uname -m
cat /etc/altlinux-release
printf 'DISPLAY=%s\n' "${DISPLAY:-<empty>}"
```

Ожидаются `x86_64`, версия 11.1 и непустой `DISPLAY` в графической сессии.

### 6.2. Установить build- и runtime-зависимости

Получите root-права принятой в вашей организации командой, например:

```bash
su -
```

Обновите индексы и установите инструменты:

```bash
apt-get update
apt-get install git curl ca-certificates tar gzip \
    rpm-build rpm-build-licenses desktop-file-utils
```

Установите библиотеки Avalonia и базовые зависимости portable .NET:

```bash
apt-get install glibc libgcc1 libstdc++6 \
    libX11 libICE libSM fontconfig \
    libicu libkrb5 openssl zlib tzdata
```

В актуальном p11 Kerberos runtime называется `libkrb5`. Если конкретная редакция
ALT сообщает, что `libicu` или другой пакет не найден, не подставляйте имя из
Ubuntu. Сначала найдите имя в подключённых p11-репозиториях:

```bash
apt-cache search '^libicu'
apt-cache search '^libssl'
apt-cache search '^libkrb5'
```

Выберите пакет shared/runtime libraries, не пакет с суффиксом `-devel`. После
установки выйдите из root-shell:

```bash
exit
```

Не скачивайте RPM системных библиотек с сайтов других дистрибутивов.

### 6.3. Установить стабильный .NET 10 SDK только для сборщика

ALT не входит в официальный список package feeds Microsoft, поэтому используется
официальный generic installer в домашний каталог. Системный `/usr` не изменяется:

```bash
curl -fL https://dot.net/v1/dotnet-install.sh -o "$HOME/dotnet-install.sh"
chmod 700 "$HOME/dotnet-install.sh"
"$HOME/dotnet-install.sh" \
    --channel 10.0.3xx \
    --quality GA \
    --install-dir "$HOME/.dotnet"
```

Добавьте SDK в окружение:

```bash
printf '%s\n' \
    'export DOTNET_ROOT="$HOME/.dotnet"' \
    'export PATH="$DOTNET_ROOT:$PATH"' >> "$HOME/.bash_profile"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --info
```

В списке SDK должна быть стабильная `10.0.3xx`; preview использовать нельзя.
Этот SDK нужен только машине сборки. На целевую машину приложение приносит свой
runtime внутри RPM.

### 6.4. Получить проект и разрешить запуск скрипта

```bash
git clone https://github.com/zdanilv/PromFlow.Dispatcher.git
cd PromFlow.Dispatcher
git status --short
chmod +x Configurator.Boot/Packaging/build-rpm.sh
```

`chmod` нужен после переноса архива через файловую систему, которая могла потерять
Unix execute bit. В Git этот файл должен храниться как executable.

### 6.5. Собрать unsigned RPM

```bash
./Configurator.Boot/Packaging/build-rpm.sh 1.0.0
```

Скрипт выполнит restore, Release build, все тесты, self-contained publish,
подготовит локальное RPM build tree и вызовет `rpmbuild -bb`. Предупреждение об
отсутствии `--gpg-key` ожидаемо для тестового пакета.

Успешный финал:

```text
RPM:      .../artifacts/release/1.0.0/promflow-dispatcher-1.0.0-alt1.x86_64.rpm
SHA-256: .../artifacts/release/1.0.0/promflow-dispatcher-1.0.0-alt1.x86_64.rpm.sha256
```

Проверка контрольной суммы:

```bash
cd artifacts/release/1.0.0
sha256sum -c promflow-dispatcher-1.0.0-alt1.x86_64.rpm.sha256
```

Ожидается `OK`.

### 6.6. Необязательно подписать RPM

Установите и настройте GPG/rpmsign по правилам организации. Проверьте секретный
ключ:

```bash
gpg --list-secret-keys --keyid-format LONG
```

Соберите пакет с идентификатором ключа:

```bash
./Configurator.Boot/Packaging/build-rpm.sh 1.0.0 \
    --gpg-key 'ВАШ_GPG_KEY_ID'
```

Скрипт использует `rpmsign`, а при его отсутствии — `rpm --addsign`, после чего
пересчитывает SHA-256. Проверка:

```bash
rpm --checksig \
    artifacts/release/1.0.0/promflow-dispatcher-1.0.0-alt1.x86_64.rpm
```

Закрытый ключ и passphrase не должны храниться в репозитории.

### 6.7. Проверить содержимое RPM

```bash
RPM=artifacts/release/1.0.0/promflow-dispatcher-1.0.0-alt1.x86_64.rpm
rpm -qpi "$RPM"
rpm -qpl "$RPM"
rpm -qp --scripts "$RPM"
```

В списке должны присутствовать:

```text
/opt/promflow-dispatcher/PromFlow.Dispatcher
/usr/bin/promflow-dispatcher
/usr/share/applications/promflow-dispatcher.desktop
/usr/share/icons/hicolor/256x256/apps/promflow-dispatcher.png
```

### 6.8. Установить RPM

ALT рекомендует устанавливать локальный RPM через APT, чтобы он разрешил
зависимости. Перейдите в root-shell и укажите абсолютный путь:

```bash
RPM_PATH="$(readlink -f artifacts/release/1.0.0/promflow-dispatcher-1.0.0-alt1.x86_64.rpm)"
su -
apt-get install "$RPM_PATH"
exit
```

Не используйте `rpm -ivh` как основной способ: он не подтягивает отсутствующие
зависимости.

### 6.9. Проверить нативные библиотеки и запуск

```bash
ldd /opt/promflow-dispatcher/PromFlow.Dispatcher | grep 'not found' || true
rpm -V promflow-dispatcher
/usr/bin/promflow-dispatcher
```

Первая команда не должна вывести ни одной строки `not found`. Программа должна
открыться без установки system-wide .NET Runtime.

Также запустите приложение из меню рабочего окружения. Проверьте импорт/экспорт
JSON через файловый диалог, сохраните настройку, закройте приложение и запустите
снова. Проверьте лог:

```bash
find "${XDG_DATA_HOME:-$HOME/.local/share}/Configurator" -maxdepth 2 -type f -print
```

### 6.10. Обновить или удалить RPM

Для обновления соберите `1.0.1` и выполните:

```bash
su -
apt-get install /абсолютный/путь/promflow-dispatcher-1.0.1-alt1.x86_64.rpm
exit
```

Для удаления:

```bash
su -
apt-get remove promflow-dispatcher
exit
```

Пользовательские настройки сохранятся в
`${XDG_DATA_HOME:-$HOME/.local/share}/Configurator`. Удаляйте этот каталог вручную
только для полного сброса профиля конкретного пользователя.

## 7. Modbus TCP, OPC UA, права и firewall

### 7.1. Порт 502 на Linux

TCP-порты ниже 1024 считаются привилегированными. Обычный пользователь ALT не
может запустить Modbus TCP server на порту 502. GUI нельзя запускать от root.

Рекомендуемый вариант для локального сервера/демо — порт `1502` или другой порт
выше 1023. Настройте один и тот же порт в приложении и подключаемом клиенте.

Если промышленное окружение требует именно 502, системный администратор может
выдать только capability привязки к низкому порту:

```bash
su -
setcap 'cap_net_bind_service=+ep' /opt/promflow-dispatcher/PromFlow.Dispatcher
getcap /opt/promflow-dispatcher/PromFlow.Dispatcher
exit
```

Это осознанное security-решение. После обновления файла capability может
потребоваться назначить повторно. RPM по умолчанию её не выдаёт.

### 7.2. Firewall

Не открывайте порты, если приложение работает только как клиент. Для входящего
сервера разрешайте только используемый порт и нужный сетевой профиль.

Пример Windows PowerShell от администратора для тестового Modbus `1502`:

```powershell
New-NetFirewallRule `
    -DisplayName 'PromFlow Modbus TCP 1502' `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 1502 `
    -Profile Domain,Private
```

Пример firewalld на ALT, если он включён:

```bash
su -
firewall-cmd --permanent --add-port=1502/tcp
firewall-cmd --reload
exit
```

OPC UA в текущей конфигурации использует TCP `4840`. Открывайте его только если
встроенный OPC UA server действительно принимает подключения извне.

## 8. Типовые ошибки

### `A compatible .NET SDK was not found`

Выполните `dotnet --list-sdks`. Нужен стабильный `10.0.3xx`. Установленный только
`10.0.4xx-preview` не подходит из-за `global.json`.

### `NETSDK1057` и сообщение о preview

Команда запущена не из дерева репозитория либо стабильный SDK отсутствует.
Проверьте наличие `global.json`, текущий каталог и `dotnet --version`.

### NuGet restore не может скачать пакет

Проверьте DNS, proxy, HTTPS-доступ к NuGet и системное время. Не копируйте DLL из
чужого publish-каталога. После восстановления сети повторите скрипт целиком.

Если restore завершается ошибкой `NU3012` и сообщает об отозванном сертификате
автора пакета, **не отключайте проверку подписей** и не добавляйте
`signatureValidationMode=accept`. Очистите локальные кэши и повторите restore:

```powershell
dotnet nuget locals all --clear
dotnet restore DesktopTemplate.slnx
```

В репозитории уже зафиксированы версии ReactiveUI/Splat с действительными
подписями. Если ошибка повторилась на чистой машине, выпуск следует остановить:
проверьте, какой именно пакет указан в сообщении, и обновите его до корректно
подписанной совместимой версии. Обход проверки подписи делает сборку
невоспроизводимой и ослабляет защиту цепочки поставки.

### `ISCC.exe was not found`

Установите Inno Setup 6/7, перезапустите PowerShell. При нестандартной portable
установке добавьте каталог с `ISCC.exe` в `PATH` текущего процесса.

### Windows SmartScreen предупреждает о неизвестном издателе

Это ожидаемо для unsigned тестового EXE. Сверьте SHA-256. Для внешнего релиза
используйте действующий Authenticode Code Signing сертификат.

### `Permission denied` при запуске Linux-файла

Проверьте права:

```bash
ls -l /opt/promflow-dispatcher/PromFlow.Dispatcher
```

Файл должен иметь execute bit. Если права потеряны уже внутри собранного RPM,
пакет считается дефектным — исправьте сборку, а не выполняйте постоянный ручной
`chmod` после каждой установки.

### `error while loading shared libraries` или `not found` в `ldd`

Найдите имя отсутствующей `.so` и пакет p11, который её предоставляет:

```bash
apt-cache search имя_библиотеки
```

Установите пакет через `apt-get`, повторите `ldd`. Не создавайте вручную symlink
на библиотеку другой ABI-версии.

### `Unable to open X display` или окно не появляется

Проверьте:

```bash
echo "$DISPLAY"
loginctl show-session "$XDG_SESSION_ID" -p Type
```

Запускайте из графической X11-сессии либо убедитесь, что в Wayland работает
XWayland. Не запускайте GUI через обычную SSH-сессию без X forwarding.

### Modbus server сообщает `Permission denied`

На ALT выбран порт 502 или другой порт ниже 1024. Используйте 1502 либо попросите
администратора настроить ограниченный `CAP_NET_BIND_SERVICE`.

### Порт занят

Windows:

```powershell
Get-NetTCPConnection -LocalPort 1502 -ErrorAction SilentlyContinue
```

ALT:

```bash
ss -ltnp | grep ':1502 '
```

Остановите конфликтующий сервис или назначьте другой порт. Не завершайте
неизвестный системный процесс без согласования.

### Настройки не сохраняются

Проверьте каталог пользователя из таблицы в разделе 3 и лог приложения. Программа
не должна пытаться писать в `Program Files` или `/opt/promflow-dispatcher`.

## 9. Обязательный протокол проверки релиза

Перед передачей установщиков заполните таблицу реальными результатами. Значение
«не проверено» нельзя заменять предположением.

| Проверка | Windows 10 LTSC | Windows 10 22H2 smoke | Windows 11 | ALT 11.1 |
|---|---:|---:|---:|---:|
| Установка на чистую VM | ☐ | ☐ | ☐ | ☐ |
| Запуск без system-wide .NET Runtime | ☐ | ☐ | ☐ | ☐ |
| Иконка и главное окно | ☐ | ☐ | ☐ | ☐ |
| Файловый диалог import/export | ☐ | ☐ | ☐ | ☐ |
| Сохранение настроек после restart | ☐ | ☐ | ☐ | ☐ |
| Создание пользовательского лога | ☐ | ☐ | ☐ | ☐ |
| Modbus TCP на порту 1502 | ☐ | ☐ | ☐ | ☐ |
| Обновление поверх предыдущей версии | ☐ | ☐ | ☐ | ☐ |
| Удаление без потери профиля | ☐ | ☐ | ☐ | ☐ |
| SHA-256 проверен | ☐ | ☐ | ☐ | ☐ |

Минимальные данные протокола:

```text
Версия приложения:
Git commit:
Дата сборки:
.NET SDK:
Версия Inno Setup / rpm-build:
Имя и версия ОС VM:
Архитектура:
SHA-256 установщика:
Результат установки:
Результат запуска:
Путь к логу:
Обнаруженные ограничения:
ФИО проверившего:
```

### 9.1. Фактическая локальная проверка версии 1.0.0 от 17.07.2026

Ниже зафиксирован предварительный результат, полученный при подготовке этой
инфраструктуры. Он подтверждает работоспособность скриптов и пакетов, но не
заменяет обязательную матрицу чистых VM выше.

| Команда или проверка | Фактический результат |
|---|---|
| `Build-WindowsInstaller.ps1 -Version 1.0.0` | Успешно: build, 403 теста, self-contained publish, Inno Setup EXE и SHA-256 |
| Прямой запуск Windows publish | Процесс успешно запущен, создан пользовательский лог, фатальных ошибок нет |
| Тихая установка/запуск/удаление Inno Setup | Успешно на Windows 11 Pro x64, build 26200, для предыдущей сборки той же упаковочной схемы; после удаления пользовательский лог сохранён. Финальный EXE нужно повторно установить в VM с подтверждением UAC |
| `./build-rpm.sh 1.0.0` | Успешно в предварительной среде WSL Ubuntu 24.04: build, 403 теста, RPM и SHA-256 |
| `rpm -qpi`, `rpm -qpl`, `rpm -qp --scripts` | Метаданные и payload прочитаны; неожиданных install/uninstall scriptlets нет |
| Временная установка, `ldd`, запуск и удаление RPM | Успешно в WSLg после установки Linux/X11-зависимостей; `not found` отсутствует, пользовательский лог сохранён |
| Чистая ALT Workstation 11.1 x86_64 | **Не проверено: обязательно выполнить перед production-релизом** |
| Полная Windows VM-матрица из таблицы выше | **Не проверено: обязательно выполнить перед production-релизом** |

Оба файла версии 1.0.0 созданы в `artifacts/release/1.0.0/`. Тестовые файлы не
подписаны, потому что Authenticode- и GPG-ключи не предоставлены. Подпись можно
добавить штатными параметрами упаковочных скриптов без изменения приложения.

Релиз готов только когда EXE и RPM имеют одинаковую версию, их SHA-256 сохранены,
все автоматические тесты прошли, а обязательные VM-строки заполнены фактическими
результатами.
