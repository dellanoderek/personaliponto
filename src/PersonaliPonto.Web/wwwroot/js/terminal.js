// Terminal Web de marcação (coletor "02 — browser").
// - Hora: âncora do servidor + relógio monotônico (performance.now). O relógio do aparelho nunca é usado.
// - Fila off-line em localStorage com estados PENDING/SYNCING/SYNCED/FAILED/RETRY/CONFLICT e idempotência por UUID.
// - PIN sempre cifrado com a chave pública do servidor (RSA-OAEP/SHA-256) — nunca fica em texto no navegador.
(() => {
    const FILA = 'tc.terminal.fila';
    const $ = (id) => document.getElementById(id);
    const estado = { info: null, base: 0, chave: null, etapa: 'id', ident: '', pin: '', online: false, enviando: false };

    const lerFila = () => { try { return JSON.parse(localStorage.getItem(FILA) || '[]'); } catch { return []; } };
    const gravarFila = (f) => { try { localStorage.setItem(FILA, JSON.stringify(f)); } catch { } };

    const agoraServidor = () => estado.info ? new Date(estado.base + (performance.now() - estado.perf)) : null;

    const fmtHora = () => new Intl.DateTimeFormat('pt-BR', { timeZone: estado.info?.fusoHorario || 'America/Sao_Paulo', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
    const fmtData = () => new Intl.DateTimeFormat('pt-BR', { timeZone: estado.info?.fusoHorario || 'America/Sao_Paulo', weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' });

    function tick() {
        const a = agoraServidor();
        if (!a) return;
        $('t-hora').textContent = fmtHora().format(a);
        $('t-data').textContent = fmtData().format(a);
    }

    function status(online, texto) {
        estado.online = online;
        const s = $('t-status');
        s.classList.toggle('off', !online);
        s.querySelector('span').textContent = texto;
    }

    function atualizarFila() {
        const f = lerFila();
        const pend = f.filter(i => i.estado === 'PENDING' || i.estado === 'RETRY' || i.estado === 'SYNCING').length;
        const conf = f.filter(i => i.estado === 'CONFLICT' || i.estado === 'FAILED').length;
        $('t-fila').textContent = (pend ? `${pend} aguardando envio` : '') + (conf ? ` · ${conf} com pendência` : '');
    }

    async function importarChave(spkiB64) {
        const der = Uint8Array.from(atob(spkiB64), c => c.charCodeAt(0));
        return crypto.subtle.importKey('spki', der, { name: 'RSA-OAEP', hash: 'SHA-256' }, false, ['encrypt']);
    }

    async function cifrar(texto) {
        const buf = await crypto.subtle.encrypt({ name: 'RSA-OAEP' }, estado.chave, new TextEncoder().encode(texto));
        return btoa(String.fromCharCode(...new Uint8Array(buf)));
    }

    async function carregarInfo() {
        try {
            const r = await fetch('/terminal/api/info', { credentials: 'same-origin', cache: 'no-store' });
            if (r.status === 401) { mostrarInativo(); return false; }
            if (!r.ok) throw new Error();
            const info = await r.json();
            estado.info = info;
            estado.base = Date.parse(info.ancora.horaServidor);
            estado.perf = performance.now();
            estado.chave = await importarChave(info.chavePublica);
            $('t-local').textContent = `${info.empresa} · ${info.estabelecimento} · ${info.nome}`;
            status(true, 'On-line');
            return true;
        } catch {
            status(false, estado.info ? 'Sem conexão (modo off-line)' : 'Sem conexão');
            return false;
        }
    }

    function mostrarInativo() {
        $('t-etapa-id').classList.add('hidden');
        $('t-inativo').classList.remove('hidden');
        status(false, 'Não ativado');
    }

    function visor() {
        const v = estado.etapa === 'id' ? estado.ident : '•'.repeat(estado.pin.length);
        $('t-visor').textContent = v || ' ';
        $('t-instrucao').textContent = estado.etapa === 'id' ? 'Digite sua matrícula ou CPF' : 'Digite seu PIN';
        document.querySelector('[data-k="ok"]').textContent = estado.etapa === 'id' ? 'Avançar' : 'Registrar';
    }

    function erro(msg) { $('t-erro').textContent = msg || ''; }

    function reiniciar() {
        estado.etapa = 'id'; estado.ident = ''; estado.pin = '';
        $('t-etapa-recibo').classList.add('hidden');
        $('t-etapa-id').classList.remove('hidden');
        visor();
    }

    function recibo(dados, offline) {
        const tz = estado.info?.fusoHorario || 'America/Sao_Paulo';
        const d = new Date(dados.dataHoraMarcacao || dados.horario);
        $('r-nome').textContent = dados.funcionarioNome || '';
        $('r-hora').textContent = new Intl.DateTimeFormat('pt-BR', { timeZone: tz, hour: '2-digit', minute: '2-digit', hour12: false }).format(d);
        $('r-data').textContent = new Intl.DateTimeFormat('pt-BR', { timeZone: tz, weekday: 'long', day: '2-digit', month: 'long' }).format(d);
        $('r-nsr').textContent = dados.nsr ? `NSR ${String(dados.nsr).padStart(9, '0')}` : '';
        $('r-hash').textContent = dados.hash ? dados.hash : '';
        $('r-offline').classList.toggle('hidden', !offline);
        $('t-etapa-id').classList.add('hidden');
        $('t-etapa-recibo').classList.remove('hidden');
        setTimeout(reiniciar, offline ? 9000 : 7000);
    }

    async function enviar(item, offline) {
        const r = await fetch('/terminal/api/marcacoes', {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'X-PersonaliPonto-Terminal': '1' },
            body: JSON.stringify({
                clientId: item.clientId, identificacao: item.identificacao, pinCifrado: item.pinCifrado,
                offline, horarioOfflineReferenciado: offline ? item.horario : null, ancoraId: offline ? item.ancoraId : null
            })
        });
        let corpo = null;
        try { corpo = await r.json(); } catch { }
        return { status: r.status, corpo };
    }

    async function registrar() {
        const agora = agoraServidor();
        if (!agora || !estado.chave) { erro('Terminal sem sincronização de horário. Aguarde a conexão.'); return; }
        const item = {
            clientId: crypto.randomUUID(), identificacao: estado.ident, pinCifrado: await cifrar(estado.pin),
            horario: agora.toISOString(), ancoraId: estado.info.ancora.ancoraId, estado: 'PENDING', tentativas: 0, criadoEm: Date.now()
        };
        estado.pin = '';
        const fila = lerFila(); fila.push(item); gravarFila(fila); atualizarFila();

        try {
            item.estado = 'SYNCING';
            const { status: st, corpo } = await enviar(item, false);
            if (st === 200) { remover(item.clientId); recibo(corpo, false); return; }
            remover(item.clientId);
            erro(corpo?.detail || corpo?.title || 'Não foi possível registrar.');
            estado.etapa = st === 401 || st === 423 ? 'pin' : 'id';
            if (st !== 401) estado.ident = '';
            visor();
        } catch {
            // Sem rede: fica na fila como PENDING e será enviado como off-line.
            atualizarEstado(item.clientId, 'PENDING');
            status(false, 'Sem conexão (modo off-line)');
            recibo({ horario: item.horario, funcionarioNome: `Matrícula/CPF ${item.identificacao}` }, true);
        }
    }

    function remover(id) { gravarFila(lerFila().filter(i => i.clientId !== id)); atualizarFila(); }
    function atualizarEstado(id, novo, msg) {
        const f = lerFila();
        const i = f.find(x => x.clientId === id);
        if (i) { i.estado = novo; i.tentativas = (i.tentativas || 0) + (novo === 'RETRY' ? 1 : 0); if (msg) i.mensagem = msg; }
        gravarFila(f); atualizarFila();
    }

    async function sincronizar() {
        if (estado.enviando) return;
        estado.enviando = true;
        try {
            for (const item of lerFila().filter(i => i.estado === 'PENDING' || i.estado === 'RETRY')) {
                try {
                    atualizarEstado(item.clientId, 'SYNCING');
                    const { status: st, corpo } = await enviar(item, true);
                    if (st === 200) { remover(item.clientId); continue; }
                    const msg = corpo?.detail || corpo?.title || `HTTP ${st}`;
                    if (st === 409) atualizarEstado(item.clientId, 'CONFLICT', msg);
                    else if (st === 401 || st === 423 || st === 422 || st === 400) atualizarEstado(item.clientId, 'FAILED', msg);
                    else atualizarEstado(item.clientId, 'RETRY', msg);
                } catch {
                    atualizarEstado(item.clientId, 'RETRY');
                    status(false, 'Sem conexão (modo off-line)');
                    break;
                }
            }
            // Registros já sincronizados/descartados há mais de 7 dias saem da fila local.
            gravarFila(lerFila().filter(i => Date.now() - i.criadoEm < 7 * 864e5 || i.estado === 'PENDING' || i.estado === 'RETRY'));
            atualizarFila();
        } finally {
            estado.enviando = false;
        }
    }

    $('t-teclado').addEventListener('click', async (e) => {
        const k = e.target.closest('button')?.dataset.k;
        if (!k) return;
        erro('');
        if (k === 'limpar') { if (estado.etapa === 'pin' && !estado.pin) { estado.etapa = 'id'; } else if (estado.etapa === 'id') estado.ident = ''; else estado.pin = ''; visor(); return; }
        if (k === 'ok') {
            if (estado.etapa === 'id') { if (estado.ident.length < 1) return; estado.etapa = 'pin'; visor(); return; }
            if (estado.pin.length < 4) { erro('PIN com 4 a 8 dígitos.'); return; }
            await registrar();
            return;
        }
        if (estado.etapa === 'id' && estado.ident.length < 14) estado.ident += k;
        if (estado.etapa === 'pin' && estado.pin.length < 8) estado.pin += k;
        visor();
    });

    document.addEventListener('keydown', (e) => {
        if (/^[0-9]$/.test(e.key)) document.querySelector(`[data-k="${e.key}"]`)?.click();
        else if (e.key === 'Enter') document.querySelector('[data-k="ok"]')?.click();
        else if (e.key === 'Backspace' || e.key === 'Escape') document.querySelector('[data-k="limpar"]')?.click();
    });

    window.addEventListener('online', async () => { if (await carregarInfo()) sincronizar(); });
    window.addEventListener('offline', () => status(false, 'Sem conexão (modo off-line)'));

    (async () => {
        visor(); atualizarFila();
        setInterval(tick, 250);
        if (await carregarInfo()) sincronizar();
        setInterval(async () => { if (await carregarInfo()) sincronizar(); }, 10 * 60 * 1000);
        setInterval(() => { if (navigator.onLine) sincronizar(); }, 15000);
    })();
})();
