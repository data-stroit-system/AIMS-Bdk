#!/usr/bin/env python3
"""Convert a QGIS 4.x-saved project (.qgs) so a QGIS Server 3.x can read the
WFS publication flags.

QGIS 4 (e.g. 4.2.1-Belém do Pará) writes project custom properties in a
nested form:

    <properties name="properties">
      <properties name="WFSLayers" type="QStringList">
        <value>Point_Aset_Plan_17_...layer-id...</value>
      </properties>
      ...

QGIS Server 3.x (e.g. 3.44.7) only reads the classic flat encoding:

    <properties>
      <WFSLayers type="QStringList">
        <value>Point_Aset_Plan_17_...layer-id...</value>
      </WFSLayers>
      ...

Because of that mismatch, WMS still works (layers are WMS-published by
default) but WFS GetCapabilities comes back with an empty FeatureTypeList —
the layer's "Published" checkbox (Project → Properties → QGIS Server → WFS)
is invisible to the server. This script rewrites the three WFS-related
properties (WFSLayers, WFSLayersPrecision, WFSTLayers) into the flat QGIS 3
encoding and renames the container, leaving everything else untouched. It is
idempotent: a file that already uses the flat form is copied through
unchanged.

Usage:
    python3 qgis4-to-qgis3.py <project.qgs> [output.qgs]
    python3 qgis4-to-qgis3.py <project.qgs>            # writes back in place

Re-run this after every QGIS 4 save of the project before serving it with
QGIS Server 3.x. The long-term fix is matching versions (QGIS Server 4.x on
the server, or saving the project from QGIS 3.x LTR).
"""

import re
import sys


def _matching_close(text: str, start: int) -> int:
    """Return the offset just past the </properties> that closes the element
    opening at or before `start` in `text`, or -1. Self-closing
    <properties .../> tags are ignored (they do not nest)."""
    i = start
    depth = 1
    while True:
        op = text.find('<properties', i)
        cl = text.find('</properties>', i)
        if cl == -1:
            return -1
        if op != -1 and op < cl and not _self_closing(text, op):
            depth += 1
            i = op + len('<properties')
        else:
            depth -= 1
            i = cl + len('</properties>')
            if depth == 0:
                return i


def _self_closing(text: str, op: int) -> bool:
    gt = text.find('>', op)
    return gt != -1 and gt > op and text[gt - 1] == '/'


def convert(text: str) -> tuple[str, bool]:
    changed = False

    # 1. Container: QGIS 4 names the properties container itself.
    if '<properties name="properties">' in text:
        text = text.replace('<properties name="properties">', '<properties>', 1)
        changed = True

    # 2. WFSLayers (QStringList): no nesting inside, plain value children.
    m = re.search(
        r'<properties name="WFSLayers" type="QStringList">.*?</properties>',
        text, re.S)
    if m:
        block = m.group(0)
        flat = block.replace(
            '<properties name="WFSLayers" type="QStringList">',
            '<WFSLayers type="QStringList">')
        flat = flat.replace('</properties>', '</WFSLayers>')
        text = text[:m.start()] + flat + text[m.end():]
        changed = True

    # 3. WFSLayersPrecision and WFSTLayers (QMap): named child properties
    #    become <value key="..."> entries.
    for key, qmap in (
        ('WFSLayersPrecision', 'QMap'),
        ('WFSTLayers', 'QMap'),
    ):
        open_str = f'<properties name="{key}">'
        start = text.find(open_str)
        if start == -1:
            continue
        # depth-count the block so nested <properties> children are included
        end = _matching_close(text, start + len(open_str))
        if end == -1:
            continue
        inner = text[start + len(open_str):end - len('</properties>')]
        if '<properties name="' not in inner:
            # not the QGIS 4 nested form — leave as-is
            continue
        inner = re.sub(
            r'<properties name="([^"]+)" type="([^"]+)"\s*/>',
            r'<value key="\1" type="\2"/>', inner)
        inner = re.sub(
            r'<properties name="([^"]+)" type="([^"]+)">(.*?)</properties>',
            r'<value key="\1" type="\2">\3</value>', inner, flags=re.S)
        text = text[:start] + f'<{key} type="{qmap}">{inner}</{key}>' + text[end:]
        changed = True

    return text, changed


def main() -> None:
    if len(sys.argv) < 2 or len(sys.argv) > 3:
        print(__doc__.strip().split('\n', 1)[1])
        sys.exit(1)
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) == 3 else src

    with open(src, encoding='utf-8') as f:
        text = f.read()

    if '<WFSLayers ' in text and '<properties name="WFSLayers"' not in text:
        print(f'{src}: already in QGIS 3 flat form, no changes needed')
        if dst != src:
            with open(dst, 'w', encoding='utf-8') as f:
                f.write(text)
        sys.exit(0)

    text, changed = convert(text)
    if not changed:
        print(f'{src}: no QGIS 4 properties pattern found, copied unchanged')
    else:
        print(f'{src}: converted WFS properties to QGIS 3 encoding')

    with open(dst, 'w', encoding='utf-8') as f:
        f.write(text)


if __name__ == '__main__':
    main()
