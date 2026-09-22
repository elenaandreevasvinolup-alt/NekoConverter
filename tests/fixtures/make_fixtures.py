#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Генератор тестовых файлов для NekoConverter.
Создаёт PNG с прозрачностью и DOCX с китайским/русским текстом, таблицей и картинкой.
Скрипт нужен только для проверки — в поставку приложения не входит.
"""
import os
import struct
import zlib
import zipfile

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)))


def make_png(path, width, height):
    """PNG RGBA: левая половина — красная непрозрачная, правая — прозрачная."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter type 0
        for x in range(width):
            if x < width // 2:
                raw += bytes((220, 40, 40, 255))
            else:
                raw += bytes((40, 80, 220, 0))

    def chunk(tag, data):
        body = tag + data
        return struct.pack('>I', len(data)) + body + struct.pack('>I', zlib.crc32(body) & 0xFFFFFFFF)

    ihdr = struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)
    blob = (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', ihdr)
            + chunk(b'IDAT', zlib.compress(bytes(raw), 9))
            + chunk(b'IEND', b''))
    with open(path, 'wb') as f:
        f.write(blob)
    return blob


W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'

DOCUMENT_XML = f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="{W}"
            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
            xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
            xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
  <w:body>
    <w:p>
      <w:pPr><w:pStyle w:val="Heading1"/><w:outlineLvl w:val="0"/></w:pPr>
      <w:r><w:t>NekoConverter 测试文档</w:t></w:r>
    </w:p>
    <w:p>
      <w:r><w:t xml:space="preserve">这是一段中文正文，用来验证中文字体嵌入是否正常，否则会出现方框。Проверка русского текста для проверки кириллицы. </w:t></w:r>
      <w:r><w:rPr><w:b/></w:rPr><w:t>这一段是加粗的中文。</w:t></w:r>
      <w:r><w:rPr><w:i/></w:rPr><w:t>这一段是斜体的中文。</w:t></w:r>
    </w:p>
    <w:p>
      <w:pPr><w:pStyle w:val="Heading2"/><w:outlineLvl w:val="1"/></w:pPr>
      <w:r><w:t>二级标题 Heading 2</w:t></w:r>
    </w:p>
    <w:p>
      <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr>
      <w:r><w:t>列表项一，检查项目符号</w:t></w:r>
    </w:p>
    <w:p>
      <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr>
      <w:r><w:t>列表项二，很长的文本用于验证自动换行是否会在页面宽度处正确折行而不会溢出边界</w:t></w:r>
    </w:p>
    <w:tbl>
      <w:tr>
        <w:tc><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>列标题 A</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>列标题 B</w:t></w:r></w:p></w:tc>
      </w:tr>
      <w:tr>
        <w:tc><w:p><w:r><w:t>数值 1</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>数值 2</w:t></w:r></w:p></w:tc>
      </w:tr>
      <w:tr>
        <w:tc><w:p><w:r><w:t>一个比较长的单元格内容，用来测试单元格内的自动换行</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>短</w:t></w:r></w:p></w:tc>
      </w:tr>
    </w:tbl>
    <w:p><w:r><w:t>下面是图片：</w:t></w:r></w:p>
    <w:p>
      <w:r>
        <w:drawing>
          <wp:inline>
            <wp:extent cx="1905000" cy="1143000"/>
            <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
              <pic:pic><pic:blipFill><a:blip r:embed="rIdImage1"/></pic:blipFill></pic:pic>
            </a:graphicData></a:graphic>
          </wp:inline>
        </w:drawing>
      </w:r>
    </w:p>
    <w:p><w:r><w:t>文档结束。</w:t></w:r></w:p>
  </w:body>
</w:document>
'''

RELS_XML = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rIdImage1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/test.png"/>
</Relationships>
'''

ROOT_RELS_XML = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
'''

STYLES_XML = f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="{W}">
  <w:docDefaults>
    <w:rPrDefault><w:rPr><w:sz w:val="21"/></w:rPr></w:rPrDefault>
  </w:docDefaults>
</w:styles>
'''

CONTENT_TYPES_XML = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Default Extension="png" ContentType="image/png"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>
'''


def make_docx(path, png_bytes):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('[Content_Types].xml', CONTENT_TYPES_XML)
        z.writestr('_rels/.rels', ROOT_RELS_XML)
        z.writestr('word/document.xml', DOCUMENT_XML)
        z.writestr('word/_rels/document.xml.rels', RELS_XML)
        z.writestr('word/styles.xml', STYLES_XML)
        z.writestr('word/media/test.png', png_bytes)


def main():
    png_path = os.path.join(OUT, 'sample-rgba.png')
    png_bytes = make_png(png_path, 200, 120)
    print(f'PNG  : {png_path} ({len(png_bytes)} байт)')

    docx_path = os.path.join(OUT, 'sample.docx')
    make_docx(docx_path, png_bytes)
    print(f'DOCX : {docx_path} ({os.path.getsize(docx_path)} байт)')


if __name__ == '__main__':
    main()
