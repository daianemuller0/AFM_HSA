// Mapa das plantas (Clientes e Unidades). Leaflet + agrupamento de marcadores,
// tudo servido pelo próprio sistema (wwwroot/lib/leaflet); só os ladrilhos do
// mapa vêm da internet (OpenStreetMap). O C# manda os pontos já com a cor; o
// mapa avisa o C# quando alguém marca ou arrasta uma planta.
window.appMapa = (() => {
    const mapas = {};

    const esc = (t) => String(t ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const brl = (v) => (v || 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 });

    function icone(p) {
        // tamanho pela base instalada (raiz, para uma planta grande não cobrir o mapa)
        const t = Math.round(Math.max(16, Math.min(40, 14 + Math.sqrt(p.equipamentos || 0) * 4)));
        const aprox = p.origem === 'estado' ? ' aprox' : '';
        const manual = p.origem === 'manual' ? ' manual' : '';
        return L.divIcon({
            className: 'mapa-pino' + aprox + manual,
            html: `<span style="width:${t}px;height:${t}px;background:${p.cor}">${p.equipamentos > 0 ? p.equipamentos : ''}</span>`,
            iconSize: [t, t], iconAnchor: [t / 2, t / 2], popupAnchor: [0, -t / 2],
        });
    }

    function popup(p) {
        const origem = p.origem === 'manual' ? 'posição marcada no mapa'
            : p.origem === 'estado' ? 'aproximada: centro do estado — arraste ou marque no mapa' : 'pela cidade';
        return `<div class="mapa-pop">
            <div class="mapa-pop-titulo">${esc(p.nome)}</div>
            <div class="mapa-pop-cliente">${esc(p.empresa)}</div>
            <div class="mapa-pop-linha">${esc(p.cidade)}${p.uf ? ' · ' + esc(p.uf) : ''}</div>
            <table>
              <tr><td>Segmento</td><td>${esc(p.segmento || '—')}</td></tr>
              <tr><td>Vendedor</td><td>${esc(p.vendedor || '—')}</td></tr>
              <tr><td>Equipamentos</td><td><strong>${p.equipamentos}</strong></td></tr>
              <tr><td>Oportunidades</td><td><strong>${p.oportunidades}</strong>${p.valor > 0 ? ' · ' + brl(p.valor) : ''}</td></tr>
            </table>
            <div class="mapa-pop-origem">${origem}</div>
            <a class="mapa-pop-link" href="/clientes/${encodeURIComponent(p.id)}">Abrir a planta →</a>
        </div>`;
    }

    function criar(elId, dotnet) {
        const el = document.getElementById(elId);
        if (!el || typeof L === 'undefined') return false;
        if (mapas[elId]) { mapas[elId].mapa.remove(); delete mapas[elId]; }
        const mapa = L.map(el, { zoomControl: true, worldCopyJump: true, preferCanvas: false }).setView([-15.5, -52], 4);
        L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 18, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
        }).addTo(mapa);
        const grupo = L.markerClusterGroup({
            showCoverageOnHover: false, maxClusterRadius: 45, spiderfyOnMaxZoom: true,
            iconCreateFunction: (c) => {
                const n = c.getChildCount();
                const t = n < 10 ? 34 : n < 50 ? 42 : 50;
                return L.divIcon({ className: 'mapa-grupo', html: `<span>${n}</span>`, iconSize: [t, t] });
            },
        });
        mapa.addLayer(grupo);
        const m = { mapa, grupo, dotnet, marcadores: {}, marcando: null, arrastar: false, ajustou: false };
        mapas[elId] = m;

        mapa.on('click', (e) => {
            if (!m.marcando) return;
            const id = m.marcando;
            m.marcando = null;
            el.classList.remove('marcando');
            dotnet.invokeMethodAsync('Marcado', id, e.latlng.lat, e.latlng.lng);
        });
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && m.marcando) { m.marcando = null; el.classList.remove('marcando'); dotnet.invokeMethodAsync('Cancelado'); }
        });
        // o mapa nasce numa aba: quando ela aparece, recalcula o tamanho
        setTimeout(() => mapa.invalidateSize(), 50);
        return true;
    }

    function pontos(elId, lista) {
        const m = mapas[elId];
        if (!m) return;
        m.grupo.clearLayers();
        m.marcadores = {};
        const camada = [];
        for (const p of lista) {
            if (p.lat == null || p.lon == null) continue;
            const mk = L.marker([p.lat, p.lon], { icon: icone(p), draggable: m.arrastar, title: p.nome, riseOnHover: true });
            mk.bindPopup(popup(p), { maxWidth: 300 });
            mk.bindTooltip(`${esc(p.nome)}<br><small>${esc(p.empresa)}</small>`, { direction: 'top', offset: [0, -8] });
            mk.on('dragend', (e) => {
                const ll = e.target.getLatLng();
                m.dotnet.invokeMethodAsync('Arrastado', p.id, ll.lat, ll.lng);
            });
            m.marcadores[p.id] = mk;
            camada.push(mk);
        }
        m.grupo.addLayers(camada);
        if (!m.ajustou && camada.length > 0) { ajustar(elId); m.ajustou = true; }
    }

    function ajustar(elId) {
        const m = mapas[elId];
        if (!m) return;
        const b = m.grupo.getBounds();
        if (b.isValid()) m.mapa.fitBounds(b.pad(0.15), { maxZoom: 9 });
        else m.mapa.setView([-15.5, -52], 4);
    }

    function focar(elId, id) {
        const m = mapas[elId];
        const mk = m && m.marcadores[id];
        if (!mk) return;
        m.grupo.zoomToShowLayer(mk, () => { mk.openPopup(); });
    }

    function marcar(elId, id) {
        const m = mapas[elId];
        if (!m) return;
        m.marcando = id;
        document.getElementById(elId)?.classList.add('marcando');
    }

    function arrastar(elId, ligado) {
        const m = mapas[elId];
        if (!m) return;
        m.arrastar = ligado;
        Object.values(m.marcadores).forEach(mk => ligado ? mk.dragging?.enable() : mk.dragging?.disable());
    }

    function destruir(elId) {
        const m = mapas[elId];
        if (m) { m.mapa.remove(); delete mapas[elId]; }
    }

    return { criar, pontos, ajustar, focar, marcar, arrastar, destruir };
})();
