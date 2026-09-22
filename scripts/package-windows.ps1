# Сборка Windows-версии NekoConverter.
#
# Три режима, от лучшего к запасному:
#
#   .\scripts\package-windows.ps1 -Aot
#       Native AOT. ~30 МБ, мгновенный запуск, без .NET Runtime у пользователя.
#       ТРЕБУЕТ Windows + MSVC (Visual Studio Build Tools, «Desktop development with C++»).
#       Кросс-компиляция Native AOT невозможна: линковщику нужен MSVC.
#
#   .\scripts\package-windows.ps1 -SingleFile
#       Один .exe, ~22 МБ. Собирается откуда угодно, включая macOS.
#       Нативные библиотеки распаковываются во временный каталог при каждом запуске,
#       поэтому старт примерно на секунду дольше.
#
#   .\scripts\package-windows.ps1
#       Каталог с файлами, ~42 МБ. Собирается откуда угодно.
#       Обычный запуск без распаковки. Вариант по умолчанию, когда AOT недоступен.
#
# Во всех режимах включены:
#   PublishTrimmed        — обрезка неиспользуемого кода BCL (главный выигрыш: 105 → 48 МБ)
#   InvariantGlobalization — без данных ICU, экономит ещё ~30 МБ
#   DebugType=none         — без отладочных символов
#
# Проверено: обрезка не ломает ни один движок. Сравнение рендера интерфейса
# до и после обрезки даёт побитово одинаковый результат.

param(
    [string]$Runtime = "win-x64",
    [switch]$Aot,
    [switch]$SingleFile,
    [switch]$Offline
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($Aot -and $SingleFile) {
    Write-Host "Режимы -Aot и -SingleFile несовместимы: AOT уже даёт один компактный файл." -ForegroundColor Red
    exit 1
}

if ($Aot -and -not $IsWindows) {
    Write-Host "Native AOT для Windows можно собрать только на Windows (нужен MSVC)." -ForegroundColor Red
    Write-Host "На этой системе используйте -SingleFile или запуск без параметров." -ForegroundColor Yellow
    exit 1
}

$suffix = if ($Aot) { "aot" } elseif ($SingleFile) { "single" } else { "folder" }
$output = "dist\$Runtime-$suffix"

Write-Host "==> RID: $Runtime" -ForegroundColor Cyan
Write-Host "==> Режим: $suffix" -ForegroundColor Cyan

# Иконка вшивается в exe только при сборке под Windows, поэтому передаём её
# флагом, а не держим в проекте: в проекте она ломала сборку ресурсов Avalonia
# и публикация с Native AOT переставала завершаться.
# Путь обязательно абсолютный: относительный MSBuild разрешает от каждого
# проекта отдельно, и Core искал иконку внутри собственного каталога.
$icon = Join-Path $root "src\NekoConverter.App\Assets\app.ico"

$arguments = @(
    "publish",
    "src\NekoConverter.App\NekoConverter.App.csproj",
    "-c", "Release",
    # -f обязателен: проект собирается под несколько платформ.
    "-f", "net10.0",
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:InvariantGlobalization=true",
    "-p:DebugType=none",
    "-o", $output
)

if (Test-Path $icon) {
    $arguments += "-p:ApplicationIcon=$icon"
    Write-Host "==> Иконка: $icon" -ForegroundColor Cyan
}

if ($Aot) {
    # AOT сам выполняет обрезку и не поддерживает PublishSingleFile.
    $arguments += "-p:PublishAot=true"
    $arguments += "-p:StripSymbols=true"
}
else {
    $arguments += "-p:PublishTrimmed=true"
    $arguments += "-p:TrimMode=partial"
}

if ($SingleFile) {
    $arguments += "-p:PublishSingleFile=true"
    $arguments += "-p:EnableCompressionInSingleFile=true"
    $arguments += "-p:IncludeNativeLibrariesForSelfExtract=true"
}

& dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "Сборка не удалась (код $LASTEXITCODE)"
}

# --- Чистка того, что нужно только отладчику ---

# Символы SkiaSharp весят около 100 МБ и в поставку не входят.
Get-ChildItem -Path $output -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

# Файлы поддержки отладчика и сборщика дампов: в рантайме не используются.
$debugOnly = @(
    "Microsoft.DiaSymReader.Native.amd64.dll",
    "mscordaccore.dll",
    "mscordbi.dll",
    "createdump.exe"
)
foreach ($name in $debugOnly) {
    Get-ChildItem -Path $output -Filter $name -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
}
Get-ChildItem -Path $output -Filter "mscordaccore_amd64*" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

# --- Проверка обязательных файлов ---

foreach ($data in @("formats.json", "catalog.json")) {
    if (-not (Test-Path (Join-Path $output $data))) {
        throw "В публикации нет $data — приложение не сможет запуститься."
    }
}

# Языковые файлы обязательны: без них интерфейс останется на запасном языке.
$localeDir = Join-Path $output "Locale"
if (-not (Test-Path $localeDir)) {
    throw "В публикации нет каталога Locale — интерфейс не сможет переключить язык."
}

# --- Итог ---

Write-Host ""
Write-Host "==> Готово: $output" -ForegroundColor Green

$files = Get-ChildItem -Path $output -Recurse -File
$total = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host ("    {0} файлов, {1:N1} МБ" -f $files.Count, ($total / 1MB))

$files | Sort-Object Length -Descending | Select-Object -First 8 |
    ForEach-Object { Write-Host ("    {0,10:N0}  {1}" -f $_.Length, $_.Name) }

# ─────────── Офлайн-набор ───────────
if ($Offline) {
    $offlineDir = "dist\$Runtime-offline"
    $depsDir = Join-Path $offlineDir "deps"

    Write-Host ""
    Write-Host "==> Собираю офлайн-набор: $offlineDir" -ForegroundColor Cyan

    if (Test-Path $offlineDir) { Remove-Item $offlineDir -Recurse -Force }
    New-Item -ItemType Directory -Path $offlineDir | Out-Null

    Copy-Item $output -Destination $offlineDir -Recurse

    # Зависимости распаковываются заранее: у пользователя установка не тратит сеть.
    dotnet run --project src\NekoConverter.Cli -c Release -- fetch-deps $depsDir --platform $Runtime

    if ($LASTEXITCODE -ne 0) { throw "Не удалось собрать офлайн-набор" }

    Write-Host ""
    Write-Host "==> Офлайн-набор готов: $offlineDir" -ForegroundColor Green
    $offlineTotal = (Get-ChildItem -Path $offlineDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("    Размер: {0:N1} МБ" -f ($offlineTotal / 1MB))
    Write-Host "    Папку deps нужно держать рядом с приложением."
}

Write-Host ""
Write-Host "Напоминание: на Windows движок sips недоступен — это часть macOS." -ForegroundColor Yellow
Write-Host "Изображения работают через Skia: PNG, JPEG, WebP, GIF, BMP." -ForegroundColor Yellow
Write-Host "Для TIFF, HEIC, RAW и остальной длинной линейки нужен модуль." -ForegroundColor Yellow
