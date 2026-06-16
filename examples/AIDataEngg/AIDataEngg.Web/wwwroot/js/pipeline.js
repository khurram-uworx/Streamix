let connection = null;

function getConnection() {
    return connection;
}

function connectHub() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl('/hub/pipeline')
        .withAutomaticReconnect()
        .build();

    connection.on('PipelineEvent', onPipelineEvent);
    connection.on('PipelineComplete', onPipelineComplete);

    connection.start().catch(err => {
        console.error('SignalR connection error:', err);
    });
}

function onPipelineEvent(event) {
    const progress = document.getElementById('pipeline-progress');
    const fill = document.getElementById('progress-fill');
    const text = document.getElementById('progress-text');
    const log = document.getElementById('pipeline-log');

    progress.classList.remove('hidden');

    switch (event.type) {
        case 'started': {
            fill.style.width = '0%';
            text.textContent = 'Pipeline started...';
            if (log) log.innerHTML = '<div class="log-entry log-item">Pipeline started</div>';
            break;
        }
        case 'progress': {
            const pct = event.current > 0 && event.total > 0
                ? Math.round((event.current / event.total) * 100)
                : 0;
            fill.style.width = pct + '%';
            text.textContent = event.message || `${event.stage}: ${event.current}/${event.total}`;
            if (log && event.message) {
                log.innerHTML += `<div class="log-entry">${event.message}</div>`;
                log.scrollTop = log.scrollHeight;
            }
            break;
        }
        case 'itemProcessed': {
            if (log) {
                const icon = event.isNoise ? '&#9888;' : '&#10003;';
                log.innerHTML += `<div class="log-entry log-item">${icon} ${event.title} &rarr; ${event.signalOrNoise}</div>`;
                log.scrollTop = log.scrollHeight;
            }
            break;
        }
        case 'failed': {
            fill.style.background = 'var(--danger)';
            text.textContent = `Error: ${event.error}`;
            if (log) {
                log.innerHTML += `<div class="log-entry log-error">&#10060; ${event.stage}: ${event.error}</div>`;
                log.scrollTop = log.scrollHeight;
            }
            break;
        }
        case 'completed': {
            fill.style.width = '100%';
            fill.style.background = 'linear-gradient(90deg, var(--accent), var(--primary))';
            text.textContent = `Done: ${event.totalItems} items (${event.signalCount} signals, ${event.noiseCount} noise, ${event.failedCount} failed)`;
            if (log) {
                log.innerHTML += `<div class="log-entry log-complete">Pipeline complete: ${event.totalItems} items</div>`;
                log.scrollTop = log.scrollHeight;
            }
            setTimeout(() => progress.classList.add('hidden'), 3000);
            updateCounts();
            break;
        }
    }
}

function onPipelineComplete(result) {
    updateCounts();
}

function updateCounts() {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return;
    connection.invoke('GetSignals').then(signals => {
        const count = signals ? signals.reduce((s, g) => s + g.count, 0) : 0;
        document.getElementById('signal-count').textContent = count;
        document.getElementById('stat-signals').textContent = count;
    }).catch(() => {});
    connection.invoke('GetNoise').then(noise => {
        const count = noise ? noise.length : 0;
        document.getElementById('noise-count').textContent = count;
        document.getElementById('stat-noise').textContent = count;
    }).catch(() => {});
    connection.invoke('GetBounced').then(bounced => {
        const count = bounced ? bounced.length : 0;
        document.getElementById('bounced-count').textContent = count;
        document.getElementById('stat-bounced').textContent = count;
    }).catch(() => {});
}

async function triggerPipeline() {
    const btn = document.getElementById('trigger-pipeline');
    btn.disabled = true;

    try {
        const runId = await connection.invoke('TriggerPipeline');
        if (!runId) {
            onPipelineEvent({ type: 'failed', stage: 'Queue', error: 'A pipeline is already running.' });
        }
    } catch (err) {
        onPipelineEvent({ type: 'failed', stage: 'Trigger', error: err.toString() });
    }

    // Wait a bit then re-enable (the server-side lock handles dedup)
    setTimeout(() => { btn.disabled = false; }, 1000);
}

async function reclassify(itemId, previousSignal, isNoise) {
    if (!connection) return;
    try {
        const newSignal = isNoise ? 'NOISE' : previousSignal;
        await connection.invoke('Reclassify', itemId, previousSignal, isNoise);
        const row = document.querySelector(`.item-row[data-id="${itemId}"]`);
        if (row) row.remove();
        updateCounts();
    } catch (err) {
        console.error('Reclassify failed:', err);
    }
}

async function deleteItem(itemId) {
    if (!confirm('Delete this item?')) return;
    if (!connection) return;
    try {
        await connection.invoke('DeleteItem', itemId);
        const row = document.querySelector(`.item-row[data-id="${itemId}"]`);
        if (row) row.remove();
        updateCounts();
    } catch (err) {
        console.error('Delete failed:', err);
    }
}
