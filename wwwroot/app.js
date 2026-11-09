// Utilitários chamados pelo Blazor via JS interop.
window.appPrint = () => window.print();

window.appDownload = (fileName, mime, base64) => {
    const a = document.createElement('a');
    a.href = `data:${mime};base64,${base64}`;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
};

// ---- Gerar Proposta por etapas (como a "Proposta v2" do axiaisvent) ----

// Volta ao topo ao trocar de etapa.
window.appTopo = () => window.scrollTo({ top: 0, behavior: 'auto' });

// A pendência leva ao campo: rola até ele, põe o foco e pisca o realce.
window.appIrAoCampo = (id) => {
    const el = document.getElementById(id);
    if (!el) return false;
    el.scrollIntoView({ block: 'center' });
    const campo = el.matches('input, select, textarea') ? el : el.querySelector('input, select, textarea');
    if (campo && typeof campo.focus === 'function') campo.focus({ preventScroll: true });
    el.classList.remove('realce');
    void el.offsetWidth;
    el.classList.add('realce');
    clearTimeout(el._realce);
    el._realce = setTimeout(() => el.classList.remove('realce'), 1800);
    return true;
};

// A proposta nova acabou de ser gravada: o endereço passa a ser o dela
// (F5 reabre a mesma), sem recarregar a tela.
window.appTrocarUrl = (url) => { try { history.replaceState(history.state, '', url); } catch (e) { } };

// Ctrl+S salva a proposta, de qualquer etapa.
window.appAtalhos = {
    ligar: (dotnet) => {
        window.appAtalhos.desligar();
        const aoTeclar = (e) => {
            if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === 's' || e.key === 'S')) {
                e.preventDefault();
                const ativo = document.activeElement;
                // o campo em edição grava o valor antes (o @bind é no change)
                if (ativo && typeof ativo.blur === 'function') ativo.blur();
                setTimeout(() => dotnet.invokeMethodAsync('SalvarPeloTeclado'), 60);
            }
        };
        window.appAtalhos._aoTeclar = aoTeclar;
        document.addEventListener('keydown', aoTeclar);
    },
    desligar: () => {
        if (window.appAtalhos._aoTeclar) {
            document.removeEventListener('keydown', window.appAtalhos._aoTeclar);
            window.appAtalhos._aoTeclar = null;
        }
    },
};

// copia o e-mail com a formatação (HTML) — no navegador sem https a API de
// área de transferência não existe: aí copia por uma seleção escondida
window.appCopiarHtml = async (html, texto) => {
    try {
        if (navigator.clipboard && window.ClipboardItem && window.isSecureContext) {
            await navigator.clipboard.write([new ClipboardItem({
                'text/html': new Blob([html], { type: 'text/html' }),
                'text/plain': new Blob([texto], { type: 'text/plain' }),
            })]);
            return true;
        }
    } catch (e) { /* segue para o modo antigo */ }
    try {
        const div = document.createElement('div');
        div.contentEditable = 'true';
        div.style.position = 'fixed'; div.style.left = '-9999px'; div.style.top = '0';
        div.innerHTML = html;
        document.body.appendChild(div);
        const faixa = document.createRange(); faixa.selectNodeContents(div);
        const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(faixa);
        const ok = document.execCommand('copy');
        sel.removeAllRanges(); div.remove();
        return ok;
    } catch (e) { return false; }
};
