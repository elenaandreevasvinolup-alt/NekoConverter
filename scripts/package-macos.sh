#!/usr/bin/env bash
#
# Сборка macOS-бандла NekoConverter (.app) с обрезкой fat-бинарников.
#
# Что делает скрипт:
#   1. Публикует приложение через Native AOT (самодостаточный бинарник, без .NET Runtime).
#   2. Собирает структуру NekoConverter.app.
#   3. Обрезает libSkiaSharp/libHarfBuzzSharp: в NuGet они лежат как fat-бинарники
#      (x86_64 + arm64 сразу), то есть половина размера уходит на чужую архитектуру.
#   4. Подписывает бандл ad-hoc подписью, чтобы macOS не ругалась на изменённые dylib.
#
# Использование:
#   scripts/package-macos.sh              # ОБЫЧНАЯ сборка, БЕЗ зависимостей
#   scripts/package-macos.sh --offline    # плюс офлайн-набор (только по запросу)
#   RID=osx-arm64 scripts/package-macos.sh
#
# ВАЖНО: по умолчанию зависимости НЕ прикладываются.
#
# Решение осознанное: FFmpeg и Pandoc вместе весят около 190 МБ, и носить их
# в каждой сборке незачем. Движки качаются приложением по требованию —
# с замером источников и докачкой. Офлайн-набор остаётся как аварийный вариант
# для тех, у кого совсем нет доступа к сети, и собирается только по флагу.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

RID="${RID:-osx-x64}"
VERSION="${VERSION:-0.1.0}"

OFFLINE=0
for arg in "$@"; do
  [ "$arg" = "--offline" ] && OFFLINE=1
done

# lipo использует имена архитектур, отличные от RID .NET.
case "$RID" in
  osx-x64)   ARCH="x86_64" ;;
  osx-arm64) ARCH="arm64" ;;
  *)         ARCH="" ;;
esac

PUBLISH_DIR="dist/$RID-aot"
APP_DIR="dist/NekoConverter.app"
MACOS_DIR="$APP_DIR/Contents/MacOS"

echo "==> RID: $RID (архитектура $ARCH)"

echo "==> Публикация (Native AOT)"
# -f обязателен: проект собирается под несколько платформ,
# и без явного указания publish не знает, какую брать.
dotnet publish src/NekoConverter.App/NekoConverter.App.csproj \
  -c Release \
  -f net10.0 \
  -r "$RID" \
  -p:PublishAot=true \
  -p:StripSymbols=true \
  -o "$PUBLISH_DIR"

echo "==> Сборка бандла"
rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$APP_DIR/Contents/Resources"

cp "$PUBLISH_DIR/NekoConverter" "$MACOS_DIR/NekoConverter"

# ── Иконка ──
# На macOS иконка берётся не из exe, а из бандла: файл .icns в Resources
# плюс ссылка на него в Info.plist. Файл создаётся tools/IconMaker.
ICON_SOURCE="src/NekoConverter.App/Assets/AppIcon.icns"
if [ -f "$ICON_SOURCE" ]; then
  echo "    копирую иконку"
  cp "$ICON_SOURCE" "$APP_DIR/Contents/Resources/AppIcon.icns"
else
  echo "    предупреждение: $ICON_SOURCE не найден, иконка будет стандартной" >&2
fi

# Таблица форматов лежит рядом с исполняемым файлом и обязательна для запуска:
# без неё ядро не знает ни одного формата. Это данные, а не код, поэтому её
# можно править и после сборки, не перекомпилируя приложение.
# Языковые файлы лежат в подкаталоге Locale и обязательны:
# без них интерфейс останется на запасном языке.
if [ -d "$PUBLISH_DIR/Locale" ]; then
  echo "    копирую Locale ($(ls "$PUBLISH_DIR/Locale" | wc -l | tr -d ' ') языков)"
  cp -R "$PUBLISH_DIR/Locale" "$MACOS_DIR/Locale"
else
  echo "    ОШИБКА: в публикации нет каталога Locale" >&2
  exit 1
fi

for data in formats.json catalog.json; do
  if [ -f "$PUBLISH_DIR/$data" ]; then
    echo "    копирую $data"
    cp "$PUBLISH_DIR/$data" "$MACOS_DIR/$data"
  else
    echo "    ОШИБКА: в публикации нет $data" >&2
    exit 1
  fi
done

# Копируем ВСЕ динамические библиотеки, а не список из трёх имён.
# Список приходилось бы дополнять при каждом новом нативном движке,
# и однажды забытая строка даёт приложение, падающее на ровном месте
# (так и вышло с libassimp.dylib).
for lib_path in "$PUBLISH_DIR"/*.dylib; do
  [ -f "$lib_path" ] || continue

  lib="$(basename "$lib_path")"

  # Универсальные двоичные файлы содержат и x86_64, и arm64. Нам нужна одна
  # архитектура: вторая только удваивает размер.
  if [ -n "$ARCH" ] && lipo -info "$lib_path" 2>/dev/null | grep -q "arm64" \
     && lipo -info "$lib_path" 2>/dev/null | grep -q "x86_64"; then
    echo "    обрезаю $lib -> $ARCH"
    lipo -thin "$ARCH" "$lib_path" -output "$MACOS_DIR/$lib"
  else
    echo "    копирую $lib"
    cp "$lib_path" "$MACOS_DIR/$lib"
  fi
done

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>NekoConverter</string>
  <key>CFBundleDisplayName</key><string>NekoConverter</string>
  <key>CFBundleIdentifier</key><string>dev.nekoconverter.app</string>
  <key>CFBundleExecutable</key><string>NekoConverter</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

echo "==> Ad-hoc подпись (обрезанные dylib ломают исходную подпись)"
codesign --force --deep --sign - "$APP_DIR" 2>/dev/null \
  || echo "    предупреждение: подписать не удалось, для локального теста это не критично"

echo
echo "==> Готово: $APP_DIR"
du -sh "$APP_DIR"
du -sh "$APP_DIR/Contents/MacOS/"* | sort -rh

# ─────────── Офлайн-набор ───────────
if [ "$OFFLINE" = "1" ]; then
  OFFLINE_DIR="dist/macos-offline"
  DEPS_DIR="$OFFLINE_DIR/deps"

  echo
  echo "==> Собираю офлайн-набор: $OFFLINE_DIR"
  rm -rf "$OFFLINE_DIR"
  mkdir -p "$OFFLINE_DIR"

  cp -R "$APP_DIR" "$OFFLINE_DIR/"

  # Зависимости скачиваются один раз и распаковываются заранее, чтобы
  # у пользователя установка не занимала ни секунды сети.
  dotnet run --project src/NekoConverter.Cli -c Release -- \
    fetch-deps "$DEPS_DIR" --platform "$RID"

  echo
  echo "==> Офлайн-набор готов"
  du -sh "$OFFLINE_DIR"
  du -sh "$DEPS_DIR"/* 2>/dev/null | sort -rh

  echo
  echo "    Пользователь распаковывает папку целиком:"
  echo "      NekoConverter.app  и  deps/  должны лежать рядом."
  echo "    Зависимости подхватятся сами, устанавливать ничего не нужно."
fi
