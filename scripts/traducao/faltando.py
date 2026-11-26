"""Lista os textos das telas que ainda não têm tradução em wwwroot/i18n/es.json.

Uso (na pasta do projeto):  python scripts/traducao/faltando.py
Gera scripts/traducao/faltando.json com os textos novos; traduza-os e junte ao
es.json ("exatos" para texto fixo; "modelos" para frases com {0}, {1}…).
"""
import json, re, subprocess, sys, os
aqui = os.path.dirname(os.path.abspath(__file__))
chaves = os.path.join(aqui, 'chaves.json')
subprocess.run([sys.executable, os.path.join(aqui, 'extrair.py'), chaves], check=True)
norm = lambda t: re.sub(r'\s+', ' ', t).strip()
d = json.load(open(chaves, encoding='utf-8'))
es = json.load(open('wwwroot/i18n/es.json', encoding='utf-8'))
ja = set(es['exatos']) | {m[0] for m in es['modelos']}
faltam = [x for x in d['exatos'] + d['modelos'] if norm(x) not in ja]
json.dump(faltam, open(os.path.join(aqui, 'faltando.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
os.remove(chaves)
print(f'{len(faltam)} texto(s) sem tradução (muitos são nomes, códigos ou iguais nas duas línguas) → scripts/traducao/faltando.json')
