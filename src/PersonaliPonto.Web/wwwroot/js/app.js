// Utilitários do painel PersonaliPonto.
window.personaliPonto = {
    localizacao: () => new Promise((resolve) => {
        if (!navigator.geolocation) return resolve(null);
        navigator.geolocation.getCurrentPosition(
            p => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude, precisao: p.coords.accuracy }),
            () => resolve(null),
            { enableHighAccuracy: true, timeout: 8000, maximumAge: 30000 });
    }),
    copiar: (texto) => navigator.clipboard?.writeText(texto),
    abrir: (url) => window.open(url, '_blank', 'noopener')
};

// Relógio ancorado na hora do servidor (nunca no relógio do aparelho): hora = servidor + tempo monotônico decorrido.
window.personaliPonto.relogio = (idHora, idData, horaServidorMs, fuso) => {
    const base = performance.now();
    const fmtHora = new Intl.DateTimeFormat('pt-BR', { timeZone: fuso, hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
    const fmtData = new Intl.DateTimeFormat('pt-BR', { timeZone: fuso, weekday: 'long', day: '2-digit', month: 'long' });
    const tick = () => {
        const agora = new Date(horaServidorMs + (performance.now() - base));
        const h = document.getElementById(idHora);
        if (!h) return clearInterval(t);
        h.textContent = fmtHora.format(agora);
        const d = document.getElementById(idData);
        if (d) d.textContent = fmtData.format(agora);
    };
    tick();
    const t = setInterval(tick, 250);
};
