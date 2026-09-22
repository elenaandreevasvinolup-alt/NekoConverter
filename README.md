# NekoConverter

开源全能格式转换器。视频、音频、图片、字幕、文档、数据、3D 模型，一个软件全搞定。

## 为什么做这个

用格式工厂被卡脖子卡到红温，一怒之下自己手搓了一个。

开源、免费，转换全程在本机完成，文件不上传。

## 能转什么

113 种格式，支持批量：

| 类别 | 覆盖 |
|---|---|
| 视频 Video | 主流容器与编码（FFmpeg 驱动） |
| 音频 Audio | 转码、提取、重采样 |
| 图片 Image | 常见位图 + RAW 读取 |
| 字幕 Subtitle | SRT / ASS / VTT / TTML 等互转 |
| 文档 Document | Markdown / DOCX / PDF 等（Pandoc 驱动） |
| 数据 Data | CSV / JSON / XML / TSV |
| 3D 模型 Model3D | Mesh 格式 |

## 平台

- macOS（x64 / arm64）
- Windows（x64 / arm64）
- Android / iOS（实验性）

Avalonia + .NET 10，一套代码跨平台。

## 下载

到 [Releases](https://github.com/elenaandreevasvinolup-alt/NekoConverter/releases) 取对应平台的包。

## 构建

```bash
git clone https://github.com/elenaandreevasvinolup-alt/NekoConverter.git
cd NekoConverter
dotnet build NekoConverter.sln -c Release
```

macOS 打成 .app：

```bash
./scripts/package-macos.sh
```

Windows：

```powershell
./scripts/package-windows.ps1
```

## 关于依赖

FFmpeg + Pandoc 加起来将近 200 MB，没塞进安装包。第一次用到相关功能时才下载，三个下载源（GitHub / Gitee / jsDelivr）自动回退，带 SHA-256 校验。

只转图片、字幕、数据的话，不装这两个引擎也能用。

## 命令行

图形界面之外还有个 CLI，方便脚本化：

```bash
nekoconv convert photo.heic --to jpeg --quality 85
nekoconv batch *.mp4 --to mkv --out ./done
nekoconv formats              # 看支持哪些格式
nekoconv packages             # 看引擎装了没
```

## 界面语言

简体中文、繁體中文、English、Русский、日本語、한국어、Deutsch、Français、Español、Italiano、Português、Polski、Türkçe、العربية、עברית、Kiswahili —— 共 16 种。
