import re,glob,json,sys
saida=sys.argv[1]
exatos=set(); modelos=set()
def norm(t): return re.sub(r'\s+',' ',t).strip()
def bom(t):
    t2=norm(t)
    if len(t2)<2: return False
    if not re.search(r'[A-Za-zÀ-ú]{2}',t2): return False
    if re.fullmatch(r'[\w\-./#:%]+',t2) and not re.search(r'[À-ú]',t2) and t2==t2.lower() and ' ' not in t2:
        return False  # identificador/código em minúsculas
    if re.search(r'^(https?:|/|#|\.|\w+\.(js|css|png|svg)$)',t2): return False
    if re.search(r'[{}<>;=]',t2) and not re.search(r'\{\d+\}',t2): return False
    if re.fullmatch(r'[A-Z0-9_\-./ ]+',t2) and len(t2)<=4: return False  # siglas curtas (PV, TA, BU...)
    if '@' in t2 or '=>' in t2 or '&&' in t2 or '||' in t2 or '(var ' in t2 or '_' in t2 or '<' in t2 or '>' in t2: return False
    if re.fullmatch(r'\(.*\)',t2) and not ' ' in t2.strip('()'): return False
    if re.fullmatch(r'[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+',t2): return False   # IdentificadorPascal
    if re.fullmatch(r'[a-z0-9\- ]+',t2) and '-' in t2: return False           # classes css
    if re.match(r'^[A-Za-z]\w*\.[A-Za-z]',t2): return False                    # o.City, it.Nome
    if re.search(r'(is null|is not|new\(|\.Count|\.Length|ToString)',t2): return False
    return True
def fecha(t,i,abre,fecha_):
    d=0
    while i<len(t):
        c=t[i]
        if c=='"':
            i+=1
            while i<len(t) and t[i]!='"':
                if t[i]==chr(92): i+=1
                i+=1
        elif c==abre: d+=1
        elif c==fecha_:
            d-=1
            if d==0: return i+1
        i+=1
    return len(t)
KW=('if','foreach','for','while','switch','using','lock','else')
def mascarar(t):
    # troca cada expressão razor (com parênteses, colchetes e chaves balanceados) por \x01
    out=[];i=0
    while i<len(t):
        c=t[i]
        if c=='@' and i+1<len(t):
            n=t[i+1]
            if n=='@': out.append('@@');i+=2;continue
            if n=='*':
                j=t.find('*@',i+2); i=len(t) if j<0 else j+2; continue
            if n=='(': i=fecha(t,i+1,'(',')'); out.append('\x01'); continue
            if n=='{': i=fecha(t,i+1,'{','}'); out.append('\x01'); continue
            if n.isalpha() or n=='_':
                j=i+1
                while j<len(t) and (t[j].isalnum() or t[j] in '_.'):
                    j+=1
                    if j<len(t) and t[j]=='(': j=fecha(t,j,'(',')')
                    if j<len(t) and t[j]=='[': j=fecha(t,j,'[',']')
                if t[j-1]=='.': j-=1
                palavra=t[i+1:j].split('(')[0]
                if palavra in KW:
                    k=j
                    while k<len(t) and t[k] in ' \t': k+=1
                    if k<len(t) and t[k]=='(': j=fecha(t,k,'(',')')
                i=j; out.append('\x01'); continue
        out.append(c);i+=1
    return ''.join(out)
def partes_razor(t):
    return t.split('\x01')
def partes_razor_antigo(t):
    out=[];atual='';i=0
    while i<len(t):
        c=t[i]
        if c=='@' and i+1<len(t):
            n=t[i+1]
            if n=='@': atual+='@';i+=2;continue
            out.append(atual);atual=''
            if n=='(': i=fecha(t,i+1,'(',')');continue
            if n=='{': i=fecha(t,i+1,'{','}');continue
            if n=='*':
                j=t.find('*@',i+2); i=len(t) if j<0 else j+2; continue
            if n.isalpha() or n=='_':
                j=i+1
                while j<len(t) and (t[j].isalnum() or t[j] in '_.'):
                    j+=1
                    if j<len(t) and t[j]=='(': j=fecha(t,j,'(',')')
                    if j<len(t) and t[j]=='[': j=fecha(t,j,'[',']')
                if t[j-1]=='.': j-=1
                i=j;continue
            atual+=c;i+=1;continue
        if c in '{}': out.append(atual);atual='';i+=1;continue
        atual+=c;i+=1
    out.append(atual)
    return out
def modelo_de_interp(s):
    # $"texto {expr:fmt} texto" -> "texto {0} texto"
    out='';n=0;i=0
    while i<len(s):
        c=s[i]
        if c=='{':
            if i+1<len(s) and s[i+1]=='{': out+='{';i+=2;continue
            d=1;j=i+1
            while j<len(s) and d>0:
                if s[j]=='{': d+=1
                elif s[j]=='}': d-=1
                elif s[j]=='"':
                    j+=1
                    while j<len(s) and s[j]!='"': j+=1
                j+=1
            out+='{%d}'%n;n+=1;i=j;continue
        if c=='}' and i+1<len(s) and s[i+1]=='}': out+='}';i+=2;continue
        out+=c;i+=1
    return out,n
LIT=r'(\$?)"((?:[^"\\\n]|\\.)*)"'
def juntar(codigo):
    rx=re.compile(LIT+r'\s*\+\s*'+LIT)
    def f(m):
        a1,a,b1,b=m.group(1),m.group(2),m.group(3),m.group(4)
        if a1 or b1:
            if not a1: a=a.replace('{','{{').replace('}','}}')
            if not b1: b=b.replace('{','{{').replace('}','}}')
            return '$"'+a+b+'"'
        return '"'+a+b+'"'
    for _ in range(8):
        novo=rx.sub(f,codigo)
        if novo==codigo: break
        codigo=novo
    return codigo
def literais(codigo):
    codigo=juntar(codigo)
    for m in re.finditer(r'(\$?)@?"((?:[^"\\\n]|\\.)*)"',codigo):
        interp=m.group(1)=='$'; l=m.group(2).replace('\\"','"').replace('\\n',' ')
        if interp:
            t,n=modelo_de_interp(l)
            partes=re.sub(r'\{\d+\}',' ',t)
            if n==0:
                if bom(t): exatos.add(norm(t))
            elif re.search(r'[A-Za-zÀ-ú]{3,}',partes) and bom(t):
                modelos.add(norm(t))
                # os pedaços fixos também (o Blazor separa o texto das expressões só no markup; aqui fica inteiro)
        else:
            if bom(l) and (' ' in l.strip() or re.search(r'[À-ú]',l) or re.match(r'^[A-ZÀ-Ú][a-zà-ú]',l.strip())):
                exatos.add(norm(l))
arquivos=glob.glob('Components/**/*.razor',recursive=True)+glob.glob('Components/**/*.cs',recursive=True)
extra=['Data/Acessos.cs','Data/FollowUp.cs','Data/VendasEConversao.cs','Data/Consolidacao.cs','Data/RelatorioSemanal.cs','Data/VentiladorCentrifugo.cs','Data/TabelaDeCustos.cs','Data/CustosAdicionais.cs','Data/ListasDoPricing.cs','Data/PricingDaProposta.cs','Data/CalculoPricing.cs','Data/ReferenciaDeCustoRepository.cs','Data/IntelligenceService.cs','Data/ImportService.cs','Data/CotacaoIntercompany.cs','Data/Cambio.cs']
for f in arquivos+extra:
    s=open(f,encoding='utf-8').read()
    if f.endswith('.razor'):
        i=s.find('\n@code')
        marca=s if i<0 else s[:i]; codigo='' if i<0 else s[i:]
        # comentários razor fora
        marca=re.sub(r'@\*.*?\*@','',marca,flags=re.S)
        # texto entre tags, cortado nas expressões razor
        for m in re.finditer(r'>([^<>]+)<',mascarar(marca)):
            t=m.group(1)
            for parte in partes_razor(t):
                p=norm(parte)
                if re.match(r'^(if|else|foreach|for|var|case|break|switch|while|return)\b',p): continue
                texto_de_tela = len(p) >= 2 and re.search(r'[A-Za-zÀ-ú]{2}', p) and not re.search(r'[{}]|=>|==|&&|\|\||\bvar\b|\(\)|_[a-z]', p)
                if bom(p) or (texto_de_tela and not re.fullmatch(r'[a-z]+[A-Z]\w*', p) and p not in ('if','else','var','new','null','true','false','break','case')): exatos.add(p)
        for m in re.finditer(r'\b(placeholder|title|aria-label|alt)="([^"@]{2,})"',marca):
            if bom(m.group(2)): exatos.add(norm(m.group(2)))
        # literais C# dentro do markup (@(...), @onclick etc.) e do @code
        literais(marca); literais(codigo)
    else:
        literais(s)
json.dump({'exatos':sorted(exatos),'modelos':sorted(modelos)},open(saida,'w',encoding='utf-8'),ensure_ascii=False,indent=0)
print(len(exatos),len(modelos))
