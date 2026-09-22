#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Генератор файлов локализации NekoConverter.

Ключи и переводы хранятся одной таблицей: строка на ключ, значения — в порядке
языков из LOCALES. Так сразу видно, если какой-то язык отстал, и невозможно
получить файл с дырками: скрипт проверяет это перед записью.

Запуск:  python3 tools/make_locales.py
"""
import json
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..',
                   'src', 'NekoConverter.App', 'Locale')

# Порядок важен: он же порядок значений в таблице ниже.
# (код, название на своём языке, правостороннее письмо)
LOCALES = [
    ('zh-Hans', '简体中文', False),
    ('zh-Hant', '繁體中文', False),
    ('en', 'English', False),
    ('fr', 'Français', False),
    ('de', 'Deutsch', False),
    ('it', 'Italiano', False),
    ('ru', 'Русский', False),
    ('es', 'Español', False),
    ('pt', 'Português', False),
    ('ja', '日本語', False),
    ('ko', '한국어', False),
    ('pl', 'Polski', False),
    ('tr', 'Türkçe', False),
    ('ar', 'العربية', True),
    ('he', 'עברית', True),
    ('sw', 'Kiswahili', False),
]

# key: 16 значений в порядке LOCALES
T = {
# ─────────── Общее ───────────
'app.subtitle': [
    '格式转换工具箱', '格式轉換工具箱', 'Format converter', 'Convertisseur de formats',
    'Format-Konverter', 'Convertitore di formati', 'Конвертер форматов', 'Conversor de formatos',
    'Conversor de formatos', 'フォーマット変換ツール', '형식 변환 도구', 'Konwerter formatów',
    'Format dönüştürücü', 'محوّل الصيغ', 'ממיר פורמטים', 'Kibadilishaji cha miundo'],
'nav.convert': [
    '转换', '轉換', 'Convert', 'Convertir', 'Konvertieren', 'Converti', 'Преобразовать',
    'Convertir', 'Converter', '変換', '변환', 'Konwertuj', 'Dönüştür', 'تحويل', 'המרה', 'Badilisha'],
'nav.modules': [
    '模块', '模組', 'Modules', 'Modules', 'Module', 'Moduli', 'Модули', 'Módulos', 'Módulos',
    'モジュール', '모듈', 'Moduły', 'Modüller', 'الوحدات', 'מודולים', 'Moduli'],
'nav.settings': [
    '设置', '設定', 'Settings', 'Paramètres', 'Einstellungen', 'Impostazioni', 'Настройки',
    'Ajustes', 'Configurações', '設定', '설정', 'Ustawienia', 'Ayarlar', 'الإعدادات', 'הגדרות', 'Mipangilio'],
'nav.about': [
    '关于', '關於', 'About', 'À propos', 'Über', 'Informazioni', 'О программе', 'Acerca de',
    'Sobre', 'このアプリについて', '정보', 'O programie', 'Hakkında', 'حول', 'אודות', 'Kuhusu'],
'theme.light': [
    '浅色', '淺色', 'Light', 'Clair', 'Hell', 'Chiaro', 'Светлая', 'Claro', 'Claro',
    'ライト', '라이트', 'Jasny', 'Açık', 'فاتح', 'בהיר', 'Nuru'],
'theme.dark': [
    '深色', '深色', 'Dark', 'Sombre', 'Dunkel', 'Scuro', 'Тёмная', 'Oscuro', 'Escuro',
    'ダーク', '다크', 'Ciemny', 'Koyu', 'داكن', 'כהה', 'Giza'],

# ─────────── Страница «Преобразовать» ───────────
'convert.title': [
    '转换', '轉換', 'Convert', 'Convertir', 'Konvertieren', 'Converti', 'Преобразование',
    'Convertir', 'Converter', '変換', '변환', 'Konwersja', 'Dönüştürme', 'تحويل', 'המרה', 'Badilisha'],
'drop.select_or_drop': [
    '请选择或拖入需要的文件', '請選擇或拖入需要的檔案', 'Choose or drop a file',
    'Choisissez ou déposez un fichier', 'Datei auswählen oder ablegen',
    'Scegli o trascina un file', 'Выберите или перетащите файл',
    'Elige o arrastra un archivo', 'Escolha ou arraste um arquivo',
    'ファイルを選択またはドロップ', '파일을 선택하거나 끌어다 놓으세요', 'Wybierz lub przeciągnij plik',
    'Dosya seçin veya sürükleyin', 'اختر ملفًا أو أفلته', 'בחרו או גררו קובץ', 'Chagua au buruta faili'],
'drop.select_only': [
    '请选择需要的文件', '請選擇需要的檔案', 'Choose a file', 'Choisissez un fichier',
    'Datei auswählen', 'Scegli un file', 'Выберите файл', 'Elige un archivo', 'Escolha um arquivo',
    'ファイルを選択', '파일을 선택하세요', 'Wybierz plik', 'Dosya seçin', 'اختر ملفًا', 'בחרו קובץ', 'Chagua faili'],
'drop.release': [
    '松开即可载入', '放開即可載入', 'Release to load', 'Relâchez pour charger',
    'Loslassen zum Laden', 'Rilascia per caricare', 'Отпустите, чтобы загрузить',
    'Suelta para cargar', 'Solte para carregar', '離して読み込み', '놓으면 불러옵니다',
    'Puść, aby wczytać', 'Yüklemek için bırakın', 'أفلت للتحميل', 'שחררו כדי לטעון', 'Achia ili kupakia'],
'drop.formats_hint': [
    '开箱支持 {0} 种格式：图片、字幕、结构化数据、无损音频、DOCX',
    '開箱支援 {0} 種格式：圖片、字幕、結構化資料、無損音訊、DOCX',
    '{0} formats out of the box: images, subtitles, structured data, lossless audio, DOCX',
    '{0} formats pris en charge : images, sous-titres, données structurées, audio sans perte, DOCX',
    '{0} Formate sofort verfügbar: Bilder, Untertitel, strukturierte Daten, verlustfreies Audio, DOCX',
    '{0} formati subito disponibili: immagini, sottotitoli, dati strutturati, audio senza perdita, DOCX',
    'Доступно сразу {0} форматов: изображения, субтитры, структурированные данные, звук без потерь, DOCX',
    '{0} formatos disponibles: imágenes, subtítulos, datos estructurados, audio sin pérdida, DOCX',
    '{0} formatos disponíveis: imagens, legendas, dados estruturados, áudio sem perdas, DOCX',
    'すぐに使える形式は {0} 種類：画像、字幕、構造化データ、可逆音声、DOCX',
    '기본 지원 형식 {0}개: 이미지, 자막, 구조화 데이터, 무손실 오디오, DOCX',
    'Dostępnych od razu {0} formatów: obrazy, napisy, dane strukturalne, dźwięk bezstratny, DOCX',
    'Kutudan {0} biçim: görseller, altyazılar, yapılandırılmış veriler, kayıpsız ses, DOCX',
    '{0} صيغة جاهزة: الصور والترجمات والبيانات المنظمة والصوت غير المضغوط وDOCX',
    '{0} פורמטים זמינים: תמונות, כתוביות, נתונים מובנים, שמע ללא אובדן, DOCX',
    'Miundo {0} tayari: picha, manukuu, data iliyopangwa, sauti bila upotevu, DOCX'],
'pick.button': [
    '选择文件…', '選擇檔案…', 'Choose file…', 'Choisir un fichier…', 'Datei auswählen…',
    'Scegli file…', 'Выбрать файл…', 'Elegir archivo…', 'Escolher arquivo…',
    'ファイルを選択…', '파일 선택…', 'Wybierz plik…', 'Dosya seç…', 'اختر ملفًا…', 'בחרו קובץ…', 'Chagua faili…'],
'pick.title': [
    '选择文件', '選擇檔案', 'Choose a file', 'Choisir un fichier', 'Datei auswählen',
    'Scegli un file', 'Выбор файла', 'Elegir archivo', 'Escolher arquivo',
    'ファイルの選択', '파일 선택', 'Wybierz plik', 'Dosya seç', 'اختر ملفًا', 'בחירת קובץ', 'Chagua faili'],
'pick.supported': [
    '支持的文件（{0} 种）', '支援的檔案（{0} 種）', 'Supported files ({0})',
    'Fichiers pris en charge ({0})', 'Unterstützte Dateien ({0})', 'File supportati ({0})',
    'Поддерживаемые файлы ({0})', 'Archivos compatibles ({0})', 'Arquivos compatíveis ({0})',
    '対応ファイル（{0}）', '지원 파일 ({0})', 'Obsługiwane pliki ({0})', 'Desteklenen dosyalar ({0})',
    'الملفات المدعومة ({0})', 'קבצים נתמכים ({0})', 'Faili zinazotumika ({0})'],
'pick.all': [
    '所有文件', '所有檔案', 'All files', 'Tous les fichiers', 'Alle Dateien', 'Tutti i file',
    'Все файлы', 'Todos los archivos', 'Todos os arquivos', 'すべてのファイル', '모든 파일',
    'Wszystkie pliki', 'Tüm dosyalar', 'كل الملفات', 'כל הקבצים', 'Faili zote'],
'file.change': [
    '更换', '更換', 'Change', 'Changer', 'Ändern', 'Cambia', 'Заменить', 'Cambiar', 'Trocar',
    '変更', '변경', 'Zmień', 'Değiştir', 'تغيير', 'החלפה', 'Badilisha'],
'file.unknown': [
    '{0} · 无法识别扩展名', '{0} · 無法辨識副檔名', '{0} · unrecognized extension',
    '{0} · extension non reconnue', '{0} · unbekannte Erweiterung', '{0} · estensione non riconosciuta',
    '{0} · неизвестное расширение', '{0} · extensión desconocida', '{0} · extensão desconhecida',
    '{0} · 不明な拡張子', '{0} · 알 수 없는 확장자', '{0} · nierozpoznane rozszerzenie',
    '{0} · tanınmayan uzantı', '{0} · امتداد غير معروف', '{0} · סיומת לא מזוהה', '{0} · kiendelezi kisichojulikana'],
'file.needs_module': [
    '需要模块', '需要模組', 'needs a module', 'nécessite un module', 'Modul erforderlich',
    'richiede un modulo', 'нужен модуль', 'requiere un módulo', 'requer um módulo',
    'モジュールが必要', '모듈 필요', 'wymaga modułu', 'modül gerekli', 'يحتاج وحدة',
    'דורש מודול', 'inahitaji moduli'],
'target.title': [
    '目标格式', '目標格式', 'Target format', 'Format cible', 'Zielformat', 'Formato di destinazione',
    'Целевой формат', 'Formato de destino', 'Formato de destino', '出力形式', '대상 형식',
    'Format docelowy', 'Hedef biçim', 'الصيغة الهدف', 'פורמט יעד', 'Muundo lengwa'],
'quality.title': [
    '质量', '品質', 'Quality', 'Qualité', 'Qualität', 'Qualità', 'Качество', 'Calidad',
    'Qualidade', '品質', '품질', 'Jakość', 'Kalite', 'الجودة', 'איכות', 'Ubora'],
'quality.highest': [
    '极高', '極高', 'Highest', 'Maximale', 'Höchste', 'Massima', 'Максимальное', 'Máxima',
    'Máxima', '最高', '최고', 'Najwyższa', 'En yüksek', 'الأعلى', 'הגבוהה ביותר', 'Juu sana'],
'quality.high': [
    '高', '高', 'High', 'Élevée', 'Hoch', 'Alta', 'Высокое', 'Alta', 'Alta',
    '高', '높음', 'Wysoka', 'Yüksek', 'عالية', 'גבוהה', 'Juu'],
'quality.medium': [
    '中', '中', 'Medium', 'Moyenne', 'Mittel', 'Media', 'Среднее', 'Media', 'Média',
    '中', '보통', 'Średnia', 'Orta', 'متوسطة', 'בינונית', 'Kati'],
'quality.small': [
    '小体积', '小體積', 'Small', 'Petite', 'Klein', 'Piccola', 'Малый размер', 'Pequeña',
    'Pequena', '小サイズ', '작은 크기', 'Mały rozmiar', 'Küçük boyut', 'حجم صغير', 'קטן', 'Ndogo'],
'quality.smallest': [
    '最小体积', '最小體積', 'Smallest', 'Minimale', 'Minimal', 'Minima', 'Минимальный размер',
    'Mínima', 'Mínima', '最小サイズ', '최소 크기', 'Najmniejszy rozmiar', 'En küçük boyut',
    'أصغر حجم', 'הקטן ביותר', 'Ndogo zaidi'],
'expert.collapsed': [
    '高级设置', '進階設定', 'Advanced', 'Avancé', 'Erweitert', 'Avanzate', 'Дополнительно',
    'Avanzado', 'Avançado', '詳細設定', '고급 설정', 'Zaawansowane', 'Gelişmiş',
    'إعدادات متقدمة', 'מתקדם', 'Mipangilio ya juu'],
'expert.expanded': [
    '高级设置', '進階設定', 'Advanced', 'Avancé', 'Erweitert', 'Avanzate', 'Дополнительно',
    'Avanzado', 'Avançado', '詳細設定', '고급 설정', 'Zaawansowane', 'Gelişmiş',
    'إعدادات متقدمة', 'מתקדם', 'Mipangilio ya juu'],
'audio.title': [
    '音频参数', '音訊參數', 'Audio settings', 'Paramètres audio', 'Audio-Einstellungen',
    'Impostazioni audio', 'Параметры звука', 'Ajustes de audio', 'Configurações de áudio',
    '音声設定', '오디오 설정', 'Ustawienia dźwięku', 'Ses ayarları',
    'إعدادات الصوت', 'הגדרות שמע', 'Mipangilio ya sauti'],
'audio.sample_rate': [
    '采样率', '取樣率', 'Sample rate', 'Fréquence', 'Abtastrate', 'Frequenza',
    'Частота дискретизации', 'Frecuencia', 'Taxa de amostragem', 'サンプルレート', '샘플 레이트',
    'Częstotliwość', 'Örnekleme hızı', 'معدل العينات', 'קצב דגימה', 'Kiwango cha sampuli'],
'audio.channels': [
    '声道', '聲道', 'Channels', 'Canaux', 'Kanäle', 'Canali', 'Каналы', 'Canales', 'Canais',
    'チャンネル', '채널', 'Kanały', 'Kanallar', 'القنوات', 'ערוצים', 'Njia'],
'audio.bits': [
    '位深', '位元深度', 'Bit depth', 'Profondeur', 'Bittiefe', 'Profondità di bit',
    'Разрядность', 'Profundidad', 'Profundidade', 'ビット深度', '비트 심도', 'Głębokość bitowa',
    'Bit derinliği', 'عمق البت', 'עומק סיביות', 'Kina cha biti'],
'audio.keep': [
    '保持原始', '保持原始', 'Keep original', 'Conserver', 'Beibehalten', 'Mantieni originale',
    'Как в источнике', 'Mantener original', 'Manter original', '元のまま', '원본 유지',
    'Zachowaj oryginał', 'Özgün kalsın', 'الإبقاء على الأصل', 'לשמור על המקור', 'Weka asili'],
'audio.mono': [
    '单声道', '單聲道', 'Mono', 'Mono', 'Mono', 'Mono', 'Моно', 'Mono', 'Mono',
    'モノラル', '모노', 'Mono', 'Mono', 'أحادي', 'מונו', 'Mono'],
'audio.stereo': [
    '立体声', '立體聲', 'Stereo', 'Stéréo', 'Stereo', 'Stereo', 'Стерео', 'Estéreo', 'Estéreo',
    'ステレオ', '스테레오', 'Stereo', 'Stereo', 'ستيريو', 'סטריאו', 'Stereo'],
'audio.hint': [
    '采样率转换使用线性插值：日常够用，但不是录音棚级别。',
    '取樣率轉換使用線性插值：日常夠用，但不是錄音室等級。',
    'Sample rate conversion uses linear interpolation: fine for everyday use, not studio grade.',
    'La conversion de fréquence utilise une interpolation linéaire : correcte au quotidien, pas de niveau studio.',
    'Die Abtastratenkonvertierung nutzt lineare Interpolation: für den Alltag ausreichend, nicht studioqualität.',
    'La conversione della frequenza usa interpolazione lineare: adatta all’uso quotidiano, non da studio.',
    'Частота дискретизации меняется линейной интерполяцией: для повседневного использования годится, студийного качества не даёт.',
    'La conversión de frecuencia usa interpolación lineal: suficiente para el día a día, no nivel de estudio.',
    'A conversão de taxa usa interpolação linear: suficiente no dia a dia, não é nível de estúdio.',
    'サンプルレート変換は線形補間です。日常用途には十分ですが、スタジオ品質ではありません。',
    '샘플 레이트 변환은 선형 보간입니다. 일상용으로는 충분하지만 스튜디오 품질은 아닙니다.',
    'Konwersja częstotliwości używa interpolacji liniowej: wystarcza na co dzień, ale to nie jakość studyjna.',
    'Örnekleme hızı dönüşümü doğrusal ara değerleme kullanır: günlük kullanım için yeterli, stüdyo kalitesi değil.',
    'تحويل معدل العينات يستخدم استيفاءً خطيًا: كافٍ للاستخدام اليومي وليس بجودة الاستوديو.',
    'המרת קצב דגימה משתמשת באינטרפולציה לינארית: מספיק לשימוש יומיומי, לא באיכות אולפן.',
    'Ubadilishaji wa kiwango cha sampuli hutumia ukalimani wa mstari: unafaa kwa kila siku, si kiwango cha studio.'],
'subtitle.title': [
    '字幕参数', '字幕參數', 'Subtitle settings', 'Paramètres des sous-titres', 'Untertitel-Einstellungen',
    'Impostazioni sottotitoli', 'Параметры субтитров', 'Ajustes de subtítulos', 'Configurações de legendas',
    '字幕設定', '자막 설정', 'Ustawienia napisów', 'Altyazı ayarları',
    'إعدادات الترجمة', 'הגדרות כתוביות', 'Mipangilio ya manukuu'],
'subtitle.fps': [
    '帧率', '影格率', 'Frame rate', 'Images par seconde', 'Bildrate', 'Frequenza fotogrammi',
    'Частота кадров', 'Fotogramas por segundo', 'Taxa de quadros', 'フレームレート', '프레임 레이트',
    'Liczba klatek', 'Kare hızı', 'معدل الإطارات', 'קצב פריימים', 'Kiwango cha fremu'],
'subtitle.hint': [
    '帧率只对 MicroDVD (.sub) 有效：这个格式用帧号记时间，不用秒。',
    '影格率只對 MicroDVD (.sub) 有效：這個格式用影格編號記時間，不用秒。',
    'Frame rate matters only for MicroDVD (.sub): that format stores time as frame numbers, not seconds.',
    'La cadence ne concerne que MicroDVD (.sub) : ce format stocke le temps en numéros d’images, pas en secondes.',
    'Die Bildrate betrifft nur MicroDVD (.sub): Dieses Format speichert Zeit als Bildnummern, nicht in Sekunden.',
    'La frequenza riguarda solo MicroDVD (.sub): quel formato memorizza il tempo in numeri di fotogramma, non in secondi.',
    'Частота кадров важна только для MicroDVD (.sub): этот формат хранит время в номерах кадров, а не в секундах.',
    'La cadencia solo afecta a MicroDVD (.sub): ese formato guarda el tiempo en números de fotograma, no en segundos.',
    'A taxa só importa para MicroDVD (.sub): esse formato guarda o tempo em números de quadro, não em segundos.',
    'フレームレートは MicroDVD (.sub) でのみ意味があります。この形式は秒ではなくフレーム番号で時間を記録します。',
    '프레임 레이트는 MicroDVD(.sub)에만 해당합니다. 이 형식은 시간을 초가 아닌 프레임 번호로 저장합니다.',
    'Liczba klatek ma znaczenie tylko dla MicroDVD (.sub): ten format zapisuje czas jako numery klatek, nie sekundy.',
    'Kare hızı yalnızca MicroDVD (.sub) için geçerlidir: bu biçim zamanı saniye değil kare numarası olarak saklar.',
    'معدل الإطارات يهم فقط مع MicroDVD ‏(.sub): فهذه الصيغة تخزّن الوقت بأرقام الإطارات لا بالثواني.',
    'קצב פריימים רלוונטי רק ל-MicroDVD‏ (.sub): הפורמט הזה שומר זמן כמספרי פריימים ולא כשניות.',
    'Kiwango cha fremu kinahusu MicroDVD (.sub) pekee: muundo huo huhifadhi muda kwa namba za fremu, si sekunde.'],
'image.title': [
    '图像参数', '圖片參數', 'Image settings', 'Paramètres d’image', 'Bild-Einstellungen',
    'Impostazioni immagine', 'Параметры изображения', 'Ajustes de imagen', 'Configurações de imagem',
    '画像設定', '이미지 설정', 'Ustawienia obrazu', 'Görsel ayarları',
    'إعدادات الصورة', 'הגדרות תמונה', 'Mipangilio ya picha'],
'image.max_dimension': [
    '最长边', '最長邊', 'Longest side', 'Côté le plus long', 'Längste Seite', 'Lato più lungo',
    'Длинная сторона', 'Lado mayor', 'Lado maior', '長辺', '긴 변', 'Najdłuższy bok',
    'En uzun kenar', 'الضلع الأطول', 'הצלע הארוכה', 'Upande mrefu'],
'image.hint': [
    '保持原始尺寸时不做任何缩放，直接重新编码。',
    '保持原始尺寸時不做任何縮放，直接重新編碼。',
    'Keeping the original size means no scaling at all — just re-encoding.',
    'Conserver la taille d’origine signifie aucun redimensionnement, seulement un ré-encodage.',
    'Originalgröße beibehalten heißt: keine Skalierung, nur Neukodierung.',
    'Mantenere la dimensione originale significa nessun ridimensionamento, solo ricodifica.',
    '«Как в источнике» — масштабирования нет, только перекодирование.',
    'Mantener el tamaño original significa no escalar, solo recodificar.',
    'Manter o tamanho original significa não redimensionar, apenas recodificar.',
    '元のサイズを保つ場合、拡大縮小は行わず再エンコードのみを行います。',
    '원본 크기를 유지하면 크기 조절 없이 다시 인코딩만 합니다.',
    'Zachowanie oryginalnego rozmiaru oznacza brak skalowania — tylko ponowne kodowanie.',
    'Özgün boyutu korumak ölçekleme yapmamak, yalnızca yeniden kodlamak demektir.',
    'الإبقاء على الحجم الأصلي يعني عدم القياس إطلاقًا، وإعادة الترميز فقط.',
    'שמירה על הגודל המקורי משמעה ללא שינוי גודל — רק קידוד מחדש.',
    'Kubaki ukubwa asili humaanisha hakuna kupima, ni kusimba upya pekee.'],
'convert.button': [
    '开始转换', '開始轉換', 'Convert', 'Convertir', 'Konvertieren', 'Converti', 'Преобразовать',
    'Convertir', 'Converter', '変換する', '변환 시작', 'Konwertuj', 'Dönüştür',
    'ابدأ التحويل', 'המר', 'Badilisha'],

# ─────────── Состояния ───────────
'status.file_selected': [
    '已选择文件。确认目标格式后点击「开始转换」。',
    '已選擇檔案。確認目標格式後點擊「開始轉換」。',
    'File selected. Check the target format, then press Convert.',
    'Fichier sélectionné. Vérifiez le format cible puis lancez la conversion.',
    'Datei ausgewählt. Zielformat prüfen und dann konvertieren.',
    'File selezionato. Controlla il formato di destinazione e avvia la conversione.',
    'Файл выбран. Проверьте целевой формат и нажмите «Преобразовать».',
    'Archivo seleccionado. Revisa el formato de destino y pulsa Convertir.',
    'Arquivo selecionado. Confira o formato de destino e clique em Converter.',
    'ファイルを選択しました。出力形式を確認して「変換する」を押してください。',
    '파일을 선택했습니다. 대상 형식을 확인하고 변환을 누르세요.',
    'Plik wybrany. Sprawdź format docelowy i naciśnij Konwertuj.',
    'Dosya seçildi. Hedef biçimi kontrol edip Dönüştür’e basın.',
    'تم اختيار الملف. تحقق من الصيغة الهدف ثم اضغط ابدأ التحويل.',
    'הקובץ נבחר. בדקו את פורמט היעד ולחצו המר.',
    'Faili imechaguliwa. Angalia muundo lengwa kisha ubonyeze Badilisha.'],
'status.converting': [
    '正在转换：{0} → {1} …', '正在轉換：{0} → {1} …', 'Converting: {0} → {1}…',
    'Conversion : {0} → {1}…', 'Konvertiere: {0} → {1}…', 'Conversione: {0} → {1}…',
    'Преобразование: {0} → {1}…', 'Convirtiendo: {0} → {1}…', 'Convertendo: {0} → {1}…',
    '変換中：{0} → {1}…', '변환 중: {0} → {1}…', 'Konwersja: {0} → {1}…',
    'Dönüştürülüyor: {0} → {1}…', 'جارٍ التحويل: {0} → {1}…', 'ממיר: {0} → {1}…',
    'Inabadilisha: {0} → {1}…'],
'status.done': [
    '完成：{0}', '完成：{0}', 'Done: {0}', 'Terminé : {0}', 'Fertig: {0}', 'Completato: {0}',
    'Готово: {0}', 'Listo: {0}', 'Concluído: {0}', '完了：{0}', '완료: {0}',
    'Gotowe: {0}', 'Tamamlandı: {0}', 'تم: {0}', 'הושלם: {0}', 'Imekamilika: {0}'],
'status.details': [
    '体积 {0} 字节 · 耗时 {1} 毫秒 · 引擎 {2}',
    '大小 {0} 位元組 · 耗時 {1} 毫秒 · 引擎 {2}',
    '{0} bytes · {1} ms · engine {2}',
    '{0} octets · {1} ms · moteur {2}',
    '{0} Bytes · {1} ms · Engine {2}',
    '{0} byte · {1} ms · motore {2}',
    '{0} байт · {1} мс · движок {2}',
    '{0} bytes · {1} ms · motor {2}',
    '{0} bytes · {1} ms · motor {2}',
    '{0} バイト · {1} ミリ秒 · エンジン {2}',
    '{0} 바이트 · {1} ms · 엔진 {2}',
    '{0} bajtów · {1} ms · silnik {2}',
    '{0} bayt · {1} ms · motor {2}',
    '{0} بايت · {1} ملم ث · المحرّك {2}',
    '{0} בייט · {1} אלפיות שנייה · מנוע {2}',
    'Baiti {0} · {1} ms · injini {2}'],
'status.failed': [
    '失败：{0}', '失敗：{0}', 'Failed: {0}', 'Échec : {0}', 'Fehlgeschlagen: {0}',
    'Non riuscito: {0}', 'Ошибка: {0}', 'Error: {0}', 'Falhou: {0}',
    '失敗：{0}', '실패: {0}', 'Niepowodzenie: {0}', 'Başarısız: {0}',
    'فشل: {0}', 'נכשל: {0}', 'Imeshindwa: {0}'],
'status.failed_hint': [
    '文件未被修改。如需反馈问题，请附上这个文件。',
    '檔案未被修改。如需回報問題，請附上這個檔案。',
    'The file was not modified. When reporting a problem, please attach it.',
    'Le fichier n’a pas été modifié. Pour signaler un problème, joignez-le.',
    'Die Datei wurde nicht verändert. Bitte bei einer Fehlermeldung anhängen.',
    'Il file non è stato modificato. Per segnalare un problema, allegalo.',
    'Файл не изменён. При сообщении о проблеме приложите его.',
    'El archivo no se modificó. Adjúntalo si informas de un problema.',
    'O arquivo não foi modificado. Anexe-o ao relatar um problema.',
    'ファイルは変更されていません。問題を報告する際は添付してください。',
    '파일은 수정되지 않았습니다. 문제를 알릴 때 첨부해 주세요.',
    'Plik nie został zmieniony. Do zgłoszenia problemu dołącz go.',
    'Dosya değiştirilmedi. Sorun bildirirken ekleyin.',
    'لم يُعدَّل الملف. أرفقه عند الإبلاغ عن مشكلة.',
    'הקובץ לא שונה. לצורך דיווח על תקלה, צרפו אותו.',
    'Faili halikubadilishwa. Kiambatanishe unaporipoti tatizo.'],
'status.multi_drop': [
    '已载入第一个文件（本次拖入 {0} 个，批处理将在后续版本支持）。',
    '已載入第一個檔案（本次拖入 {0} 個，批次處理將於後續版本支援）。',
    'Loaded the first file ({0} were dropped; batch processing is planned).',
    'Premier fichier chargé ({0} déposés ; le traitement par lot est prévu).',
    'Erste Datei geladen ({0} abgelegt; Stapelverarbeitung ist geplant).',
    'Caricato il primo file ({0} trascinati; l’elaborazione in blocco è prevista).',
    'Загружен первый файл (перетащено {0}; пакетная обработка запланирована).',
    'Cargado el primer archivo ({0} arrastrados; el procesamiento por lotes está previsto).',
    'Primeiro arquivo carregado ({0} arrastados; o processamento em lote está previsto).',
    '最初のファイルを読み込みました（{0} 個ドロップ。バッチ処理は今後対応予定）。',
    '첫 번째 파일을 불러왔습니다 ({0}개를 놓았습니다. 일괄 처리는 예정).',
    'Wczytano pierwszy plik (przeciągnięto {0}; przetwarzanie wsadowe w planach).',
    'İlk dosya yüklendi ({0} bırakıldı; toplu işlem planlanıyor).',
    'تم تحميل الملف الأول (أُفلت {0}؛ المعالجة الدفعية مخططة).',
    'הקובץ הראשון נטען ({0} נגררו; עיבוד באצווה מתוכנן).',
    'Faili la kwanza limepakiwa ({0} ziliburrutwa; usindikaji wa kundi umepangwa).'],
'error.no_file': [
    '请先选择文件。', '請先選擇檔案。', 'Choose a file first.', 'Choisissez d’abord un fichier.',
    'Bitte zuerst eine Datei auswählen.', 'Scegli prima un file.', 'Сначала выберите файл.',
    'Elige primero un archivo.', 'Escolha primeiro um arquivo.',
    '先にファイルを選択してください。', '먼저 파일을 선택하세요.',
    'Najpierw wybierz plik.', 'Önce bir dosya seçin.',
    'اختر ملفًا أولًا.', 'בחרו קובץ תחילה.', 'Chagua faili kwanza.'],
'error.no_target': [
    '请先选择目标格式。', '請先選擇目標格式。', 'Choose a target format first.',
    'Choisissez d’abord un format cible.', 'Bitte zuerst ein Zielformat wählen.',
    'Scegli prima un formato di destinazione.', 'Сначала выберите целевой формат.',
    'Elige primero un formato de destino.', 'Escolha primeiro um formato de destino.',
    '先に出力形式を選択してください。', '먼저 대상 형식을 선택하세요.',
    'Najpierw wybierz format docelowy.', 'Önce bir hedef biçim seçin.',
    'اختر صيغة هدف أولًا.', 'בחרו פורמט יעד תחילה.', 'Chagua muundo lengwa kwanza.'],
'error.needs_module': [
    '「{0}」需要安装模块（{1}）才能使用。',
    '「{0}」需要安裝模組（{1}）才能使用。',
    '“{0}” needs the “{1}” module.',
    '« {0} » nécessite le module « {1} ».',
    '„{0}“ benötigt das Modul „{1}“.',
    '«{0}» richiede il modulo «{1}».',
    '«{0}» требует модуля «{1}».',
    '«{0}» requiere el módulo «{1}».',
    '«{0}» requer o módulo «{1}».',
    '「{0}」にはモジュール「{1}」が必要です。',
    '"{0}"에는 "{1}" 모듈이 필요합니다.',
    '„{0}” wymaga modułu „{1}”.',
    '"{0}" için "{1}" modülü gerekli.',
    '«{0}» يحتاج إلى الوحدة «{1}».',
    '"{0}" דורש את המודול "{1}".',
    '"{0}" inahitaji moduli ya "{1}".'],

# ─────────── Страница «Модули» ───────────
'modules.title': [
    '模块与依赖', '模組與相依項目', 'Modules and dependencies', 'Modules et dépendances',
    'Module und Abhängigkeiten', 'Moduli e dipendenze', 'Модули и зависимости',
    'Módulos y dependencias', 'Módulos e dependências', 'モジュールと依存関係',
    '모듈과 종속성', 'Moduły i zależności', 'Modüller ve bağımlılıklar',
    'الوحدات والاعتماديات', 'מודולים ותלויות', 'Moduli na vitegemezi'],
'modules.summary': [
    '共 {0} 种格式，当前可用 {1} 种。包目录中有 {2} 个包。',
    '共 {0} 種格式，目前可用 {1} 種。套件目錄中有 {2} 個套件。',
    '{0} formats total, {1} available now. {2} package(s) in the catalog.',
    '{0} formats au total, {1} disponibles. {2} paquet(s) dans le catalogue.',
    '{0} Formate insgesamt, {1} verfügbar. {2} Paket(e) im Katalog.',
    '{0} formati in totale, {1} disponibili. {2} pacchetti nel catalogo.',
    'Всего форматов {0}, доступно сейчас {1}. Пакетов в каталоге: {2}.',
    '{0} formatos en total, {1} disponibles. {2} paquete(s) en el catálogo.',
    '{0} formatos no total, {1} disponíveis. {2} pacote(s) no catálogo.',
    '全 {0} 形式中 {1} 形式が利用可能。カタログに {2} 個のパッケージ。',
    '전체 {0}개 형식 중 {1}개 사용 가능. 카탈로그에 패키지 {2}개.',
    '{0} formatów, dostępnych {1}. W katalogu {2} pakiet(ów).',
    'Toplam {0} biçim, {1} kullanılabilir. Katalogda {2} paket.',
    'إجمالي {0} صيغة، المتاح الآن {1}. في الكتالوج {2} حزمة.',
    'סה״כ {0} פורמטים, זמינים {1}. בקטלוג {2} חבילות.',
    'Jumla ya miundo {0}, inayopatikana {1}. Katika katalogi {2} pakiti.'],
'modules.dep_hint': [
    '依赖是原版第三方程序（如 FFmpeg），装一次长期保留，不会跟着格式包一起卸载。应用本体不包含它们，所以体积保持轻量。',
    '相依項目是原版第三方程式（如 FFmpeg），裝一次長期保留，不會跟著格式套件一起卸載。應用本體不含它們，所以體積保持輕量。',
    'Dependencies are the original third-party programs (such as FFmpeg). They are installed once and kept; the app itself stays small because it does not include them.',
    'Les dépendances sont les programmes tiers d’origine (comme FFmpeg). Installées une fois, elles sont conservées ; l’application reste légère car elle ne les inclut pas.',
    'Abhängigkeiten sind die Original-Programme Dritter (etwa FFmpeg). Einmal installiert bleiben sie erhalten; die App selbst bleibt klein.',
    'Le dipendenze sono i programmi originali di terze parti (come FFmpeg). Installate una volta restano; l’app resta leggera perché non le include.',
    'Зависимости — это оригинальные сторонние программы (например FFmpeg). Ставятся один раз и остаются; приложение остаётся лёгким, потому что не содержит их.',
    'Las dependencias son los programas originales de terceros (como FFmpeg). Se instalan una vez y se conservan; la app sigue siendo ligera.',
    'As dependências são os programas originais de terceiros (como o FFmpeg). Instaladas uma vez, permanecem; o app continua leve.',
    '依存関係とはオリジナルのサードパーティ製プログラム（FFmpeg など）です。一度インストールすると保持され、アプリ本体は軽いままです。',
    '종속성은 원본 서드파티 프로그램(FFmpeg 등)입니다. 한 번 설치하면 유지되며, 앱 본체는 가볍게 유지됩니다.',
    'Zależności to oryginalne programy firm trzecich (np. FFmpeg). Instalowane raz, pozostają; aplikacja pozostaje lekka.',
    'Bağımlılıklar orijinal üçüncü taraf programlardır (FFmpeg gibi). Bir kez kurulur ve kalır; uygulama hafif kalır.',
    'الاعتماديات هي البرامج الأصلية من أطراف ثالثة (مثل FFmpeg). تُثبَّت مرة واحدة وتبقى؛ ويظل التطبيق خفيفًا.',
    'התלויות הן התוכנות המקוריות של צד שלישי (כמו FFmpeg). מותקנות פעם אחת ונשארות; האפליקציה נשארת קלה.',
    'Vitegemezi ni programu asili za watu wengine (kama FFmpeg). Huwekwa mara moja na kubaki; programu yenyewe hubaki nyepesi.'],
'modules.offline_hint': [
    '如果使用「离线包」版本，依赖已经放在应用旁边的 deps 文件夹里：无需下载，直接可用，也无需安装。',
    '若使用「離線包」版本，相依項目已放在應用程式旁的 deps 資料夾：無需下載，直接可用，也無需安裝。',
    'With the offline build, dependencies already sit in the deps folder next to the app: nothing to download, nothing to install.',
    'Avec la version hors ligne, les dépendances sont déjà dans le dossier deps à côté de l’application : rien à télécharger ni à installer.',
    'In der Offline-Version liegen die Abhängigkeiten bereits im Ordner deps neben der App: nichts herunterladen, nichts installieren.',
    'Con la versione offline le dipendenze sono già nella cartella deps accanto all’app: niente da scaricare né installare.',
    'В офлайн-сборке зависимости уже лежат в папке deps рядом с приложением: качать и устанавливать ничего не нужно.',
    'Con la versión sin conexión, las dependencias ya están en la carpeta deps junto a la app: nada que descargar ni instalar.',
    'Na versão offline, as dependências já estão na pasta deps ao lado do app: nada para baixar nem instalar.',
    'オフライン版では、依存関係はアプリの隣の deps フォルダにあります。ダウンロードもインストールも不要です。',
    '오프라인 빌드에서는 종속성이 앱 옆 deps 폴더에 있습니다. 다운로드도 설치도 필요 없습니다.',
    'W wersji offline zależności są już w folderze deps obok aplikacji: nic nie trzeba pobierać ani instalować.',
    'Çevrimdışı sürümde bağımlılıklar uygulamanın yanındaki deps klasöründedir: indirmeye ve kurmaya gerek yok.',
    'في النسخة دون اتصال، الاعتماديات موجودة في مجلد deps بجانب التطبيق: لا تحميل ولا تثبيت.',
    'בגרסת האופליין התלויות כבר נמצאות בתיקיית deps שליד האפליקציה: אין מה להוריד או להתקין.',
    'Katika toleo la nje ya mtandao, vitegemezi viko kwenye folda deps karibu na programu: hakuna cha kupakua wala kuweka.'],
'modules.empty_title': [
    '包目录为空', '套件目錄為空', 'Catalog is empty', 'Catalogue vide', 'Katalog ist leer',
    'Catalogo vuoto', 'Каталог пуст', 'El catálogo está vacío', 'O catálogo está vazio',
    'カタログが空です', '카탈로그가 비어 있습니다', 'Katalog jest pusty', 'Katalog boş',
    'الكتالوج فارغ', 'הקטלוג ריק', 'Katalogi ni tupu'],
'modules.empty_body': [
    '应用旁边没有 catalog.json，或者里面没有适用于当前平台的包。',
    '應用程式旁沒有 catalog.json，或裡面沒有適用於目前平台的套件。',
    'There is no catalog.json next to the app, or it lists no packages for this platform.',
    'Aucun catalog.json à côté de l’application, ou aucun paquet pour cette plateforme.',
    'Neben der App liegt keine catalog.json, oder sie enthält keine Pakete für diese Plattform.',
    'Non c’è catalog.json accanto all’app, oppure non elenca pacchetti per questa piattaforma.',
    'Рядом с приложением нет catalog.json, либо в нём нет пакетов для этой платформы.',
    'No hay catalog.json junto a la app, o no incluye paquetes para esta plataforma.',
    'Não há catalog.json ao lado do app, ou ele não lista pacotes para esta plataforma.',
    'アプリの隣に catalog.json がないか、このプラットフォーム向けのパッケージがありません。',
    '앱 옆에 catalog.json이 없거나 이 플랫폼용 패키지가 없습니다.',
    'Obok aplikacji nie ma catalog.json lub nie ma w nim pakietów dla tej platformy.',
    'Uygulamanın yanında catalog.json yok veya bu platform için paket listelenmiyor.',
    'لا يوجد catalog.json بجانب التطبيق، أو لا توجد حزم لهذه المنصة.',
    'אין catalog.json ליד האפליקציה, או שאין בו חבילות לפלטפורמה הזו.',
    'Hakuna catalog.json karibu na programu, au hakuna pakiti za jukwaa hili.'],
'pkg.install': [
    '安装', '安裝', 'Install', 'Installer', 'Installieren', 'Installa', 'Установить',
    'Instalar', 'Instalar', 'インストール', '설치', 'Zainstaluj', 'Kur', 'تثبيت', 'התקנה', 'Weka'],
'pkg.install_with_deps': [
    '安装（含依赖）', '安裝（含相依項目）', 'Install (with dependencies)',
    'Installer (avec dépendances)', 'Installieren (mit Abhängigkeiten)',
    'Installa (con dipendenze)', 'Установить (с зависимостями)',
    'Instalar (con dependencias)', 'Instalar (com dependências)',
    'インストール（依存関係を含む）', '설치(종속성 포함)', 'Zainstaluj (z zależnościami)',
    'Kur (bağımlılıklarla)', 'تثبيت (مع الاعتماديات)', 'התקנה (עם תלויות)', 'Weka (na vitegemezi)'],
'pkg.uninstall': [
    '卸载', '卸載', 'Remove', 'Supprimer', 'Entfernen', 'Rimuovi', 'Удалить',
    'Desinstalar', 'Remover', '削除', '제거', 'Usuń', 'Kaldır', 'إزالة', 'הסרה', 'Ondoa'],
'pkg.installed': [
    '已安装', '已安裝', 'Installed', 'Installé', 'Installiert', 'Installato', 'Установлено',
    'Instalado', 'Instalado', 'インストール済み', '설치됨', 'Zainstalowano', 'Kurulu',
    'مثبَّت', 'מותקן', 'Imewekwa'],
'pkg.bundled': [
    '已随包提供', '已隨包提供', 'Included', 'Inclus', 'Enthalten', 'Incluso', 'Входит в комплект',
    'Incluido', 'Incluído', '同梱', '포함됨', 'Dołączone', 'Dahil', 'مضمَّن', 'כלול', 'Imejumuishwa'],
'pkg.dep_label': [
    '依赖（装一次长期保留）', '相依項目（裝一次長期保留）', 'dependency (installed once, kept)',
    'dépendance (installée une fois, conservée)', 'Abhängigkeit (einmal installiert, bleibt)',
    'dipendenza (installata una volta, resta)', 'зависимость (ставится один раз и остаётся)',
    'dependencia (se instala una vez y se conserva)', 'dependência (instalada uma vez, permanece)',
    '依存関係（一度インストールすると保持）', '종속성(한 번 설치하면 유지)',
    'zależność (instalowana raz, pozostaje)', 'bağımlılık (bir kez kurulur, kalır)',
    'اعتمادية (تُثبَّت مرة وتبقى)', 'תלות (מותקנת פעם אחת ונשארת)', 'kitegemezi (huwekwa mara moja)'],
'pkg.dep_label_offline': [
    '依赖（离线包，无需安装）', '相依項目（離線包，無需安裝）', 'dependency (offline build, nothing to install)',
    'dépendance (version hors ligne, rien à installer)', 'Abhängigkeit (Offline-Version, nichts zu installieren)',
    'dipendenza (versione offline, niente da installare)', 'зависимость (офлайн-сборка, ставить не нужно)',
    'dependencia (versión sin conexión, nada que instalar)', 'dependência (versão offline, nada a instalar)',
    '依存関係（オフライン版、インストール不要）', '종속성(오프라인 빌드, 설치 불필요)',
    'zależność (wersja offline, nic do instalowania)', 'bağımlılık (çevrimdışı sürüm, kurulum gerekmez)',
    'اعتمادية (نسخة دون اتصال، لا تثبيت)', 'תלות (גרסת אופליין, אין צורך בהתקנה)',
    'kitegemezi (toleo la nje ya mtandao)'],
'pkg.unlocks': [
    '解锁 {0} 种格式：{1}', '解鎖 {0} 種格式：{1}', 'Unlocks {0} formats: {1}',
    'Débloque {0} formats : {1}', 'Schaltet {0} Formate frei: {1}',
    'Sblocca {0} formati: {1}', 'Открывает {0} форматов: {1}',
    'Desbloquea {0} formatos: {1}', 'Desbloqueia {0} formatos: {1}',
    '{0} 形式が使えるようになります：{1}', '{0}개 형식을 사용할 수 있습니다: {1}',
    'Odblokowuje {0} formatów: {1}', '{0} biçimin kilidini açar: {1}',
    'يفتح {0} صيغة: {1}', 'פותח {0} פורמטים: {1}', 'Hufungua miundo {0}: {1}'],
'pkg.license': [
    '许可：{0}', '授權：{0}', 'License: {0}', 'Licence : {0}', 'Lizenz: {0}', 'Licenza: {0}',
    'Лицензия: {0}', 'Licencia: {0}', 'Licença: {0}', 'ライセンス：{0}', '라이선스: {0}',
    'Licencja: {0}', 'Lisans: {0}', 'الترخيص: {0}', 'רישיון: {0}', 'Leseni: {0}'],
'pkg.source': [
    '来源：{0}', '來源：{0}', 'Source: {0}', 'Source : {0}', 'Quelle: {0}', 'Origine: {0}',
    'Источник: {0}', 'Origen: {0}', 'Origem: {0}', '入手元：{0}', '출처: {0}',
    'Źródło: {0}', 'Kaynak: {0}', 'المصدر: {0}', 'מקור: {0}', 'Chanzo: {0}'],
'pkg.disk': [
    '磁盘占用：{0}', '磁碟占用：{0}', 'On disk: {0}', 'Sur le disque : {0}', 'Auf der Platte: {0}',
    'Su disco: {0}', 'На диске: {0}', 'En disco: {0}', 'No disco: {0}',
    'ディスク使用量：{0}', '디스크 사용량: {0}', 'Na dysku: {0}', 'Diskte: {0}',
    'على القرص: {0}', 'בדיסק: {0}', 'Kwenye diski: {0}'],
'pkg.offline_size': [
    '离线包，占用 {0}', '離線包，佔用 {0}', 'Offline build, {0}', 'Version hors ligne, {0}',
    'Offline-Version, {0}', 'Versione offline, {0}', 'Офлайн-сборка, {0}',
    'Versión sin conexión, {0}', 'Versão offline, {0}', 'オフライン版、{0}',
    '오프라인 빌드, {0}', 'Wersja offline, {0}', 'Çevrimdışı sürüm, {0}',
    'نسخة دون اتصال، {0}', 'גרסת אופליין, {0}', 'Toleo la nje ya mtandao, {0}'],
'install.resolving': [
    '正在解析依赖…', '正在解析相依項目…', 'Resolving dependencies…', 'Résolution des dépendances…',
    'Abhängigkeiten werden aufgelöst…', 'Risoluzione delle dipendenze…',
    'Разбор зависимостей…', 'Resolviendo dependencias…', 'Resolvendo dependências…',
    '依存関係を解決中…', '종속성 확인 중…', 'Rozwiązywanie zależności…',
    'Bağımlılıklar çözümleniyor…', 'جارٍ تحليل الاعتماديات…', 'מאתר תלויות…', 'Inatatua vitegemezi…'],
'install.downloading': [
    '下载 {0}：{1} / {2} MB（{3}）', '下載 {0}：{1} / {2} MB（{3}）',
    'Downloading {0}: {1} / {2} MB ({3})', 'Téléchargement de {0} : {1} / {2} Mo ({3})',
    'Lade {0}: {1} / {2} MB ({3})', 'Scaricamento di {0}: {1} / {2} MB ({3})',
    'Загрузка {0}: {1} / {2} МБ ({3})', 'Descargando {0}: {1} / {2} MB ({3})',
    'Baixando {0}: {1} / {2} MB ({3})', '{0} をダウンロード中：{1} / {2} MB（{3}）',
    '{0} 다운로드 중: {1} / {2} MB ({3})', 'Pobieranie {0}: {1} / {2} MB ({3})',
    '{0} indiriliyor: {1} / {2} MB ({3})', 'جارٍ تنزيل {0}: {1} / {2} م.ب ({3})',
    'מוריד את {0}: {1} / {2} MB ({3})', 'Inapakua {0}: {1} / {2} MB ({3})'],
'install.downloading_unknown': [
    '下载 {0}：{1} MB', '下載 {0}：{1} MB', 'Downloading {0}: {1} MB',
    'Téléchargement de {0} : {1} Mo', 'Lade {0}: {1} MB', 'Scaricamento di {0}: {1} MB',
    'Загрузка {0}: {1} МБ', 'Descargando {0}: {1} MB', 'Baixando {0}: {1} MB',
    '{0} をダウンロード中：{1} MB', '{0} 다운로드 중: {1} MB', 'Pobieranie {0}: {1} MB',
    '{0} indiriliyor: {1} MB', 'جارٍ تنزيل {0}: {1} م.ب', 'מוריד את {0}: {1} MB',
    'Inapakua {0}: {1} MB'],
'install.verifying': [
    '校验完整性…', '驗證完整性…', 'Verifying…', 'Vérification…', 'Prüfe Integrität…',
    'Verifica integrità…', 'Проверка целостности…', 'Verificando integridad…',
    'Verificando integridade…', '整合性を確認中…', '무결성 확인 중…', 'Sprawdzanie integralności…',
    'Bütünlük doğrulanıyor…', 'جارٍ التحقق من السلامة…', 'מאמת שלמות…', 'Inathibitisha uhalali…'],
'install.extracting': [
    '解压…', '解壓縮…', 'Extracting…', 'Extraction…', 'Entpacke…', 'Estrazione…',
    'Распаковка…', 'Extrayendo…', 'Extraindo…', '展開中…', '압축 해제 중…',
    'Rozpakowywanie…', 'Çıkarılıyor…', 'جارٍ فك الضغط…', 'מחלץ…', 'Inafungua…'],
'install.finishing': [
    '写入安装目录…', '寫入安裝目錄…', 'Installing…', 'Installation…', 'Installiere…',
    'Installazione…', 'Установка на место…', 'Instalando…', 'Instalando…',
    'インストール中…', '설치 중…', 'Instalowanie…', 'Kuruluyor…',
    'جارٍ التثبيت…', 'מתקין…', 'Inaweka…'],
'install.done': [
    '已安装「{0}」。当前可用格式：{1} 种。',
    '已安裝「{0}」。目前可用格式：{1} 種。',
    'Installed “{0}”. {1} formats are now available.',
    '« {0} » installé. {1} formats sont disponibles.',
    '„{0}“ installiert. {1} Formate verfügbar.',
    '«{0}» installato. {1} formati disponibili.',
    '«{0}» установлен. Доступно форматов: {1}.',
    '«{0}» instalado. {1} formatos disponibles.',
    '«{0}» instalado. {1} formatos disponíveis.',
    '「{0}」をインストールしました。利用可能な形式は {1} 種類です。',
    '"{0}" 설치 완료. 사용 가능한 형식 {1}개.',
    'Zainstalowano „{0}”. Dostępnych formatów: {1}.',
    '"{0}" kuruldu. Kullanılabilir biçim: {1}.',
    'تم تثبيت «{0}». الصيغ المتاحة: {1}.',
    '"{0}" הותקן. {1} פורמטים זמינים.',
    '"{0}" imewekwa. Miundo {1} inapatikana.'],
'install.done_hint': [
    '依赖已放在用户数据目录，不会随应用更新而丢失。',
    '相依項目已放在使用者資料目錄，不會隨應用程式更新而遺失。',
    'Dependencies live in your user data folder and survive app updates.',
    'Les dépendances résident dans votre dossier utilisateur et survivent aux mises à jour.',
    'Abhängigkeiten liegen im Benutzerordner und überstehen App-Updates.',
    'Le dipendenze stanno nella cartella utente e sopravvivono agli aggiornamenti.',
    'Зависимости лежат в пользовательском каталоге и переживают обновление приложения.',
    'Las dependencias están en tu carpeta de usuario y sobreviven a las actualizaciones.',
    'As dependências ficam na pasta do usuário e sobrevivem às atualizações.',
    '依存関係はユーザーデータフォルダにあり、アプリ更新でも残ります。',
    '종속성은 사용자 데이터 폴더에 있어 앱 업데이트에도 유지됩니다.',
    'Zależności są w folderze użytkownika i przetrwają aktualizacje.',
    'Bağımlılıklar kullanıcı klasöründe kalır ve güncellemelerden etkilenmez.',
    'الاعتماديات في مجلد بيانات المستخدم وتبقى بعد تحديثات التطبيق.',
    'התלויות נמצאות בתיקיית המשתמש ושורדות עדכוני אפליקציה.',
    'Vitegemezi viko kwenye folda ya mtumiaji na hubaki baada ya masasisho.'],
'install.failed': [
    '安装失败：{0}', '安裝失敗：{0}', 'Installation failed: {0}',
    'Échec de l’installation : {0}', 'Installation fehlgeschlagen: {0}',
    'Installazione non riuscita: {0}', 'Установка не удалась: {0}',
    'Error de instalación: {0}', 'Falha na instalação: {0}',
    'インストールに失敗：{0}', '설치 실패: {0}', 'Instalacja nie powiodła się: {0}',
    'Kurulum başarısız: {0}', 'فشل التثبيت: {0}', 'ההתקנה נכשלה: {0}', 'Uwekaji umeshindwa: {0}'],
'install.failed_hint': [
    '应用本体未受影响，可以重试。',
    '應用程式本體未受影響，可以重試。',
    'The app itself is unaffected — you can retry.',
    'L’application n’est pas affectée — vous pouvez réessayer.',
    'Die App selbst ist nicht betroffen — erneut versuchen.',
    'L’app non è stata toccata — puoi riprovare.',
    'Само приложение не пострадало — можно повторить.',
    'La aplicación no se vio afectada: puedes reintentar.',
    'O app não foi afetado — pode tentar de novo.',
    'アプリ本体は影響を受けていません。再試行できます。',
    '앱 본체는 영향받지 않았습니다. 다시 시도하세요.',
    'Aplikacja nie ucierpiała — możesz spróbować ponownie.',
    'Uygulama etkilenmedi — yeniden deneyebilirsiniz.',
    'التطبيق نفسه لم يتأثر — يمكنك إعادة المحاولة.',
    'האפליקציה עצמה לא נפגעה — אפשר לנסות שוב.',
    'Programu yenyewe haikuathirika — unaweza kujaribu tena.'],
'install.uninstalled': [
    '已卸载「{0}」。', '已卸載「{0}」。', 'Removed “{0}”.', '« {0} » supprimé.',
    '„{0}“ entfernt.', '«{0}» rimosso.', '«{0}» удалён.',
    '«{0}» desinstalado.', '«{0}» removido.', '「{0}」を削除しました。',
    '"{0}" 제거 완료.', 'Usunięto „{0}”.', '"{0}" kaldırıldı.',
    'تمت إزالة «{0}».', '"{0}" הוסר.', '"{0}" imeondolewa.'],
'install.uninstall_failed': [
    '卸载失败：{0}', '卸載失敗：{0}', 'Removal failed: {0}', 'Échec de la suppression : {0}',
    'Entfernen fehlgeschlagen: {0}', 'Rimozione non riuscita: {0}', 'Удаление не удалось: {0}',
    'Error al desinstalar: {0}', 'Falha ao remover: {0}', '削除に失敗：{0}',
    '제거 실패: {0}', 'Usuwanie nie powiodło się: {0}', 'Kaldırma başarısız: {0}',
    'فشلت الإزالة: {0}', 'ההסרה נכשלה: {0}', 'Uondoaji umeshindwa: {0}'],

# ─────────── Страница «Настройки» ───────────
'settings.title': [
    '设置', '設定', 'Settings', 'Paramètres', 'Einstellungen', 'Impostazioni', 'Настройки',
    'Ajustes', 'Configurações', '設定', '설정', 'Ustawienia', 'Ayarlar',
    'الإعدادات', 'הגדרות', 'Mipangilio'],
'settings.theme': [
    '主题', '主題', 'Theme', 'Thème', 'Design', 'Tema', 'Тема', 'Tema', 'Tema',
    'テーマ', '테마', 'Motyw', 'Tema', 'المظهر', 'ערכת נושא', 'Mandhari'],
'settings.theme_light': [
    '☀  浅色', '☀  淺色', '☀  Light', '☀  Clair', '☀  Hell', '☀  Chiaro', '☀  Светлая',
    '☀  Claro', '☀  Claro', '☀  ライト', '☀  라이트', '☀  Jasny', '☀  Açık',
    '☀  فاتح', '☀  בהיר', '☀  Nuru'],
'settings.theme_dark': [
    '🌙  深色', '🌙  深色', '🌙  Dark', '🌙  Sombre', '🌙  Dunkel', '🌙  Scuro', '🌙  Тёмная',
    '🌙  Oscuro', '🌙  Escuro', '🌙  ダーク', '🌙  다크', '🌙  Ciemny', '🌙  Koyu',
    '🌙  داكن', '🌙  כהה', '🌙  Giza'],
'settings.theme_system': [
    '🖥  跟随系统', '🖥  跟隨系統', '🖥  Follow system', '🖥  Suivre le système',
    '🖥  System folgen', '🖥  Segui il sistema', '🖥  Как в системе',
    '🖥  Seguir al sistema', '🖥  Seguir o sistema', '🖥  システムに従う',
    '🖥  시스템 설정 따르기', '🖥  Jak w systemie', '🖥  Sistemi izle',
    '🖥  اتباع النظام', '🖥  לפי המערכת', '🖥  Fuata mfumo'],
'settings.theme_bytime': [
    '🕐  按时间自动', '🕐  依時間自動', '🕐  Automatic by time', '🕐  Automatique selon l’heure',
    '🕐  Automatisch nach Uhrzeit', '🕐  Automatico per orario', '🕐  По времени суток',
    '🕐  Automático por hora', '🕐  Automático por hora', '🕐  時刻で自動',
    '🕐  시간에 따라 자동', '🕐  Automatycznie wg godziny', '🕐  Saate göre otomatik',
    '🕐  تلقائي حسب الوقت', '🕐  אוטומטי לפי שעה', '🕐  Kiotomatiki kwa muda'],
'settings.theme_hint': [
    '「按时间自动」在 19:00 到次日 07:00 之间使用深色主题，其余时间使用浅色。',
    '「依時間自動」在 19:00 到隔日 07:00 之間使用深色主題，其餘時間使用淺色。',
    '“Automatic by time” uses the dark theme from 19:00 to 07:00, light otherwise.',
    '« Automatique selon l’heure » applique le thème sombre de 19 h à 7 h, clair sinon.',
    '„Automatisch nach Uhrzeit“ nutzt von 19:00 bis 07:00 das dunkle Design, sonst das helle.',
    '«Automatico per orario» usa il tema scuro dalle 19:00 alle 07:00, chiaro altrimenti.',
    '«По времени суток» включает тёмную тему с 19:00 до 07:00, в остальное время светлую.',
    '«Automático por hora» usa el tema oscuro de 19:00 a 07:00, claro el resto.',
    '«Automático por hora» usa o tema escuro das 19h às 7h, claro no restante.',
    '「時刻で自動」は 19:00〜07:00 にダーク、それ以外はライトを使います。',
    '시간에 따라 자동은 19:00~07:00에 다크, 그 외에는 라이트를 사용합니다.',
    '„Automatycznie wg godziny” używa ciemnego motywu od 19:00 do 07:00.',
    'Saate göre otomatik 19:00–07:00 arası koyu, diğer zamanlarda açık temayı kullanır.',
    '«تلقائي حسب الوقت» يستخدم المظهر الداكن من 19:00 إلى 07:00 والفاتح فيما عدا ذلك.',
    '״אוטומטי לפי שעה״ משתמש בערכה כהה בין 19:00 ל-07:00, ובהירה בשאר הזמן.',
    'Kiotomatiki kwa muda hutumia mandhari ya giza kuanzia 19:00 hadi 07:00.'],
'settings.data_dir': [
    '数据目录', '資料目錄', 'Data folder', 'Dossier de données', 'Datenordner',
    'Cartella dati', 'Каталог данных', 'Carpeta de datos', 'Pasta de dados',
    'データフォルダ', '데이터 폴더', 'Folder danych', 'Veri klasörü',
    'مجلد البيانات', 'תיקיית נתונים', 'Folda ya data'],
'settings.data_dir_hint': [
    '模块和设置都存放在这里。卸载模块只需删除对应文件夹，应用本体不受影响。',
    '模組和設定都存放在這裡。卸載模組只需刪除對應資料夾，應用程式本體不受影響。',
    'Modules and settings live here. Removing a module just deletes its folder; the app is unaffected.',
    'Modules et paramètres sont ici. Supprimer un module revient à supprimer son dossier.',
    'Module und Einstellungen liegen hier. Ein Modul zu entfernen heißt nur, seinen Ordner zu löschen.',
    'Moduli e impostazioni stanno qui. Rimuovere un modulo significa solo eliminare la sua cartella.',
    'Здесь лежат модули и настройки. Удалить модуль — значит просто удалить его папку.',
    'Aquí están los módulos y los ajustes. Desinstalar un módulo solo borra su carpeta.',
    'Módulos e configurações ficam aqui. Remover um módulo é só apagar a pasta.',
    'モジュールと設定はここにあります。モジュールの削除はフォルダを消すだけです。',
    '모듈과 설정이 여기에 있습니다. 모듈 제거는 폴더 삭제로 끝납니다.',
    'Tu są moduły i ustawienia. Usunięcie modułu to skasowanie jego folderu.',
    'Modüller ve ayarlar burada. Modülü kaldırmak klasörünü silmek demektir.',
    'الوحدات والإعدادات هنا. إزالة وحدة تعني حذف مجلدها فقط.',
    'המודולים וההגדרות כאן. הסרת מודול היא מחיקת התיקייה שלו.',
    'Moduli na mipangilio viko hapa. Kuondoa moduli ni kufuta folda yake.'],
'settings.language': [
    '界面语言', '介面語言', 'Interface language', 'Langue de l’interface', 'Sprache der Oberfläche',
    'Lingua dell’interfaccia', 'Язык интерфейса', 'Idioma de la interfaz', 'Idioma da interface',
    '表示言語', '인터페이스 언어', 'Język interfejsu', 'Arayüz dili',
    'لغة الواجهة', 'שפת הממשק', 'Lugha ya kiolesura'],

# ─────────── Страница «О программе» ───────────
'about.title': [
    '关于', '關於', 'About', 'À propos', 'Über', 'Informazioni', 'О программе',
    'Acerca de', 'Sobre', 'このアプリについて', '정보', 'O programie', 'Hakkında',
    'حول', 'אודות', 'Kuhusu'],
'about.body': [
    '轻量格式转换工具箱。核心保持精简，能力按需扩展：图像走系统 ImageIO，文本与结构化数据由自研引擎处理，重资产格式留给可选模块。',
    '輕量格式轉換工具箱。核心保持精簡，能力按需擴充：圖片走系統 ImageIO，文字與結構化資料由自研引擎處理，重資產格式留給可選模組。',
    'A lightweight format converter. The core stays small and capabilities extend on demand: images use the system ImageIO, text and structured data are handled by our own engines, and heavy formats are left to optional modules.',
    'Un convertisseur de formats léger. Le cœur reste minimal et les capacités s’étendent à la demande : les images passent par ImageIO, le texte et les données structurées par nos propres moteurs, les formats lourds par des modules optionnels.',
    'Ein schlanker Format-Konverter. Der Kern bleibt klein, Fähigkeiten kommen bei Bedarf dazu: Bilder über ImageIO, Text und strukturierte Daten über eigene Engines, schwere Formate über optionale Module.',
    'Un convertitore leggero. Il nucleo resta minimo e le capacità si estendono all’occorrenza: immagini via ImageIO, testo e dati strutturati con motori propri, formati pesanti con moduli opzionali.',
    'Лёгкий конвертер форматов. Ядро остаётся небольшим, возможности добавляются по мере надобности: изображения — через системный ImageIO, текст и структурированные данные — своими движками, тяжёлые форматы — отдельными модулями.',
    'Un conversor de formatos ligero. El núcleo permanece pequeño y las capacidades se amplían a demanda: imágenes con ImageIO, texto y datos estructurados con motores propios, formatos pesados con módulos opcionales.',
    'Um conversor de formatos leve. O núcleo permanece pequeno e os recursos vêm sob demanda: imagens via ImageIO, texto e dados estruturados com motores próprios, formatos pesados em módulos opcionais.',
    '軽量なフォーマット変換ツール。コアは小さく保ち、機能は必要に応じて追加します。画像はシステムの ImageIO、テキストと構造化データは自前のエンジン、重い形式はオプションのモジュールに任せます。',
    '가벼운 형식 변환 도구입니다. 핵심은 작게 유지하고 기능은 필요할 때 확장합니다. 이미지는 시스템 ImageIO, 텍스트와 구조화 데이터는 자체 엔진, 무거운 형식은 선택 모듈이 담당합니다.',
    'Lekki konwerter formatów. Rdzeń pozostaje mały, a możliwości dodaje się w razie potrzeby: obrazy przez ImageIO, tekst i dane strukturalne własnymi silnikami, ciężkie formaty w modułach opcjonalnych.',
    'Hafif bir biçim dönüştürücü. Çekirdek küçük kalır, yetenekler gerektiğinde eklenir: görseller ImageIO, metin ve yapılandırılmış veriler kendi motorlarımız, ağır biçimler isteğe bağlı modüller.',
    'محوّل صيغ خفيف. يبقى الجوهر صغيرًا وتُضاف القدرات عند الحاجة: الصور عبر ImageIO، والنصوص والبيانات المنظمة بمحركاتنا، والصيغ الثقيلة في وحدات اختيارية.',
    'ממיר פורמטים קל. הליבה נשארת קטנה והיכולות מתרחבות לפי צורך: תמונות דרך ImageIO, טקסט ונתונים מובנים במנועים שלנו, ופורמטים כבדים במודולים אופציונליים.',
    'Kibadilishaji chepesi cha miundo. Kiini hubaki kidogo na uwezo huongezwa inapohitajika: picha kwa ImageIO, maandishi na data kwa injini zetu, miundo mizito kwa moduli za hiari.'],
'about.engines': [
    '引擎', '引擎', 'Engines', 'Moteurs', 'Engines', 'Motori', 'Движки', 'Motores', 'Motores',
    'エンジン', '엔진', 'Silniki', 'Motorlar', 'المحركات', 'מנועים', 'Injini'],
'about.license': [
    '许可', '授權', 'License', 'Licence', 'Lizenz', 'Licenza', 'Лицензия', 'Licencia',
    'Licença', 'ライセンス', '라이선스', 'Licencja', 'Lisans', 'الترخيص', 'רישיון', 'Leseni'],
'about.license_body': [
    '应用本体：MIT。图像引擎使用 macOS 系统自带的 ImageIO（通过 sips 调用），不随应用分发任何第三方编解码器。可选的 av / 3d / office 模块会各自附带许可证说明，并作为独立进程运行，因此其许可证不会影响本体。',
    '應用程式本體：MIT。圖片引擎使用 macOS 系統內建的 ImageIO（透過 sips 呼叫），不隨應用程式散布任何第三方編解碼器。可選的 av / 3d / office 模組各自附帶授權說明，並以獨立行程執行，因此其授權不影響本體。',
    'The app itself is MIT. The image engine uses the ImageIO built into macOS (via sips); no third-party codecs are distributed with the app. Optional av / 3d / office modules carry their own license notes and run as separate processes, so their licenses do not affect the core.',
    'L’application est sous MIT. Le moteur d’images utilise ImageIO intégré à macOS (via sips) ; aucun codec tiers n’est distribué. Les modules av / 3d / office optionnels ont leurs propres licences et s’exécutent comme processus séparés.',
    'Die App selbst steht unter MIT. Die Bild-Engine nutzt das in macOS enthaltene ImageIO (über sips); es werden keine Codecs Dritter mitgeliefert. Optionale av-/3d-/office-Module haben eigene Lizenzhinweise und laufen als getrennte Prozesse.',
    'L’app è MIT. Il motore immagini usa ImageIO di macOS (via sips); nessun codec di terze parti è distribuito. I moduli opzionali av / 3d / office hanno licenze proprie e girano come processi separati.',
    'Само приложение — MIT. Движок изображений использует системный ImageIO в macOS (через sips); сторонние кодеки не поставляются. Необязательные модули av / 3d / office имеют собственные лицензии и работают отдельными процессами.',
    'La aplicación es MIT. El motor de imágenes usa ImageIO de macOS (vía sips); no se distribuye ningún códec de terceros. Los módulos opcionales av / 3d / office tienen sus propias licencias y se ejecutan como procesos aparte.',
    'O app é MIT. O motor de imagens usa o ImageIO do macOS (via sips); nenhum codec de terceiros é distribuído. Os módulos opcionais av / 3d / office têm licenças próprias e rodam como processos separados.',
    'アプリ本体は MIT です。画像エンジンは macOS 内蔵の ImageIO（sips 経由）を使い、サードパーティのコーデックは同梱しません。オプションの av / 3d / office モジュールは独自のライセンス表記を持ち、別プロセスで動作します。',
    '앱 본체는 MIT입니다. 이미지 엔진은 macOS 내장 ImageIO(sips 경유)를 사용하며 서드파티 코덱을 배포하지 않습니다. 선택 모듈 av / 3d / office는 자체 라이선스를 가지며 별도 프로세스로 실행됩니다.',
    'Aplikacja jest na licencji MIT. Silnik obrazów używa wbudowanego w macOS ImageIO (przez sips); nie dystrybuujemy kodeków firm trzecich. Opcjonalne moduły av / 3d / office mają własne licencje i działają jako osobne procesy.',
    'Uygulamanın kendisi MIT. Görsel motoru macOS’un ImageIO’sunu (sips üzerinden) kullanır; üçüncü taraf kodek dağıtılmaz. İsteğe bağlı av / 3d / office modülleri kendi lisanslarına sahiptir ve ayrı süreçler olarak çalışır.',
    'التطبيق نفسه برخصة MIT. محرّك الصور يستخدم ImageIO المدمج في macOS (عبر sips)؛ ولا نوزّع أي مرمّزات من أطراف ثالثة. الوحدات الاختيارية av / 3d / office لها تراخيصها وتعمل كعمليات منفصلة.',
    'האפליקציה עצמה ב-MIT. מנוע התמונות משתמש ב-ImageIO המובנה ב-macOS (דרך sips); אין הפצה של קודקים של צד שלישי. המודולים האופציונליים av / 3d / office נושאים רישיונות משלהם ופועלים כתהליכים נפרדים.',
    'Programu yenyewe ni MIT. Injini ya picha hutumia ImageIO iliyomo macOS (kwa sips); hatusambazi kodeki za watu wengine. Moduli za hiari av / 3d / office zina leseni zake na hufanya kazi kama michakato tofauti.'],
'about.version': [
    '版本 {0} · macOS {1} · .NET {2}', '版本 {0} · macOS {1} · .NET {2}',
    'Version {0} · macOS {1} · .NET {2}', 'Version {0} · macOS {1} · .NET {2}',
    'Version {0} · macOS {1} · .NET {2}', 'Versione {0} · macOS {1} · .NET {2}',
    'Версия {0} · macOS {1} · .NET {2}', 'Versión {0} · macOS {1} · .NET {2}',
    'Versão {0} · macOS {1} · .NET {2}', 'バージョン {0} · macOS {1} · .NET {2}',
    '버전 {0} · macOS {1} · .NET {2}', 'Wersja {0} · macOS {1} · .NET {2}',
    'Sürüm {0} · macOS {1} · .NET {2}', 'الإصدار {0} · macOS {1} · .NET {2}',
    'גרסה {0} · macOS {1} · .NET {2}', 'Toleo {0} · macOS {1} · .NET {2}'],
# ─────────── Страница «О программе» — группы движков ───────────
'about.engines_builtin': [
    '内置引擎', '內建引擎', 'Built-in engines', 'Moteurs intégrés', 'Integrierte Engines',
    'Motori integrati', 'Встроенные движки', 'Motores integrados', 'Motores integrados',
    '組み込みエンジン', '내장 엔진', 'Silniki wbudowane', 'Yerleşik motorlar',
    'محركات مدمجة', 'מנועים מובנים', 'Injini zilizojengwa'],
'about.engines_module': [
    '需要依赖的引擎', '需要相依項目的引擎', 'Engines that need a package',
    'Moteurs nécessitant un paquet', 'Engines mit Paketbedarf',
    'Motori che richiedono un pacchetto', 'Движки, которым нужен пакет',
    'Motores que requieren un paquete', 'Motores que exigem um pacote',
    'パッケージが必要なエンジン', '패키지가 필요한 엔진', 'Silniki wymagające pakietu',
    'Paket gerektiren motorlar', 'محركات تحتاج إلى حزمة', 'מנועים שדורשים חבילה',
    'Injini zinazohitaji pakiti'],
'about.engines_none': [
    '当前没有需要依赖的引擎：所有已安装的引擎都已内置。',
    '目前沒有需要相依項目的引擎：所有已安裝的引擎都已內建。',
    'No engine currently needs a package — everything installed is built in.',
    'Aucun moteur ne nécessite de paquet : tout ce qui est installé est intégré.',
    'Derzeit braucht kein Engine ein Paket — alles Installierte ist integriert.',
    'Nessun motore richiede un pacchetto: tutto ciò che è installato è integrato.',
    'Сейчас ни одному движку не нужен пакет: всё установленное встроено.',
    'Ningún motor necesita un paquete: todo lo instalado está integrado.',
    'Nenhum motor precisa de pacote: tudo o que está instalado é integrado.',
    '現在パッケージが必要なエンジンはありません。インストール済みはすべて組み込みです。',
    '현재 패키지가 필요한 엔진이 없습니다. 설치된 것은 모두 내장입니다.',
    'Żaden silnik nie wymaga pakietu — wszystko jest wbudowane.',
    'Şu anda paket gerektiren motor yok; kurulu olan her şey yerleşik.',
    'لا يوجد حاليًا محرك يحتاج إلى حزمة — كل المثبَّت مدمج.',
    'אין מנוע שדורש חבילה — כל המותקן מובנה.',
    'Hakuna injini inayohitaji pakiti — kila kitu kilichowekwa kimejengwa ndani.'],
'about.engines_count': [
    '{0} 种格式', '{0} 種格式', '{0} formats', '{0} formats', '{0} Formate',
    '{0} formati', '{0} форматов', '{0} formatos', '{0} formatos',
    '{0} 形式', '{0}개 형식', '{0} formatów', '{0} biçim',
    '{0} صيغة', '{0} פורמטים', 'miundo {0}'],
# ─────────── Куда сохранять ───────────
'output.title': [
    '输出位置', '輸出位置', 'Output location', 'Emplacement', 'Speicherort',
    'Posizione di salvataggio', 'Куда сохранять', 'Ubicación de salida', 'Local de saída',
    '保存先', '저장 위치', 'Miejsce zapisu', 'Kayıt konumu',
    'مكان الحفظ', 'מיקום השמירה', 'Mahali pa kuhifadhi'],
'output.custom': [
    '保存到指定目录', '儲存到指定目錄', 'Save to a chosen folder', 'Enregistrer dans un dossier choisi',
    'In einen gewählten Ordner speichern', 'Salva in una cartella scelta',
    'Сохранять в выбранный каталог', 'Guardar en una carpeta elegida', 'Salvar em uma pasta escolhida',
    '指定フォルダに保存', '지정 폴더에 저장', 'Zapisz w wybranym folderze', 'Seçilen klasöre kaydet',
    'الحفظ في مجلد محدد', 'שמירה בתיקייה נבחרת', 'Hifadhi kwenye folda uliyochagua'],
'output.pick': [
    '选择目录…', '選擇目錄…', 'Choose folder…', 'Choisir un dossier…', 'Ordner wählen…',
    'Scegli cartella…', 'Выбрать каталог…', 'Elegir carpeta…', 'Escolher pasta…',
    'フォルダを選択…', '폴더 선택…', 'Wybierz folder…', 'Klasör seç…',
    'اختر مجلدًا…', 'בחירת תיקייה…', 'Chagua folda…'],
'output.hint': [
    '不勾选时，结果保存在与原文件相同的目录。', '未勾選時，結果儲存在與原檔案相同的目錄。',
    'Left unchecked, the result goes next to the source file.',
    'Décoché, le résultat est placé à côté du fichier source.',
    'Ohne Häkchen landet das Ergebnis neben der Quelldatei.',
    'Senza spunta il risultato va accanto al file di origine.',
    'Без галочки результат сохраняется рядом с исходным файлом.',
    'Sin marcar, el resultado se guarda junto al archivo original.',
    'Sem marcar, o resultado fica ao lado do arquivo original.',
    'チェックを外すと、元のファイルと同じ場所に保存します。',
    '체크하지 않으면 원본 파일과 같은 위치에 저장합니다.',
    'Bez zaznaczenia wynik trafia obok pliku źródłowego.',
    'İşaretlenmezse sonuç kaynak dosyanın yanına kaydedilir.',
    'بدون تحديد، يُحفظ الناتج بجانب الملف الأصلي.',
    'ללא סימון, התוצאה נשמרת לצד קובץ המקור.',
    'Bila hukuchagua, matokeo huhifadhiwa karibu na faili asili.'],
# ─────────── Индикатор сети ───────────
'network.checking': [
    '检测网络…', '偵測網路…', 'Checking network…', 'Vérification du réseau…', 'Netzwerk wird geprüft…',
    'Verifica rete…', 'Проверка сети…', 'Comprobando la red…', 'Verificando a rede…',
    'ネットワークを確認中…', '네트워크 확인 중…', 'Sprawdzanie sieci…', 'Ağ kontrol ediliyor…',
    'جارٍ فحص الشبكة…', 'בודק רשת…', 'Inaangalia mtandao…'],
'network.online': [
    '可以下载依赖', '可以下載相依項目', 'Downloads available', 'Téléchargements possibles',
    'Downloads möglich', 'Download disponibili', 'Загрузка доступна',
    'Descargas disponibles', 'Downloads disponíveis', 'ダウンロード可能',
    '다운로드 가능', 'Pobieranie możliwe', 'İndirme kullanılabilir',
    'التنزيل متاح', 'הורדות זמינות', 'Upakuaji unapatikana'],
'network.offline': [
    '无法连接下载源', '無法連線下載來源', 'Download sources unreachable',
    'Sources de téléchargement inaccessibles', 'Download-Quellen nicht erreichbar',
    'Sorgenti di download irraggiungibili', 'Источники загрузки недоступны',
    'Fuentes de descarga inaccesibles', 'Fontes de download inacessíveis',
    'ダウンロード元に接続できません', '다운로드 소스에 연결할 수 없음',
    'Źródła pobierania niedostępne', 'İndirme kaynaklarına ulaşılamıyor',
    'مصادر التنزيل غير متاحة', 'מקורות ההורדה אינם זמינים', 'Vyanzo vya kupakua havipatikani'],
# ─────────── MCP ───────────
'mcp.title': [
    'MCP 服务器', 'MCP 伺服器', 'MCP server', 'Serveur MCP', 'MCP-Server',
    'Server MCP', 'MCP-сервер', 'Servidor MCP', 'Servidor MCP',
    'MCP サーバー', 'MCP 서버', 'Serwer MCP', 'MCP sunucusu',
    'خادم MCP', 'שרת MCP', 'Seva ya MCP'],
'mcp.body': [
    '同一个应用也能作为 MCP 服务器运行，让 AI 助手直接调用转换引擎。把下面的配置粘到 MCP 客户端（Claude Desktop、Cursor 等）里即可。',
    '同一個應用也能作為 MCP 伺服器執行，讓 AI 助手直接呼叫轉換引擎。把下面的設定貼到 MCP 用戶端（Claude Desktop、Cursor 等）即可。',
    'The same app can run as an MCP server, letting AI assistants call the conversion engine directly. Paste the configuration below into your MCP client (Claude Desktop, Cursor, and others).',
    'La même application peut servir de serveur MCP, permettant aux assistants IA d’appeler directement le moteur de conversion. Collez la configuration ci-dessous dans votre client MCP.',
    'Dieselbe App kann als MCP-Server laufen, damit KI-Assistenten die Konvertierungs-Engine direkt aufrufen. Fügen Sie die Konfiguration unten in Ihren MCP-Client ein.',
    'La stessa app può funzionare come server MCP, permettendo agli assistenti IA di chiamare direttamente il motore di conversione. Incolla la configurazione qui sotto nel tuo client MCP.',
    'Это же приложение умеет работать MCP-сервером, чтобы ИИ-ассистенты вызывали движок напрямую. Вставьте конфигурацию ниже в свой MCP-клиент (Claude Desktop, Cursor и другие).',
    'La misma aplicación puede funcionar como servidor MCP, permitiendo que los asistentes de IA llamen al motor de conversión. Pega la configuración de abajo en tu cliente MCP.',
    'O mesmo app pode funcionar como servidor MCP, permitindo que assistentes de IA chamem o motor de conversão. Cole a configuração abaixo no seu cliente MCP.',
    '同じアプリが MCP サーバーとして動作し、AI アシスタントから変換エンジンを直接呼び出せます。以下の設定を MCP クライアントに貼り付けてください。',
    '같은 앱이 MCP 서버로 동작하여 AI 어시스턴트가 변환 엔진을 직접 호출할 수 있습니다. 아래 설정을 MCP 클라이언트에 붙여 넣으세요.',
    'Ta sama aplikacja może działać jako serwer MCP, pozwalając asystentom AI wywoływać silnik konwersji. Wklej poniższą konfigurację do klienta MCP.',
    'Aynı uygulama MCP sunucusu olarak çalışabilir ve yapay zekâ asistanları dönüştürme motorunu doğrudan çağırabilir. Aşağıdaki yapılandırmayı MCP istemcinize yapıştırın.',
    'يمكن للتطبيق نفسه أن يعمل خادم MCP، ليتيح للمساعدين الذكيين استدعاء محرّك التحويل مباشرة. الصق الإعدادات أدناه في عميل MCP.',
    'אותה אפליקציה יכולה לפעול כשרת MCP, ולאפשר לעוזרי AI לקרוא ישירות למנוע ההמרה. הדביקו את התצורה שלמטה בלקוח ה-MCP.',
    'Programu hiyo hiyo inaweza kufanya kazi kama seva ya MCP, ikiruhusu wasaidizi wa AI kuita injini ya ubadilishaji moja kwa moja. Bandika usanidi hapa chini kwenye mteja wako wa MCP.'],
'mcp.copy': [
    '复制配置', '複製設定', 'Copy configuration', 'Copier la configuration', 'Konfiguration kopieren',
    'Copia configurazione', 'Скопировать конфигурацию', 'Copiar configuración', 'Copiar configuração',
    '設定をコピー', '설정 복사', 'Kopiuj konfigurację', 'Yapılandırmayı kopyala',
    'نسخ الإعدادات', 'העתקת התצורה', 'Nakili usanidi'],
'mcp.tools': [
    '可用工具：{0}', '可用工具：{0}', 'Tools: {0}', 'Outils : {0}', 'Werkzeuge: {0}',
    'Strumenti: {0}', 'Инструменты: {0}', 'Herramientas: {0}', 'Ferramentas: {0}',
    '利用できるツール：{0}', '사용 가능한 도구: {0}', 'Narzędzia: {0}', 'Araçlar: {0}',
    'الأدوات: {0}', 'כלים: {0}', 'Zanaa: {0}'],
# ─────────── Предпросмотр ───────────
'preview.title': [
    '预览', '預覽', 'Preview', 'Aperçu', 'Vorschau', 'Anteprima', 'Предпросмотр',
    'Vista previa', 'Pré-visualização', 'プレビュー', '미리보기', 'Podgląd', 'Önizleme',
    'معاينة', 'תצוגה מקדימה', 'Onyesho'],
'preview.settings': [
    '预览器', '預覽器', 'Previewers', 'Aperçus', 'Vorschauen', 'Anteprime', 'Предпросмотры',
    'Vistas previas', 'Pré-visualizações', 'プレビュー', '미리보기', 'Podglądy', 'Önizlemeler',
    'المعاينات', 'תצוגות מקדימות', 'Onyesho'],
'preview.settings_hint': [
    '选择选中文件时显示哪些预览。关掉不需要的可以加快大文件的响应。',
    '選擇選取檔案時顯示哪些預覽。關掉不需要的可以加快大檔案的反應。',
    'Choose what to show when a file is selected. Turning off what you do not need keeps large files responsive.',
    'Choisissez ce qui s’affiche à la sélection d’un fichier. Désactiver l’inutile garde les gros fichiers réactifs.',
    'Wählen Sie, was beim Auswählen einer Datei erscheint. Abschalten hält große Dateien flüssig.',
    'Scegli cosa mostrare quando selezioni un file. Disattivare l’inutile mantiene reattivi i file grandi.',
    'Выберите, что показывать при выборе файла. Отключение лишнего сохраняет отзывчивость на больших файлах.',
    'Elige qué mostrar al seleccionar un archivo. Desactivar lo innecesario mantiene ágiles los archivos grandes.',
    'Escolha o que mostrar ao selecionar um arquivo. Desligar o desnecessário mantém arquivos grandes ágeis.',
    'ファイル選択時に表示する内容を選べます。不要なものを切ると大きなファイルでも軽快です。',
    '파일을 선택할 때 표시할 항목을 고릅니다. 불필요한 것을 끄면 큰 파일도 빠릅니다.',
    'Wybierz, co pokazywać po wybraniu pliku. Wyłączenie zbędnych przyspiesza duże pliki.',
    'Dosya seçildiğinde ne gösterileceğini seçin. Gereksizi kapatmak büyük dosyaları hızlı tutar.',
    'اختر ما يُعرض عند تحديد ملف. إيقاف غير الضروري يحافظ على سرعة الملفات الكبيرة.',
    'בחרו מה להציג בעת בחירת קובץ. כיבוי מיותר שומר על קובצי ענק זריזים.',
    'Chagua cha kuonyesha unapochagua faili. Kuzima kisichohitajika huweka faili kubwa wepesi.'],
'preview.thumbnail': [
    '图片缩略图', '圖片縮圖', 'Image thumbnail', 'Miniature d’image', 'Bildvorschau',
    'Miniatura immagine', 'Миниатюра изображения', 'Miniatura de imagen', 'Miniatura da imagem',
    '画像サムネイル', '이미지 썸네일', 'Miniatura obrazu', 'Görsel küçük resmi',
    'صورة مصغّرة', 'תמונה מוקטנת', 'Picha ndogo'],
'preview.text': [
    '文本内容', '文字內容', 'Text content', 'Contenu texte', 'Textinhalt',
    'Contenuto testuale', 'Текстовое содержимое', 'Contenido de texto', 'Conteúdo de texto',
    'テキスト内容', '텍스트 내용', 'Treść tekstowa', 'Metin içeriği',
    'المحتوى النصي', 'תוכן טקסט', 'Maudhui ya maandishi'],
'preview.audio': [
    '音频参数', '音訊參數', 'Audio details', 'Détails audio', 'Audio-Details',
    'Dettagli audio', 'Параметры звука', 'Detalles de audio', 'Detalhes de áudio',
    '音声情報', '오디오 정보', 'Szczegóły dźwięku', 'Ses ayrıntıları',
    'تفاصيل الصوت', 'פרטי שמע', 'Maelezo ya sauti'],
'preview.media': [
    '视频信息', '視訊資訊', 'Video details', 'Détails vidéo', 'Video-Details',
    'Dettagli video', 'Параметры видео', 'Detalles de vídeo', 'Detalhes de vídeo',
    '動画情報', '비디오 정보', 'Szczegóły wideo', 'Video ayrıntıları',
    'تفاصيل الفيديو', 'פרטי וידאו', 'Maelezo ya video'],
'preview.mesh': [
    '3D 网格统计', '3D 網格統計', '3D mesh stats', 'Statistiques de maillage', '3D-Netzstatistik',
    'Statistiche mesh', 'Статистика сетки', 'Estadísticas de malla', 'Estatísticas de malha',
    '3D メッシュ統計', '3D 메시 통계', 'Statystyki siatki', '3B ağ istatistikleri',
    'إحصاءات الشبكة', 'נתוני רשת', 'Takwimu za matundu'],
'preview.lines': [
    '{0} / {1} 行', '{0} / {1} 行', '{0} / {1} lines', '{0} / {1} lignes', '{0} / {1} Zeilen',
    '{0} / {1} righe', '{0} / {1} строк', '{0} / {1} líneas', '{0} / {1} linhas',
    '{0} / {1} 行', '{0} / {1}줄', '{0} / {1} wierszy', '{0} / {1} satır',
    '{0} / {1} سطر', '{0} / {1} שורות', 'mistari {0} / {1}'],
'preview.unavailable': [
    '预览不可用：{0}', '預覽無法使用：{0}', 'Preview unavailable: {0}',
    'Aperçu indisponible : {0}', 'Vorschau nicht verfügbar: {0}',
    'Anteprima non disponibile: {0}', 'Предпросмотр недоступен: {0}',
    'Vista previa no disponible: {0}', 'Pré-visualização indisponível: {0}',
    'プレビューを利用できません：{0}', '미리보기를 사용할 수 없습니다: {0}',
    'Podgląd niedostępny: {0}', 'Önizleme kullanılamıyor: {0}',
    'المعاينة غير متاحة: {0}', 'התצוגה המקדימה אינה זמינה: {0}', 'Onyesho halipatikani: {0}'],
# ─────────── Пакетная обработка ───────────
'files.add': [
    '添加文件', '新增檔案', 'Add files', 'Ajouter', 'Dateien hinzufügen', 'Aggiungi file',
    'Добавить файлы', 'Añadir archivos', 'Adicionar arquivos', 'ファイルを追加',
    '파일 추가', 'Dodaj pliki', 'Dosya ekle', 'إضافة ملفات', 'הוספת קבצים', 'Ongeza faili'],
'files.clear': [
    '清空', '清空', 'Clear', 'Vider', 'Leeren', 'Svuota', 'Очистить', 'Vaciar', 'Limpar',
    'クリア', '비우기', 'Wyczyść', 'Temizle', 'مسح', 'נקה', 'Ondoa zote'],
'files.summary': [
    '已载入 {0} 个文件，分为 {1} 组', '已載入 {0} 個檔案，分為 {1} 組',
    '{0} files loaded in {1} group(s)', '{0} fichiers chargés en {1} groupe(s)',
    '{0} Dateien in {1} Gruppe(n) geladen', '{0} file caricati in {1} gruppo/i',
    'Загружено файлов: {0}, групп: {1}', '{0} archivos cargados en {1} grupo(s)',
    '{0} arquivos carregados em {1} grupo(s)', '{0} 個のファイルを {1} グループに読み込みました',
    '{0}개 파일을 {1}개 그룹으로 불러왔습니다', 'Wczytano {0} plików w {1} grupach',
    '{0} dosya {1} grupta yüklendi', 'تم تحميل {0} ملفًا في {1} مجموعة',
    'נטענו {0} קבצים ב-{1} קבוצות', 'Faili {0} zimepakiwa katika makundi {1}'],
'files.max': [
    '一次最多 {0} 个文件。多出的已忽略。', '一次最多 {0} 個檔案。多出的已忽略。',
    'At most {0} files at a time. The rest were ignored.',
    '{0} fichiers au maximum. Le reste a été ignoré.',
    'Höchstens {0} Dateien auf einmal. Der Rest wurde ignoriert.',
    'Al massimo {0} file alla volta. Il resto è stato ignorato.',
    'Не больше {0} файлов за раз. Остальные пропущены.',
    'Como máximo {0} archivos a la vez. El resto se ignoró.',
    'No máximo {0} arquivos por vez. O restante foi ignorado.',
    '一度に最大 {0} ファイルまで。残りは無視されました。',
    '한 번에 최대 {0}개 파일. 나머지는 무시되었습니다.',
    'Maksymalnie {0} plików naraz. Reszta została pominięta.',
    'Tek seferde en fazla {0} dosya. Gerisi yok sayıldı.',
    'بحد أقصى {0} ملفًا في المرة. تم تجاهل الباقي.',
    'לכל היותר {0} קבצים בכל פעם. השאר התעלמו מהם.',
    'Kwa juu faili {0} kwa wakati mmoja. Zilizobaki zilipuuzwa.'],
'input.watermark': [
    '粘贴图片、输入文件路径或 URL', '貼上圖片、輸入檔案路徑或 URL',
    'Paste an image, or type a file path or URL',
    'Collez une image, ou saisissez un chemin ou une URL',
    'Bild einfügen oder Pfad/URL eingeben',
    'Incolla un’immagine o digita un percorso o URL',
    'Вставьте изображение, введите путь или ссылку',
    'Pega una imagen o escribe una ruta o URL',
    'Cole uma imagem ou digite um caminho ou URL',
    '画像を貼り付けるか、パスや URL を入力',
    '이미지를 붙여넣거나 경로 또는 URL 입력',
    'Wklej obraz lub wpisz ścieżkę albo URL',
    'Görsel yapıştırın veya yol ya da URL girin',
    'الصق صورة أو أدخل مسارًا أو رابطًا',
    'הדביקו תמונה או הקלידו נתיב או כתובת',
    'Bandika picha au andika njia au URL'],
'input.add': [
    '添加', '新增', 'Add', 'Ajouter', 'Hinzufügen', 'Aggiungi', 'Добавить', 'Añadir',
    'Adicionar', '追加', '추가', 'Dodaj', 'Ekle', 'إضافة', 'הוספה', 'Ongeza'],
'input.paste': [
    '粘贴图片', '貼上圖片', 'Paste image', 'Coller l’image', 'Bild einfügen',
    'Incolla immagine', 'Вставить изображение', 'Pegar imagen', 'Colar imagem',
    '画像を貼り付け', '이미지 붙여넣기', 'Wklej obraz', 'Görsel yapıştır',
    'لصق صورة', 'הדבקת תמונה', 'Bandika picha'],
'output.template': [
    '命名模板', '命名範本', 'Name template', 'Modèle de nom', 'Namensvorlage',
    'Modello nome', 'Шаблон имени', 'Plantilla de nombre', 'Modelo de nome',
    '名前テンプレート', '이름 템플릿', 'Szablon nazwy', 'Ad şablonu',
    'قالب الاسم', 'תבנית שם', 'Kiolezo cha jina'],
'output.template_default': [
    '{name}.{ext}', '{name}.{ext}', '{name}.{ext}', '{name}.{ext}', '{name}.{ext}',
    '{name}.{ext}', '{name}.{ext}', '{name}.{ext}', '{name}.{ext}',
    '{name}.{ext}', '{name}.{ext}', '{name}.{ext}', '{name}.{ext}',
    '{name}.{ext}', '{name}.{ext}', '{name}.{ext}'],
'output.tokens': [
    '可用：{0}', '可用：{0}', 'Available: {0}', 'Disponibles : {0}', 'Verfügbar: {0}',
    'Disponibili: {0}', 'Доступно: {0}', 'Disponibles: {0}', 'Disponíveis: {0}',
    '使用可能：{0}', '사용 가능: {0}', 'Dostępne: {0}', 'Kullanılabilir: {0}',
    'المتاح: {0}', 'זמין: {0}', 'Inapatikana: {0}'],
'convert.all': [
    '全部转换', '全部轉換', 'Convert all', 'Tout convertir', 'Alle konvertieren',
    'Converti tutto', 'Преобразовать всё', 'Convertir todo', 'Converter tudo',
    'すべて変換', '모두 변환', 'Konwertuj wszystko', 'Tümünü dönüştür',
    'تحويل الكل', 'המרת הכול', 'Badilisha zote'],
'group.title': [
    '{0}（{1} 个）', '{0}（{1} 個）', '{0} ({1})', '{0} ({1})', '{0} ({1})',
    '{0} ({1})', '{0} ({1})', '{0} ({1})', '{0} ({1})',
    '{0}（{1}）', '{0} ({1})', '{0} ({1})', '{0} ({1})',
    '{0} ({1})', '{0} ({1})', '{0} ({1})'],
'group.expand': [
    '展开详情', '展開詳情', 'Show details', 'Afficher les détails', 'Details anzeigen',
    'Mostra dettagli', 'Подробности', 'Mostrar detalles', 'Mostrar detalhes',
    '詳細を表示', '세부정보 표시', 'Pokaż szczegóły', 'Ayrıntıları göster',
    'عرض التفاصيل', 'הצגת פרטים', 'Onyesha maelezo'],
'group.collapse': [
    '收起详情', '收起詳情', 'Hide details', 'Masquer les détails', 'Details ausblenden',
    'Nascondi dettagli', 'Скрыть подробности', 'Ocultar detalles', 'Ocultar detalhes',
    '詳細を隠す', '세부정보 숨기기', 'Ukryj szczegóły', 'Ayrıntıları gizle',
    'إخفاء التفاصيل', 'הסתר פרטים', 'Ficha maelezo'],
'group.individual': [
    '单独设置每个文件', '單獨設定每個檔案', 'Set each file individually',
    'Régler chaque fichier individuellement', 'Jede Datei einzeln einstellen',
    'Imposta ogni file singolarmente', 'Настроить каждый файл отдельно',
    'Configurar cada archivo por separado', 'Configurar cada arquivo individualmente',
    'ファイルごとに設定', '파일별로 설정', 'Ustaw każdy plik osobno', 'Her dosyayı ayrı ayarla',
    'ضبط كل ملف على حدة', 'הגדרה נפרדת לכל קובץ', 'Weka kila faili kivyake'],
'item.pending': [
    '等待', '等待', 'Waiting', 'En attente', 'Wartet', 'In attesa', 'Ожидание',
    'En espera', 'Aguardando', '待機', '대기', 'Oczekuje', 'Bekliyor',
    'بالانتظار', 'ממתין', 'Inasubiri'],
'item.running': [
    '转换中', '轉換中', 'Converting', 'Conversion', 'Läuft', 'In corso', 'Обработка',
    'Convirtiendo', 'Convertendo', '変換中', '변환 중', 'Konwersja', 'Dönüştürülüyor',
    'جارٍ التحويل', 'ממיר', 'Inabadilisha'],
'item.done': [
    '完成', '完成', 'Done', 'Terminé', 'Fertig', 'Fatto', 'Готово',
    'Listo', 'Concluído', '完了', '완료', 'Gotowe', 'Tamam',
    'تم', 'הושלם', 'Imekamilika'],
'item.failed': [
    '失败', '失敗', 'Failed', 'Échec', 'Fehlgeschlagen', 'Non riuscito', 'Ошибка',
    'Error', 'Falhou', '失敗', '실패', 'Błąd', 'Başarısız',
    'فشل', 'נכשל', 'Imeshindwa'],
'progress.batch': [
    '正在转换 {0} / {1}：{2}', '正在轉換 {0} / {1}：{2}',
    'Converting {0} of {1}: {2}', 'Conversion {0} sur {1} : {2}',
    'Konvertiere {0} von {1}: {2}', 'Conversione {0} di {1}: {2}',
    'Преобразование {0} из {1}: {2}', 'Convirtiendo {0} de {1}: {2}',
    'Convertendo {0} de {1}: {2}', '{1} 中 {0} 件目を変換：{2}',
    '{1} 중 {0} 변환 중: {2}', 'Konwersja {0} z {1}: {2}', '{1} / {0} dönüştürülüyor: {2}',
    'جارٍ تحويل {0} من {1}: {2}', 'ממיר {0} מתוך {1}: {2}', 'Inabadilisha {0} kati ya {1}: {2}'],
'progress.summary': [
    '完成 {0} 个，失败 {1} 个，共 {2}', '完成 {0} 個，失敗 {1} 個，共 {2}',
    'Done {0}, failed {1}, total {2}', 'Terminés {0}, échecs {1}, total {2}',
    'Fertig {0}, fehlgeschlagen {1}, gesamt {2}', 'Completati {0}, non riusciti {1}, totale {2}',
    'Готово {0}, с ошибками {1}, всего {2}', 'Listos {0}, fallidos {1}, total {2}',
    'Concluídos {0}, falhas {1}, total {2}', '完了 {0}、失敗 {1}、合計 {2}',
    '완료 {0}, 실패 {1}, 전체 {2}', 'Gotowe {0}, błędy {1}, razem {2}',
    'Tamam {0}, başarısız {1}, toplam {2}', 'تم {0}، فشل {1}، الإجمالي {2}',
    'הושלמו {0}, נכשלו {1}, סה״כ {2}', 'Zilizokamilika {0}, zilizoshindwa {1}, jumla {2}'],
'compare.title': [
    '转换前后对比', '轉換前後對比', 'Before and after', 'Avant et après', 'Vorher und nachher',
    'Prima e dopo', 'До и после', 'Antes y después', 'Antes e depois',
    '変換前と後', '변환 전후', 'Przed i po', 'Öncesi ve sonrası',
    'قبل وبعد', 'לפני ואחרי', 'Kabla na baada'],
'compare.original': [
    '原始', '原始', 'Original', 'Original', 'Original', 'Originale', 'Исходный',
    'Original', 'Original', '元', '원본', 'Oryginał', 'Özgün',
    'الأصل', 'מקור', 'Asili'],
'compare.result': [
    '结果', '結果', 'Result', 'Résultat', 'Ergebnis', 'Risultato', 'Результат',
    'Resultado', 'Resultado', '結果', '결과', 'Wynik', 'Sonuç',
    'النتيجة', 'תוצאה', 'Matokeo'],
'compare.open': [
    '对比', '對比', 'Compare', 'Comparer', 'Vergleichen', 'Confronta', 'Сравнить',
    'Comparar', 'Comparar', '比較', '비교', 'Porównaj', 'Karşılaştır',
    'مقارنة', 'השוואה', 'Linganisha'],
'item.same_as_group': [
    '同组设置', '同組設定', 'Same as group', 'Comme le groupe', 'Wie Gruppe',
    'Come il gruppo', 'Как в группе', 'Igual que el grupo', 'Igual ao grupo',
    'グループと同じ', '그룹과 동일', 'Jak w grupie', 'Grupla aynı',
    'مثل المجموعة', 'כמו הקבוצה', 'Kama kikundi'],
'compare.smaller': [
    '体积减少 {0}%', '體積減少 {0}%', '{0}% smaller', '{0} % plus petit', '{0} % kleiner',
    '{0}% più piccolo', 'меньше на {0}%', '{0}% más pequeño', '{0}% menor',
    '{0}% 小さくなりました', '{0}% 작아짐', 'mniejszy o {0}%', '{0}% daha küçük',
    'أصغر بنسبة {0}%', 'קטן ב-{0}%', 'ndogo kwa {0}%'],
'compare.larger': [
    '体积增加 {0}%', '體積增加 {0}%', '{0}% larger', '{0} % plus grand', '{0} % größer',
    '{0}% più grande', 'больше на {0}%', '{0}% más grande', '{0}% maior',
    '{0}% 大きくなりました', '{0}% 커짐', 'większy o {0}%', '{0}% daha büyük',
    'أكبر بنسبة {0}%', 'גדול ב-{0}%', 'kubwa kwa {0}%'],
'compare.same': [
    '体积基本不变', '體積基本不變', 'size unchanged', 'taille inchangée', 'Größe unverändert',
    'dimensione invariata', 'размер не изменился', 'tamaño sin cambios', 'tamanho inalterado',
    'サイズはほぼ同じ', '크기 거의 동일', 'rozmiar bez zmian', 'boyut değişmedi',
    'الحجم دون تغيير', 'הגודל ללא שינוי', 'ukubwa haujabadilika'],
'compare.reveal': [
    '在文件夹中显示', '在資料夾中顯示', 'Show in folder', 'Afficher dans le dossier',
    'Im Ordner zeigen', 'Mostra nella cartella', 'Показать в папке',
    'Mostrar en la carpeta', 'Mostrar na pasta', 'フォルダに表示',
    '폴더에서 보기', 'Pokaż w folderze', 'Klasörde göster',
    'إظهار في المجلد', 'הצג בתיקייה', 'Onyesha kwenye folda'],
'compare.close': [
    '关闭', '關閉', 'Close', 'Fermer', 'Schließen', 'Chiudi', 'Закрыть', 'Cerrar', 'Fechar',
    '閉じる', '닫기', 'Zamknij', 'Kapat', 'إغلاق', 'סגירה', 'Funga'],
'compare.failed': [
    '无法比较：{0}', '無法比較：{0}', 'Cannot compare: {0}', 'Comparaison impossible : {0}',
    'Vergleich nicht möglich: {0}', 'Confronto non possibile: {0}',
    'Сравнить не удалось: {0}', 'No se puede comparar: {0}', 'Não é possível comparar: {0}',
    '比較できません：{0}', '비교할 수 없습니다: {0}', 'Nie można porównać: {0}',
    'Karşılaştırılamıyor: {0}', 'لا يمكن المقارنة: {0}', 'לא ניתן להשוות: {0}', 'Haiwezi kulinganisha: {0}'],
}


def main():
    os.makedirs(OUT, exist_ok=True)

    # Проверка полноты: у каждого ключа должно быть ровно столько значений,
    # сколько языков. Иначе файл получился бы с дырками.
    problems = []
    for key, values in T.items():
        if len(values) != len(LOCALES):
            problems.append(f'{key}: значений {len(values)}, языков {len(LOCALES)}')

    if problems:
        print('ОШИБКА: таблица переводов неполная:')
        for p in problems:
            print('  ' + p)
        raise SystemExit(1)

    # Все языки обязаны иметь одинаковый набор ключей.
    for index, (code, native, rtl) in enumerate(LOCALES):
        strings = {key: values[index] for key, values in T.items()}

        # Служебные ключи: имя языка и направление письма.
        # Приложение читает их, чтобы построить список языков без списка в коде,
        # поэтому пользовательский перевод подхватывается сам.
        strings['_name'] = native
        strings['_rtl'] = 'true' if rtl else 'false'

        path = os.path.join(OUT, f'strings.{code}.json')
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(strings, f, ensure_ascii=False, indent=2, sort_keys=True)
            f.write('\n')

        print(f'  {code:8} {native:12} {len(strings):3} строк')

    print()
    print(f'Всего: {len(LOCALES)} языков × {len(T)} ключей = {len(LOCALES) * len(T)} строк')
    print('Пропусков нет: проверено перед записью.')


if __name__ == '__main__':
    main()
