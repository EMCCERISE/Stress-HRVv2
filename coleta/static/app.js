// UI Elements
const navLive = document.getElementById('nav-live');
const navHistory = document.getElementById('nav-history');
const sectionLive = document.getElementById('section-live');
const sectionHistory = document.getElementById('section-history');

const btnStart = document.getElementById('btn-start');
const btnStop = document.getElementById('btn-stop');
const inputUsername = document.getElementById('username');
const valBpm = document.getElementById('val-bpm');
const valHrv = document.getElementById('val-hrv');
const liveStatus = document.getElementById('live-status');

const btnRefreshHistory = document.getElementById('btn-refresh-history');
const sessionList = document.getElementById('session-list');
const histSessionTitle = document.getElementById('hist-session-title');

// Global State
let currentSessionId = null;
let pollInterval = null;

// Chart configuration (Chart.js)
Chart.defaults.color = '#94a3b8';
Chart.defaults.font.family = 'Inter';

const commonOptions = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: { mode: 'index', intersect: false },
    plugins: { legend: { position: 'top' } },
    scales: {
        x: { grid: { color: 'rgba(255,255,255,0.05)' } },
        y: { grid: { color: 'rgba(255,255,255,0.05)' }, beginAtZero: false }
    }
};

// Live Chart
let liveChart = null;
let historyChart = null;
try {
    const ctxLive = document.getElementById('liveChart').getContext('2d');
    liveChart = new Chart(ctxLive, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                { label: 'BPM', data: [], borderColor: '#00f0ff', tension: 0.4 },
                { label: 'HRV (RMSSD)', data: [], borderColor: '#ff0055', tension: 0.4 }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false
        }
    });

    // History Chart
    const ctxHist = document.getElementById('historyChart').getContext('2d');
    historyChart = new Chart(ctxHist, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                { label: 'BPM', data: [], borderColor: '#00f0ff', tension: 0.4 },
                { label: 'HRV', data: [], borderColor: '#ff0055', tension: 0.4 }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false
        }
    });
} catch(e) {
    alert("Erro interno no Gráfico: " + e.message);
    console.error("Chart load failed:", e);
}

// Navigation
navLive.addEventListener('click', () => {
    navLive.classList.add('active');
    navHistory.classList.remove('active');
    sectionLive.classList.add('active');
    sectionHistory.classList.remove('active');
});

navHistory.addEventListener('click', () => {
    navHistory.classList.add('active');
    navLive.classList.remove('active');
    sectionHistory.classList.add('active');
    sectionLive.classList.remove('active');
    loadHistory();
});

// API Config
const API_BASE = '/api';

// Live Collection Logic
btnStart.addEventListener('click', async () => {
    const user = inputUsername.value.trim();
    if (!user) return alert("Digite o nome do usuário.");

    try {
        const res = await fetch(`${API_BASE}/sessions`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ user_name: user })
        });
        const data = await res.json();
        currentSessionId = data.session_id;
        
        btnStart.disabled = true;
        btnStop.disabled = false;
        inputUsername.disabled = true;
        liveStatus.classList.add('recording');
        liveStatus.querySelector('.text').textContent = 'Gravando...';
        
        // Reset chart
        if (liveChart) {
            liveChart.data.labels = [];
            liveChart.data.datasets[0].data = [];
            liveChart.data.datasets[1].data = [];
            liveChart.update();
        }

        startPolling();
    } catch (e) {
        alert("Erro ao iniciar sessão.");
        console.error(e);
    }
});

btnStop.addEventListener('click', async () => {
    try {
        await fetch(`${API_BASE}/sessions/stop`, { method: 'PUT' });
        resetLiveUI();
    } catch (e) {
        console.error("Erro ao parar sessão.", e);
    }
});

function resetLiveUI() {
    currentSessionId = null;
    stopPolling();
    btnStart.disabled = false;
    btnStop.disabled = true;
    inputUsername.disabled = false;
    inputUsername.value = '';
    liveStatus.classList.remove('recording');
    liveStatus.querySelector('.text').textContent = 'Aguardando Início';
    valBpm.textContent = '--';
    valHrv.textContent = '--';
}

function startPolling() {
    if (pollInterval) clearInterval(pollInterval);
    pollInterval = setInterval(fetchLiveData, 1000);
}

function stopPolling() {
    if (pollInterval) clearInterval(pollInterval);
    pollInterval = null;
}

async function fetchLiveData() {
    if (!currentSessionId) return;
    try {
        const res = await fetch(`${API_BASE}/sessions/${currentSessionId}/data`);
        const data = await res.json();
        
        if (data.length > 0) {
            const last = data[data.length - 1];
            valBpm.textContent = last.bpm ? last.bpm.toFixed(0) : '--';
            valHrv.textContent = last.hrv ? last.hrv.toFixed(1) : '--';

            // Update chart
            if (liveChart) {
                liveChart.data.labels = data.map(d => {
                    const t = new Date(d.timestamp);
                    return `${t.getHours().toString().padStart(2,'0')}:${t.getMinutes().toString().padStart(2,'0')}:${t.getSeconds().toString().padStart(2,'0')}`;
                });
                liveChart.data.datasets[0].data = data.map(d => d.bpm);
                liveChart.data.datasets[1].data = data.map(d => d.hrv);
                liveChart.update(); // Smooth polling
            }
        }
    } catch (e) {
        console.error("Erro no polling", e);
    }
}

// Check for active session on load
async function checkActiveSession() {
    try {
        const res = await fetch(`${API_BASE}/sessions/active`);
        if (res.ok) {
            const data = await res.json();
            if (data && data.id) {
                currentSessionId = data.id;
                inputUsername.value = data.user_name;
                btnStart.disabled = true;
                btnStop.disabled = false;
                inputUsername.disabled = true;
                liveStatus.classList.add('recording');
                liveStatus.querySelector('.text').textContent = 'Gravando...';
                startPolling();
            }
        }
    } catch (e) {
        console.error("Erro ao checar sessão ativa", e);
    }
}

// History Logic
btnRefreshHistory.addEventListener('click', loadHistory);

async function loadHistory() {
    try {
        const res = await fetch(`${API_BASE}/sessions`);
        const sessions = await res.json();
        
        sessionList.innerHTML = '';
        sessions.forEach(s => {
            const div = document.createElement('div');
            div.className = 'session-item';
            const date = new Date(s.start_time).toLocaleString('pt-BR');
            div.innerHTML = `
                <strong>${s.user_name}</strong>
                <span>${date}</span>
                <span style="color: ${s.status==='active'?'var(--success)':'var(--text-secondary)'}">${s.status.toUpperCase()}</span>
            `;
            div.addEventListener('click', () => {
                document.querySelectorAll('.session-item').forEach(el => el.classList.remove('active'));
                div.classList.add('active');
                loadHistoryData(s);
            });
            sessionList.appendChild(div);
        });
    } catch (e) {
        console.error("Erro ao carregar histórico", e);
    }
}

async function loadHistoryData(session) {
    histSessionTitle.textContent = `Sessão: ${session.user_name} (${new Date(session.start_time).toLocaleString('pt-BR')})`;
    try {
        const res = await fetch(`${API_BASE}/sessions/${session.id}/data`);
        const data = await res.json();
        
        historyChart.data.labels = data.map(d => {
            const t = new Date(d.timestamp);
            return `${t.getHours().toString().padStart(2,'0')}:${t.getMinutes().toString().padStart(2,'0')}:${t.getSeconds().toString().padStart(2,'0')}`;
        });
        historyChart.data.datasets[0].data = data.map(d => d.bpm);
        historyChart.data.datasets[1].data = data.map(d => d.hrv);
        historyChart.update();
    } catch (e) {
        console.error("Erro ao carregar dados da sessão", e);
    }
}

// Init
checkActiveSession();
