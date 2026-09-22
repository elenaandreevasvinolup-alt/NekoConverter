#!/usr/bin/env bash
#
# Сборка мобильных версий NekoConverter.
#
# Использование:
#   scripts/package-mobile.sh android
#   scripts/package-mobile.sh ios
#
# ─────────────────────── Что здесь можно, а что нет ───────────────────────
#
# Проверено на этой машине:
#   • Android — workload не установлен, сборка не проверялась.
#   • iOS     — Xcode отсутствует (есть только Command Line Tools),
#               сборка НЕВОЗМОЖНА в принципе: нужен полноценный Xcode.
#
# Поэтому скрипт сначала честно проверяет окружение и объясняет, чего не хватает,
# вместо того чтобы падать на середине сборки с непонятной ошибкой.
#
# ─────────────────────── Чего лишается мобильная версия ───────────────────────
#
# Настольные сборки получают форматы из системных движков macOS (sips, afconvert)
# и из пакетов-зависимостей (FFmpeg, Pandoc). На телефонах ничего этого нет:
#
#   • sips и afconvert — часть macOS, на iOS и Android их не существует.
#     Значит, длинная линейка изображений (TIFF, HEIC, RAW, PSD, EXR) и звук
#     через CoreAudio (FLAC, M4A, ALAC) недоступны.
#
#   • FFmpeg и Pandoc для iOS и Android — это отдельные сборки под ARM,
#     а на iOS их ещё и нельзя просто скачать: правила App Store запрещают
#     загрузку исполняемого кода. Их пришлось бы включать в сам бандл,
#     что увеличило бы приложение примерно на 100 МБ на каждую платформу.
#
# Что реально работает на телефоне без всего этого:
#     изображения  PNG, JPEG, WebP, GIF, BMP           (движок skia)
#     звук         WAV, AIFF, AU                       (движок pcm)
#     субтитры     SRT, ASS, SSA, WebVTT, MicroDVD, TTML
#     данные       CSV, TSV, JSON, XML, XLSX
#     документы    DOCX → PDF / Markdown / текст / HTML
#
# Это 22 формата из 107. Остальное требует отдельных мобильных сборок движков.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PLATFORM="${1:-}"

if [ "$PLATFORM" != "android" ] && [ "$PLATFORM" != "ios" ]; then
  echo "Использование: scripts/package-mobile.sh android|ios" >&2
  exit 1
fi

# ─────────────── Проверка окружения ───────────────

if [ "$PLATFORM" = "ios" ]; then
  if ! xcodebuild -version >/dev/null 2>&1; then
    cat >&2 <<'MESSAGE'

Для сборки под iOS нужен полноценный Xcode, а не только Command Line Tools.

Сейчас активен каталог:
MESSAGE
    xcode-select -p >&2 || true
    cat >&2 <<'MESSAGE'

Что сделать:
  1. Установить Xcode из App Store.
  2. Переключить активный каталог:
       sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
  3. Установить workload:
       dotnet workload install ios

MESSAGE
    exit 1
  fi
fi

if ! dotnet workload list 2>/dev/null | grep -q "^$PLATFORM"; then
  echo "Workload «$PLATFORM» не установлен. Установите его:" >&2
  echo "    sudo dotnet workload install $PLATFORM" >&2
  exit 1
fi

# ─────────────── Android: ищем SDK и JDK сами ───────────────
#
# Без этих двух вещей сборка падает с XA5300 («Android SDK directory could not be found»),
# причём без подсказки, куда именно смотреть. Поэтому проверяем заранее и объясняем.

EXTRA_ARGS=()

if [ "$PLATFORM" = "android" ]; then
  ANDROID_SDK="${ANDROID_HOME:-$HOME/Library/Android/sdk}"

  if [ ! -d "$ANDROID_SDK/platforms" ]; then
    cat >&2 <<MESSAGE

Android SDK не найден в «$ANDROID_SDK».

Что нужно:
  1. Командные инструменты уже могут быть на месте. Проверьте:
       ls "$ANDROID_SDK/cmdline-tools/latest/bin/sdkmanager"
  2. Установите компоненты SDK (скачивание идёт с dl.google.com):
       export JAVA_HOME=\$(/usr/libexec/java_home -v 17)
       "$ANDROID_SDK/cmdline-tools/latest/bin/sdkmanager" --install \
         "platform-tools" "platforms;android-35" "build-tools;35.0.0"

  Если загрузка обрывается («unknown archive»), просто повторите команду:
  она докачивает недостающее.

MESSAGE
    exit 1
  fi

  if [ -z "${JAVA_HOME:-}" ]; then
    if JAVA_HOME="$(/usr/libexec/java_home -v 17 2>/dev/null)"; then
      export JAVA_HOME
    else
      echo "JDK 17 не найден. Установите Temurin 17: https://adoptium.net/temurin/releases/?version=17" >&2
      exit 1
    fi
  fi

  EXTRA_ARGS+=("-p:AndroidSdkDirectory=$ANDROID_SDK")
  echo "==> Android SDK: $ANDROID_SDK"
  echo "==> JDK: $JAVA_HOME"
fi

# ─────────────── Сборка ───────────────

# По умолчанию собираем под реальные устройства (arm64).
# Для эмулятора на Intel нужен другой идентификатор:
#   RID=android-x64 scripts/package-mobile.sh android
case "$PLATFORM" in
  android) RID="${RID:-android-arm64}" ;;
  ios)     RID="${RID:-ios-arm64}" ;;
esac

OUTPUT="dist/$PLATFORM"

echo "==> Платформа: $PLATFORM ($RID)"
echo "==> Интерфейс общий с десктопом; мобильная вёрстка включается по ширине экрана."

# Каждая мобильная платформа включается своим флагом. Общий флаг заставил бы
# SDK требовать workload обеих платформ сразу — именно на этом сборка под Android
# падала с NETSDK1147, требуя установить ещё и iOS.
case "$PLATFORM" in
  android) MOBILE_FLAG="-p:IncludeAndroid=true" ;;
  ios)     MOBILE_FLAG="-p:IncludeIos=true" ;;
esac

dotnet publish src/NekoConverter.App/NekoConverter.App.csproj \
  -c Release \
  -f "net10.0-$PLATFORM" \
  "$MOBILE_FLAG" \
  -p:RuntimeIdentifier="$RID" \
  "${EXTRA_ARGS[@]}" \
  -o "$OUTPUT"

echo
echo "==> Готово: $OUTPUT"
du -sh "$OUTPUT" 2>/dev/null || true

echo
echo "Напоминание: на телефоне работают 22 формата из 107."
echo "Длинная линейка изображений и звука требует системных движков macOS"
echo "или отдельных мобильных сборок FFmpeg и Pandoc."
