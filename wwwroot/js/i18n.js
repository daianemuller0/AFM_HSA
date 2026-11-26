// Tradução das telas (português → espanhol), escolhida no seletor de idioma do topo.
//
// As telas são escritas em português; com "Español" escolhido, cada texto da
// tela (rótulos, botões, títulos, mensagens, dicas) é trocado pela tradução
// do dicionário wwwroot/i18n/es.json, à medida que a tela é desenhada. O
// dicionário tem os textos fixos ("exatos") e as frases com valores no meio
// ("modelos", com {0}, {1}…). O que o usuário digitou (campos) não é tocado.
// A escolha fica guardada neste navegador.
window.appI18n = (() => {
    const CHAVE = 'afm-idioma';
    const VERSAO = (document.currentScript && document.currentScript.dataset.dicionario) || '';
    const ATRIBUTOS = ['placeholder', 'title', 'aria-label'];
    let idioma = 'pt';
    let exatos = null;          // Map texto → tradução
    let modelos = [];           // [{ rx, ordem, trad, fixo }]
    let observador = null;
    const feitos = new WeakMap(); // nó de texto → o texto traduzido que pusemos nele

    const norm = t => t.replace(/\s+/g, ' ').trim();
    const escapar = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

    function ler() {
        try { return localStorage.getItem(CHAVE) || 'pt'; } catch { return 'pt'; }
    }

    function guardar(l) {
        try { localStorage.setItem(CHAVE, l); } catch { /* sem armazenamento: vale só nesta página */ }
    }

    function regexDe(pt) {
        const partes = pt.split(/\{(\d+)\}/);
        let src = '^';
        const ordem = [];
        let fixo = 0;
        for (let i = 0; i < partes.length; i++) {
            if (i % 2 === 0) { src += escapar(partes[i]); fixo += partes[i].replace(/[^A-Za-zÀ-ú]/g, '').length; }
            else { src += '(.+?)'; ordem.push(+partes[i]); }
        }
        return { rx: new RegExp(src + '$', 's'), ordem, fixo };
    }

    async function carregar(l) {
        if (l === 'pt') { exatos = null; modelos = []; return; }
        const r = await fetch(`i18n/${l}.json?v=${VERSAO}`, { cache: 'no-cache' });
        if (!r.ok) throw new Error('dicionário ' + l + ' não encontrado');
        const d = await r.json();
        exatos = new Map(Object.entries(d.exatos || {}));
        modelos = (d.modelos || [])
            .map(([pt, trad]) => Object.assign(regexDe(pt), { trad }))
            .filter(m => m.fixo >= 4)
            .sort((a, b) => b.fixo - a.fixo);
    }

    function traduzirTexto(texto) {
        const k = norm(texto);
        if (!k || !exatos) return null;
        const e = exatos.get(k);
        if (e !== undefined) return e;
        if (k.length > 600 || !/[A-Za-zÀ-ú]{3}/.test(k)) return null;
        for (const m of modelos) {
            const r = m.rx.exec(k);
            if (!r) continue;
            let out = m.trad;
            m.ordem.forEach((n, i) => {
                const valor = r[i + 1];
                out = out.split('{' + n + '}').join(exatos.get(norm(valor)) ?? valor);
            });
            return out;
        }
        // texto montado com separadores ("Peça · ação", "Cliente — cidade"): traduz cada pedaço que estiver no dicionário
        for (const sep of [' · ', ' — ', ' – ', ' | ']) {
            if (!k.includes(sep)) continue;
            const pedacos = k.split(sep);
            let mudou = false;
            const trad = pedacos.map(p => { const t = exatos.get(p.trim()); if (t !== undefined) { mudou = true; return t; } return p; });
            if (mudou) return trad.join(sep);
        }
        return null;
    }

    function ignorar(el) {
        for (let e = el; e && e !== document.body; e = e.parentElement) {
            const t = e.tagName;
            if (t === 'SCRIPT' || t === 'STYLE' || t === 'TEXTAREA' || t === 'CODE' || t === 'PRE') return true;
            if (e.isContentEditable || (e.classList && (e.classList.contains('no-i18n') || e.classList.contains('email-previa')))) return true;
        }
        return false;
    }

    function texto(n) {
        const v = n.nodeValue;
        if (!v || feitos.get(n) === v || !n.parentElement || ignorar(n.parentElement)) return;
        const t = traduzirTexto(v);
        if (t == null || t === norm(v)) return;
        const novo = v.match(/^\s*/)[0] + t + v.match(/\s*$/)[0];
        feitos.set(n, novo);
        n.nodeValue = novo;
    }

    function atributo(el, a) {
        const v = el.getAttribute(a);
        if (!v || ignorar(el)) return;
        const t = traduzirTexto(v);
        if (t != null && t !== v) el.setAttribute(a, t);
    }

    function arvore(raiz) {
        if (!exatos || !raiz) return;
        if (raiz.nodeType === 3) { texto(raiz); return; }
        if (raiz.nodeType !== 1) return;
        if (ignorar(raiz)) return;
        for (const a of ATRIBUTOS) if (raiz.hasAttribute(a)) atributo(raiz, a);
        const w = document.createTreeWalker(raiz, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
        let n;
        while ((n = w.nextNode())) {
            if (n.nodeType === 3) texto(n);
            else for (const a of ATRIBUTOS) if (n.hasAttribute(a)) atributo(n, a);
        }
    }

    /** O seletor do topo mostra o idioma guardado (o cabeçalho é redesenhado pela navegação). */
    function sincronizarSeletor() {
        const sel = document.getElementById('seletor-idioma');
        if (sel && sel.value !== idioma) sel.value = idioma;
    }

    function observar() {
        if (observador || !document.body) return;
        observador = new MutationObserver(muts => {
            sincronizarSeletor();
            if (!exatos) return;
            for (const m of muts) {
                if (m.type === 'characterData') texto(m.target);
                else if (m.type === 'childList') m.addedNodes.forEach(arvore);
                else if (m.type === 'attributes') atributo(m.target, m.attributeName);
            }
        });
        observador.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ATRIBUTOS });
    }

    async function aplicar(l) {
        idioma = l;
        document.documentElement.lang = l === 'es' ? 'es' : l === 'en' ? 'en' : 'pt-BR';
        sincronizarSeletor();
        observar();
        if (l === 'pt') return;
        try {
            await carregar(l);
            arvore(document.body);
        } catch (e) { console.warn('tradução:', e); }
    }

    function iniciar() { aplicar(ler()); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', iniciar); else iniciar();

    return {
        /** O idioma guardado neste navegador ("pt", "es" ou "en"). */
        atual: () => ler(),
        /** Troca o idioma; voltar para o português recarrega a página (os textos originais voltam). */
        definir: async l => {
            const antes = idioma;
            guardar(l);
            if (l === 'pt') { if (antes !== 'pt') location.reload(); return; }
            await aplicar(l);
        },
        /** Traduz um texto avulso (para quem precisar no script). */
        traduzir: t => traduzirTexto(t) ?? t,
    };
})();
