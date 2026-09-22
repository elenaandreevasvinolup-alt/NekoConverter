"""从英文模板生成另外 14 种语言的文件。

它们的内容暂时是英文，`_meta.reviewed` 标成 false —— 这样：
  · 语言选择器里能看到全部 16 种，架构上"支持 16 语言"是真的；
  · 译者拿到的是结构完整、键齐全的文件，只需要替换值，不会漏键；
  · 界面能明确区分"已校对"和"待翻译"，不会把英文冒充成翻译。

打包前它们是仓库里的普通文件，打包后同一份内容进扩展包。
"""

import json
import pathlib

LOCALES = {
    "ar": "العربية",
    "de": "Deutsch",
    "es": "Español",
    "fr": "Français",
    "he": "עברית",
    "it": "Italiano",
    "ja": "日本語",
    "ko": "한국어",
    "pl": "Polski",
    "pt": "Português",
    "ru": "Русский",
    "sw": "Kiswahili",
    "tr": "Türkçe",
    "zh-Hant": "繁體中文",
}

# 阿拉伯语和希伯来语是 RTL。注意：RTL 不只是 CSS 的 direction，
# 数学公式在 RTL 环境里必须保持 LTR（∠A = 60° 不能被镜像），
# 那需要双向隔离标记。所以这两种单独测、单独验收。
RTL = {"ar", "he"}

directory = pathlib.Path("src/MathGeo.Core/Locales")
english = json.loads((directory / "strings.en.json").read_text(encoding="utf-8"))

for lang, name in LOCALES.items():
    data = {"_meta": {"lang": lang, "name": name, "rtl": lang in RTL, "reviewed": False}}
    data.update({k: v for k, v in english.items() if k != "_meta"})

    path = directory / f"strings.{lang}.json"
    path.write_text(
        json.dumps(data, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"  {lang:8} {name:12} rtl={str(lang in RTL):5} {len(data) - 1} 个键")

print(f"\n共 {len(LOCALES) + 2} 种语言（含内置的 zh-Hans 与 en）")
