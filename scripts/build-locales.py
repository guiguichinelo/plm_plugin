"""Build curated translations from a Jellyfin Web source checkout of the target version.
Usage: python scripts/build-locales.py PATH_TO_JELLYFIN_WEB_SRC
Core message values retain Jellyfin's GPL-2.0-or-later licensing. See translations/NOTICE.md.
"""
import json,re,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1];source=Path(sys.argv[1]);out=root/'src/Jellyfin.Plugin.Plm/Web/locales';out.mkdir(exist_ok=True)
definitions=json.loads((root/'translations/messages.json').read_text(encoding='utf-8'))
names=re.findall(r"'([^']+)'",(source/'lib/globalize/locales.ts').read_text(encoding='utf-8').split('];',1)[0]);coverage={}
for original in names:
 locale=original.replace('_','-').lower();core=json.loads((source/'strings'/f'{original}.json').read_text(encoding='utf-8'));values={}
 for key,definition in definitions.items():
  if locale=='en-us':values[key]=definition['en']
  elif locale.startswith('pt'):values[key]=definition['pt']
  if definition.get('native') and core.get(definition['native']):values[key]=core[definition['native']]
  # Reuse localized host labels for a compound heading when no full phrase is supplied.
  parts=definition.get('nativeParts',[])
  if locale!='en-us' and not locale.startswith('pt') and parts and all(core.get(part) for part in parts):
   values[key]=' · '.join(core[part] for part in parts)
  if locale in definition.get('locales',{}):values[key]=definition['locales'][locale]
 custom=root/'translations'/f'{locale}.json'
 if custom.exists():values.update(json.loads(custom.read_text(encoding='utf-8')))
 (out/f'{locale}.json').write_text(json.dumps(values,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8');coverage[locale]={'translated':len(values),'total':len(definitions)}
(out/'index.json').write_text(json.dumps({'source':'Jellyfin Web 12.4.0','fallback':'en-us','locales':coverage},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'{len(names)} interface locales; {len(definitions)} messages; English fallback for missing translations.')
