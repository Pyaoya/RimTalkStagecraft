# 游戏内文本.md is where the Chinese text lives. This writes it into the Chinese Keyed file and
# About.xml (Chinese description first, then Build/About_en.txt), and the Workshop page text into
# Build/创意工坊简介.txt. It stops, writing nothing, when an entry is missing, unknown, or has other
# {0} {1} placeholders than its English one. build.ps1 runs it before compiling.
import os, re, sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

sys.stdout.reconfigure(encoding='utf-8')
here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
md_path = os.path.join(here, '游戏内文本.md')
zh_path = os.path.join(here, 'Languages', 'ChineseSimplified (简体中文)', 'Keyed', 'RimTalkStagecraft.xml')
en_path = os.path.join(here, 'Languages', 'English', 'Keyed', 'RimTalkStagecraft.xml')
about_path = os.path.join(here, 'About', 'About.xml')
about_en_path = os.path.join(here, 'Build', 'About_en.txt')
workshop_path = os.path.join(here, 'Build', '创意工坊简介.txt')

sections = []   # (title, [(name, notes, text)])
for raw in open(md_path, encoding='utf-8-sig').read().replace('\r\n', '\n').split('\n'):
    if raw.startswith('## '):
        sections.append((raw[3:].strip(), []))
    elif raw.startswith('### '):
        if not sections:
            sys.exit('游戏内文本.md: an entry before the first ## section')
        sections[-1][1].append([raw[4:].strip(), [], []])
    elif sections and sections[-1][1]:
        entry = sections[-1][1][-1]
        if raw.startswith('>'):
            entry[1].append(raw[1:].strip())
        else:
            entry[2].append(raw)
entries = {}
for title, items in sections:
    for item in items:
        item[2] = '\n'.join(item[2]).strip('\n').rstrip()
        if item[0] in entries:
            sys.exit('游戏内文本.md: ' + item[0] + ' is written twice')
        entries[item[0]] = item[2]

english = {e.tag: (e.text or '') for e in ET.parse(en_path).getroot()}
ours = [k for k in entries if k.startswith('Stagecraft_')]
problems = []
for k in english:
    if k not in entries:
        problems.append(k + ': missing')
for k in ours:
    if k not in english:
        problems.append(k + ': not a key the mod uses')
        continue
    want = sorted(set(re.findall(r'\{\d+\}', english[k])))
    have = sorted(set(re.findall(r'\{\d+\}', entries[k])))
    if want != have:
        problems.append(k + ': placeholders ' + ' '.join(have or ['none']) + ', should be ' + ' '.join(want or ['none']))
    if not entries[k].strip():
        problems.append(k + ': empty')
for k in ('About.description', 'Workshop.description'):
    if not entries.get(k, '').strip():
        problems.append(k + ': missing or empty')
if problems:
    print('游戏内文本.md has problems, nothing was written:')
    for p in problems:
        print('  ' + p)
    sys.exit(1)

out = ['<?xml version="1.0" encoding="utf-8"?>',
       '<!-- 由 Build/sync_text.py 从 游戏内文本.md 生成，不要直接改这里，改那份文件。 -->',
       '<LanguageData>']
for title, items in sections:
    keyed = [i for i in items if i[0].startswith('Stagecraft_')]
    if not keyed:
        continue
    out.append('')
    out.append('\t<!-- ===== ' + escape(title).replace('--', '—') + ' ===== -->')
    for name, notes, text in keyed:
        for n in notes:
            out.append('\t<!-- ' + escape(n).replace('--', '—') + ' -->')
        out.append('\t<' + name + '>' + escape(text) + '</' + name + '>')
out += ['', '</LanguageData>', '']
new_zh = '\n'.join(out)
old_zh = open(zh_path, encoding='utf-8-sig').read() if os.path.exists(zh_path) else ''
if new_zh != old_zh:
    open(zh_path, 'w', encoding='utf-8', newline='\n').write(new_zh)

about = open(about_path, encoding='utf-8-sig').read()
desc = entries['About.description'] + '\n\n\n' + open(about_en_path, encoding='utf-8-sig').read().strip()
new_about = re.sub(r'<description>.*?</description>', lambda m: '<description>' + escape(desc) + '</description>', about, count=1, flags=re.S)
if new_about != about:
    open(about_path, 'w', encoding='utf-8', newline='\n').write(new_about)

workshop = entries['Workshop.description'] + '\n'
old_ws = open(workshop_path, encoding='utf-8-sig').read() if os.path.exists(workshop_path) else ''
if workshop != old_ws:
    open(workshop_path, 'w', encoding='utf-8', newline='\n').write(workshop)

print('text: %d entries; Keyed %s, About %s, Workshop %s' % (len(ours),
      'rewritten' if new_zh != old_zh else 'unchanged',
      'rewritten' if new_about != about else 'unchanged',
      'rewritten' if workshop != old_ws else 'unchanged'))
