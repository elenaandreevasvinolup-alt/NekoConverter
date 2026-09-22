#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Сборка всего, что приложение скачивает, и подготовка каталога к публикации.

Что делает скрипт:
  1. Собирает языковой пакет из src/NekoConverter.App/Locale.
  2. Упаковывает установленные движки (FFmpeg, Pandoc), если они есть локально.
  3. Считает для каждого архива sha256 и размер.
  4. Вписывает базовые адреса площадок в catalog.json.
  5. Печатает список файлов и что с ними делать.

После запуска от вас требуется только загрузить файлы на три площадки —
всё остальное уже сделано.

Запуск:
  python3 tools/make_release.py --owner <ваш-ник> --version v1.0.0

  # если ники на GitHub и Gitee разные:
  python3 tools/make_release.py --github-owner nick1 --gitee-owner nick2 --version v1.0.0
"""
import argparse
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
OUTPUT = os.path.join(ROOT, 'dist', 'downloads')
CATALOG = os.path.join(ROOT, 'src', 'NekoConverter.Core', 'catalog.json')
LOCALE_SRC = os.path.join(ROOT, 'src', 'NekoConverter.App', 'Locale')

# Откуда берутся уже установленные движки: их не нужно качать заново,
# они лежат в каталоге пользовательских данных.
INSTALLED = os.path.expanduser('~/Library/Application Support/NekoConverter/packages')

# Максимальный размер файла для jsDelivr. Он отдаёт только файлы из репозитория
# и не больше 20 МБ, поэтому крупные движки туда не попадают.
JSDELIVR_LIMIT = 20 * 1024 * 1024


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


# Фиксированное время и права для всех записей архива.
#
# Иначе архив меняется от запуска к запуску: package.json пересобирается
# каждый раз с новым временем, а os.walk обходит каталоги в произвольном
# порядке. Контрольная сумма тогда перестаёт совпадать с уже выложенными
# файлами, и приложение отказывается устанавливать пакет.
FIXED_TIMESTAMP = (2020, 1, 1, 0, 0, 0)


def add_file(z, full, arc):
    """Кладёт файл в архив с фиксированными метаданными."""
    info = zipfile.ZipInfo(arc, date_time=FIXED_TIMESTAMP)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o644 << 16

    with open(full, 'rb') as f:
        z.writestr(info, f.read())


def make_zip(source_dir, archive_path, flatten=False, skip_names=()):
    """Упаковывает каталог. flatten — класть файлы в корень архива."""
    with zipfile.ZipFile(archive_path, 'w', zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(source_dir):
            for name in sorted(files):
                if name in skip_names or name == '.DS_Store':
                    continue

                full = os.path.join(root, name)
                arc = name if flatten else os.path.relpath(full, os.path.dirname(source_dir))
                add_file(z, full, arc)


def build_locales():
    """Языковой пакет: все strings.*.json в корне архива."""
    archive = os.path.join(OUTPUT, 'pack-locales-1.0.0.zip')

    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for name in sorted(os.listdir(LOCALE_SRC)):
            if name.startswith('strings.') and name.endswith('.json'):
                add_file(z, os.path.join(LOCALE_SRC, name), name)

    return archive


def build_engine(package_id):
    """Упаковывает установленный движок вместе с его манифестом."""
    source = os.path.join(INSTALLED, package_id)

    if not os.path.isdir(source):
        return None

    manifest_path = os.path.join(source, 'package.json')
    version = '1.0.0'

    if os.path.isfile(manifest_path):
        with io.open(manifest_path, encoding='utf-8') as f:
            version = json.load(f).get('version', version)

    archive = os.path.join(OUTPUT, f'{package_id}-{version}.zip')

    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(source):
            for name in sorted(files):
                if name == '.DS_Store':
                    continue

                full = os.path.join(root, name)
                arc = os.path.relpath(full, os.path.dirname(source))
                add_file(z, full, arc)

    return archive


def update_package_hashes(hashes):
    """
    Вписывает в каталог размер и контрольную сумму каждого архива.

    Без этого пришлось бы переносить значения руками, а расхождение суммы
    означает отказ установки — то есть ошибку, которую заметит пользователь,
    а не сборка.
    """
    with io.open(CATALOG, encoding='utf-8') as f:
        text = f.read()

    for package_id, (size, digest) in hashes.items():
        # находим блок пакета по идентификатору и правим в нём два поля
        pattern = re.compile(
            r'("id":\s*"%s".*?)("sizeBytes":\s*)\d+(.*?)("sha256":\s*")[0-9a-f]*(")'
            % re.escape(package_id),
            re.S)

        def replace(match):
            return f'{match.group(1)}{match.group(2)}{size}{match.group(3)}{match.group(4)}{digest}{match.group(5)}'

        text, count = pattern.subn(replace, text, count=1)

        if count == 0:
            print(f'    предупреждение: пакет «{package_id}» не найден в каталоге')

    with io.open(CATALOG, 'w', encoding='utf-8') as f:
        f.write(text)


def clear_upstream_urls(package_ids):
    """
    Убирает ссылки на оригинальные сборки у переупакованных пакетов.

    Причина: мы упаковываем движок заново и добавляем внутрь package.json,
    поэтому наш архив — ДРУГОЙ файл с другой контрольной суммой. Если оставить
    ссылку на оригинал, приложение скачает его, и проверка суммы откажет.
    Остаются только собственные площадки.
    """
    with io.open(CATALOG, encoding='utf-8') as f:
        text = f.read()

    for package_id in package_ids:
        pattern = re.compile(
            r'("id":\s*"%s".*?)("downloadUrl":\s*")[^"]*(")' % re.escape(package_id),
            re.S)

        text, count = pattern.subn(lambda m: f'{m.group(1)}{m.group(2)}{m.group(3)}', text, count=1)

        if count == 0:
            print(f'    предупреждение: у «{package_id}» не найдено поле downloadUrl')

    with io.open(CATALOG, 'w', encoding='utf-8') as f:
        f.write(text)


def update_catalog(github_owner, gitee_owner, gitee_repo, version):
    """
    Вписывает базовые адреса площадок.

    Заменяются три строки в блоке releaseBase — вписывать ссылки в каждый пакет
    не нужно, они строятся из этих адресов и имён файлов.
    """
    with io.open(CATALOG, encoding='utf-8') as f:
        text = f.read()

    block = '''  // ─────────── Площадки, откуда приложение скачивает пакеты ───────────
  //
  // Заполняется ОДИН раз на весь каталог. Имена файлов подставляются
  // автоматически по правилу «<id>-<версия>.zip», поэтому дублировать
  // ссылки в каждом пакете не нужно.
  //
  // Три площадки независимы и служат друг другу резервом: приложение замеряет
  // их одновременно и берёт быстрейшую, а при обрыве переходит к следующей.
  "releaseBase": {
    "github": "https://github.com/%s/NekoConverter/releases/download/%s",
    "gitee": "https://gitee.com/%s/%s/releases/download/%s",
    "jsdelivr": "https://cdn.jsdelivr.net/gh/%s/NekoConverter@%s/dist/downloads"
  },

''' % (github_owner, version, gitee_owner, gitee_repo, version, github_owner, version)

    # заменяем существующий блок или добавляем новый
    if '"releaseBase"' in text:
        text = re.sub(r'\s*//[^\n]*Площадки[^\n]*\n(?:\s*//[^\n]*\n)*\s*"releaseBase":\s*\{[^}]*\},?\n',
                      '\n' + block, text, count=1)
        # если шаблон не совпал (комментарии могли отличаться) — правим только тело
        if '"releaseBase"' in text:
            text = re.sub(r'"releaseBase":\s*\{[^}]*\}',
                          block.strip().rstrip(',').split('"releaseBase":', 1)[1].strip().rstrip(',')
                          .join(['"releaseBase": {', '}']) if False else
                          block.strip().rstrip(',').split('"releaseBase":', 1)[1].join(['"releaseBase":', '']),
                          text, count=1)
    else:
        text = text.replace('"packages": [', block + '  "packages": [', 1)

    with io.open(CATALOG, 'w', encoding='utf-8') as f:
        f.write(text)


def main():
    parser = argparse.ArgumentParser(description='Сборка архивов для публикации')
    parser.add_argument('--owner', help='ник на GitHub и Gitee, если он совпадает')
    parser.add_argument('--github-owner', help='ник на GitHub')
    parser.add_argument('--gitee-owner', help='ник на Gitee')
    parser.add_argument('--gitee-repo', default='NekoConverter',
                        help='путь репозитория на Gitee (Gitee переводит CamelCase в kebab-case)')
    parser.add_argument('--version', default='v1.0.0', help='тег версии, например v1.0.0')
    args = parser.parse_args()

    github_owner = args.github_owner or args.owner
    gitee_owner = args.gitee_owner or args.owner or args.github_owner
    gitee_repo = args.gitee_repo

    if not github_owner:
        print('Укажите --owner <ник> (или --github-owner и --gitee-owner)', file=sys.stderr)
        return 1

    os.makedirs(OUTPUT, exist_ok=True)

    print('=' * 70)
    print('СБОРКА АРХИВОВ')
    print('=' * 70)

    archives = []

    locales = build_locales()
    archives.append(locales)
    print(f'  языковой пакет   {os.path.basename(locales)}')

    for package_id in ('dep-ffmpeg', 'dep-pandoc'):
        engine = build_engine(package_id)

        if engine:
            archives.append(engine)
            print(f'  движок           {os.path.basename(engine)}')
        else:
            print(f'  движок           {package_id} не установлен локально — пропущен')

    print()
    print('=' * 70)
    print('КОНТРОЛЬНЫЕ СУММЫ')
    print('=' * 70)

    hashes = {}

    for path in archives:
        size = os.path.getsize(path)
        digest = sha256_of(path)
        note = ' (больше 20 МБ — jsDelivr не подойдёт)' if size > JSDELIVR_LIMIT else ''

        # идентификатор пакета — имя файла без версии и расширения
        name = os.path.basename(path)[:-4]
        package_id = name.rsplit('-', 1)[0]
        hashes[package_id] = (size, digest)

        print(f'  {os.path.basename(path)}')
        print(f'    размер: {size:,} байт')
        print(f'    sha256: {digest}{note}')
        print()

    # Переупакованные движки получают только собственные адреса.
    clear_upstream_urls([pid for pid in hashes if pid.startswith('dep-')])

    update_package_hashes(hashes)
    update_catalog(github_owner, gitee_owner, gitee_repo, args.version)

    print('=' * 70)
    print('КАТАЛОГ ОБНОВЛЁН')
    print('=' * 70)
    print(f'  GitHub:   {github_owner}')
    print(f'  Gitee:    {gitee_owner}')
    print(f'  версия:   {args.version}')
    print()
    print('=' * 70)
    print('ЧТО ДЕЛАТЬ ДАЛЬШЕ')
    print('=' * 70)
    print()
    print('  1. Загрузите файлы из dist/downloads/ на три площадки:')
    print()
    print(f'     GitHub — создайте релиз с тегом {args.version}:')
    print('       https://github.com/%s/NekoConverter/releases/new' % github_owner)
    print()
    print(f'     Gitee — создайте релиз с тегом {args.version}:')
    print('       https://gitee.com/%s/%s/releases/new' % (gitee_owner, gitee_repo))
    print()
    print('     jsDelivr — ничего загружать не нужно: он отдаёт файлы')
    print('     прямо из репозитория GitHub. Достаточно, чтобы папка')
    print('     dist/downloads/ была закоммичена и помечена тегом.')
    print()
    print('  2. Пересоберите приложение, чтобы обновлённый catalog.json')
    print('     попал внутрь:')
    print()
    print('       ./scripts/package-macos.sh')
    print()

    return 0


if __name__ == '__main__':
    sys.exit(main())
