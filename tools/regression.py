#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Полный прогон: по одной конвертации на каждую категорию плюс проверка того,
что каждый формат из таблицы вообще маршрутизируется.

Запуск:  python3 tools/regression.py [путь-к-nekoconv]
"""
import json
import os
import subprocess
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
CLI = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    ROOT, 'src', 'NekoConverter.Cli', 'bin', 'Release', 'net10.0', 'nekoconv')
FIX = os.path.join(ROOT, 'tests', 'fixtures')
TMP = '/tmp/nekoconv-regression'

os.makedirs(TMP, exist_ok=True)


def run(*args):
    p = subprocess.run([CLI, *args], capture_output=True, text=True, timeout=600)
    return p.returncode, (p.stdout + p.stderr).strip()


# ─────────── по одной конвертации на категорию ───────────

CASES = [
    ('изображение skia',    f'{FIX}/sample-rgba.png', 'jpeg',  'jpg'),
    ('изображение sips',    f'{FIX}/probe.tiff',      'png',   'png'),
    ('изображение кросс',   f'{FIX}/probe.heic',      'webp',  'webp'),
    ('звук pcm',            f'{FIX}/sample.wav',      'aiff',  'aiff'),
    ('звук afconvert',      f'{FIX}/sample.wav',      'flac',  'flac'),
    ('субтитры',            f'{FIX}/sample.ass',      'srt',   'srt'),
    ('данные csv→json',     f'{FIX}/sample.csv',      'json',  'json'),
    ('данные json→xlsx',    f'{FIX}/sample.json',     'xlsx',  'xlsx'),
    ('данные yaml',         f'{FIX}/sample.csv',      'yaml',  'yaml'),
    ('документ встроенный', f'{FIX}/sample.docx',     'pdf',   'pdf'),
    ('документ pandoc',     f'{FIX}/sample.docx',     'odt',   'odt'),
    ('3D',                  '/tmp/cube.stl',          'obj',   'obj'),
    ('видео',               '/tmp/src.mp4',           'mkv',   'mkv'),
    ('видео → звук',        '/tmp/src.mp4',           'mp3',   'mp3'),
]

print('=' * 74)
print('КОНВЕРТАЦИИ')
print('=' * 74)

passed = failed = 0

for label, source, target, extension in CASES:
    if not os.path.exists(source):
        print(f'  ПРОПУСК  {label:22} (нет файла {os.path.basename(source)})')
        continue

    output = os.path.join(TMP, f'{label.replace(" ", "_")}.{extension}')
    if os.path.exists(output):
        os.remove(output)

    code, text = run('convert', source, '--to', target, '-o', output)

    if code == 0 and os.path.exists(output) and os.path.getsize(output) > 0:
        size = os.path.getsize(output)
        print(f'  OK       {label:22} {target:6} {size:>9,} байт')
        passed += 1
    else:
        print(f'  ОШИБКА   {label:22} {target:6} {text[:60]}')
        failed += 1

# ─────────── маршрутизация всех форматов ───────────

print()
print('=' * 74)
print('МАРШРУТИЗАЦИЯ ФОРМАТОВ')
print('=' * 74)

code, text = run('formats')
if code != 0:
    print('  не удалось получить таблицу форматов')
    sys.exit(1)

lines = text.split('\n')
total = available = 0

for line in lines:
    if line.startswith('Всего форматов'):
        parts = line.replace(';', ' ').split()
        for i, token in enumerate(parts):
            if token == 'форматов:':
                total = int(parts[i + 1].rstrip(';'))
            if token == 'сразу:':
                available = int(parts[i + 1])

print(f'  всего форматов: {total}')
print(f'  доступно сейчас: {available}')

if 'ВНИМАНИЕ' in text:
    print('  ВНИМАНИЕ: в таблице есть дубликаты расширений!')
    failed += 1

# ─────────── локальные файлы ───────────

print()
print('=' * 74)
print('ЛОКАЛИЗАЦИЯ')
print('=' * 74)

locale_dir = os.path.join(ROOT, 'src', 'NekoConverter.App', 'Locale')
files = sorted(os.listdir(locale_dir))
keysets = {}

for name in files:
    with open(os.path.join(locale_dir, name), encoding='utf-8') as f:
        keysets[name] = set(json.load(f).keys())

reference = keysets.get('strings.zh-Hans.json', set())
print(f'  языков: {len(files)}, ключей в эталоне: {len(reference)}')

for name, keys in keysets.items():
    missing = reference - keys
    extra = keys - reference
    if missing or extra:
        print(f'  РАСХОЖДЕНИЕ {name}: нет {len(missing)}, лишних {len(extra)}')
        failed += 1

if all(keysets[n] == reference for n in keysets):
    print('  все языки имеют одинаковый набор ключей')

# ─────────── итог ───────────

print()
print('=' * 74)
print(f'ИТОГ: успешно {passed}, ошибок {failed}')
print('=' * 74)

sys.exit(1 if failed else 0)
