let connection = null;
let wfCounts = { signals: 0, noise: 0, failed: 0 };

function escapeHtml(str) {
    var div = document.createElement('div');
    div.appendChild(document.createTextNode(str));
    return div.innerHTML;
}

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

    connection.start().then(function() {
        updateCounts();
        checkPipelineState();
    }).catch(function(err) {
        console.error('SignalR connection error:', err);
    });
}

function onPipelineEvent(event) {
    var progress = document.getElementById('pipeline-progress');
    var fill = document.getElementById('progress-fill');
    var text = document.getElementById('progress-text');
    var log = document.getElementById('pipeline-log');
    var wf = document.getElementById('workflow');

    progress.classList.remove('hidden');

    switch (event.type) {
        case 'started': {
            fill.style.width = '0%';
            text.textContent = 'Pipeline started...';
            if (log) log.innerHTML = '<div class="log-entry log-item">Pipeline started</div>';

            wfCounts = { signals: 0, noise: 0, failed: 0 };
            if (wf) wf.classList.remove('hidden');
            setText('wf-feeds', event.totalFeeds);
            setText('wf-items', '0');
            setText('wf-classified', '0 / 0');
            setText('wf-signals', '0');
            setText('wf-noise', '0');
            setText('wf-failed', '0');
            setPct('wf-progress-fill', 0);
            break;
        }
        case 'progress': {
            var pct = event.current > 0 && event.total > 0
                ? Math.round((event.current / event.total) * 100)
                : 0;
            fill.style.width = pct + '%';
            text.textContent = event.message || (event.stage + ': ' + event.current + '/' + event.total);

            if (log && event.message) {
                log.innerHTML += '<div class="log-entry">' + event.message + '</div>';
                log.scrollTop = log.scrollHeight;
            }

            if (event.stage === 'Fetch') {
                setText('wf-items', event.current);
            } else if (event.stage === 'Classify' && event.total > 0) {
                var cur = event.current || 0;
                setText('wf-classified', cur + ' / ' + event.total);
                setPct('wf-progress-fill', pct);
            }
            break;
        }
        case 'itemProcessed': {
            if (log) {
                var icon = event.isNoise ? '&#9888;' : '&#10003;';
                log.innerHTML += '<div class="log-entry log-item">' + icon + ' ' + escapeHtml(event.title) + ' &rarr; ' + escapeHtml(event.signal) + '</div>';
                log.scrollTop = log.scrollHeight;
            }

            if (event.isNoise) {
                wfCounts.noise++;
            } else {
                wfCounts.signals++;
            }
            setText('wf-signals', wfCounts.signals);
            setText('wf-noise', wfCounts.noise);
            break;
        }
        case 'failed': {
            fill.style.background = 'var(--danger)';
            text.textContent = 'Error: ' + event.error;
            if (log) {
                log.innerHTML += '<div class="log-entry log-error">&#10060; ' + event.stage + ': ' + escapeHtml(event.error) + '</div>';
                log.scrollTop = log.scrollHeight;
            }
            wfCounts.failed++;
            setText('wf-failed', wfCounts.failed);
            break;
        }
        case 'completed': {
            fill.style.width = '100%';
            fill.style.background = 'linear-gradient(90deg, var(--accent), var(--primary))';
            text.textContent = 'Done: ' + event.totalItems + ' items (' + event.signalCount + ' signals, ' + event.noiseCount + ' noise, ' + event.failedCount + ' failed)';
            if (log) {
                log.innerHTML += '<div class="log-entry log-complete">Pipeline complete: ' + event.totalItems + ' items</div>';
                log.scrollTop = log.scrollHeight;
            }

            setText('wf-classified', event.signalCount + event.noiseCount + ' / ' + event.totalItems);
            setPct('wf-progress-fill', 100);
            setText('wf-signals', event.signalCount);
            setText('wf-noise', event.noiseCount);
            setText('wf-failed', event.failedCount);

            setTimeout(function() { progress.classList.add('hidden'); }, 3000);
            updateCounts();
            break;
        }
    }
}

function setText(id, val) {
    var el = document.getElementById(id);
    if (el) el.textContent = val;
}

function setPct(id, pct) {
    var el = document.getElementById(id);
    if (el) el.style.width = pct + '%';
}

function onPipelineComplete(result) {
    updateCounts();
}

function checkPipelineState() {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return;
    connection.invoke('IsPipelineRunning').then(function(running) {
        if (!running) return;
        var progress = document.getElementById('pipeline-progress');
        if (progress) {
            progress.classList.remove('hidden');
            progress.classList.add('pulse');
        }
        var text = document.getElementById('progress-text');
        if (text) text.textContent = 'Pipeline running...';
        var wf = document.getElementById('workflow');
        if (wf) {
            wf.classList.remove('hidden');
            setText('wf-feeds', '--');
            setText('wf-items', '--');
            setText('wf-classified', '-- / --');
            setText('wf-signals', '--');
            setText('wf-noise', '--');
            setText('wf-failed', '--');
            setText('wf-progress-fill', 0);
        }
    }).catch(function() {});
}

function updateCounts() {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return;

    connection.invoke('GetSignalCounts').then(function(counts) {
        var folderContainer = document.getElementById('signal-folders');
        if (folderContainer) {
            folderContainer.innerHTML = '';
            var keys = Object.keys(counts || {}).sort(function(a, b) {
                return (counts[b] || 0) - (counts[a] || 0);
            });
            var qs = window.location.search;
            for (var i = 0; i < keys.length; i++) {
                var signal = keys[i];
                var count = counts[signal] || 0;
                var a = document.createElement('a');
                a.href = '/Signals?signal=' + encodeURIComponent(signal);
                a.className = 'nav-item nav-item-signal';
                if (qs.indexOf('signal=' + encodeURIComponent(signal)) !== -1) {
                    a.className += ' active';
                }
                a.innerHTML = '<span class="nav-icon-folder">&#128193;</span>' + escapeHtml(signal) + '<span class="nav-badge">' + count + '</span>';
                folderContainer.appendChild(a);
            }
            var browseAll = document.createElement('a');
            browseAll.href = '/Signals';
            browseAll.className = 'nav-item nav-item-signal nav-item-browse';
            browseAll.innerHTML = 'Browse All &rarr;';
            folderContainer.appendChild(browseAll);
        }
        var total = 0;
        for (var k in counts) total += counts[k];
    }).catch(function() {});

    connection.invoke('GetNoise').then(function(noise) {
        var count = noise ? noise.length : 0;
        setText('noise-count', count);
        setText('stat-noise', count);
    }).catch(function() {});

    connection.invoke('GetBounced').then(function(bounced) {
        var count = bounced ? bounced.length : 0;
        setText('bounced-count', count);
        setText('stat-bounced', count);
    }).catch(function() {});
}

async function triggerPipeline() {
    var btn = document.getElementById('trigger-pipeline');
    btn.disabled = true;

    try {
        var runId = await connection.invoke('TriggerPipeline');
        if (!runId) {
            onPipelineEvent({ type: 'failed', stage: 'Queue', error: 'A pipeline is already running.' });
        }
    } catch (err) {
        onPipelineEvent({ type: 'failed', stage: 'Trigger', error: err.toString() });
    }

    setTimeout(function() { btn.disabled = false; }, 1000);
}

async function reclassify(itemId, previousSignal, isNoise) {
    if (!connection) return;
    try {
        await connection.invoke('Reclassify', itemId, previousSignal, isNoise);
        var row = document.querySelector('.item-row[data-id="' + itemId + '"]');
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
        var row = document.querySelector('.item-row[data-id="' + itemId + '"]');
        if (row) row.remove();
        updateCounts();
    } catch (err) {
        console.error('Delete failed:', err);
    }
}

/* Reclassify modal */
var reclassifyTargetItemId = null;
var reclassifyCurrentSignal = null;

function showReclassifyModal(itemId, currentSignal) {
    reclassifyTargetItemId = itemId;
    reclassifyCurrentSignal = currentSignal;

    var noiseBtn = document.getElementById('reclassify-noise-btn');
    if (noiseBtn) noiseBtn.style.display = currentSignal ? '' : 'none';

    var select = document.getElementById('reclassify-signal-select');
    select.innerHTML = '<option value="">Loading...</option>';

    connection.invoke('GetAllSignals').then(function(signals) {
        select.innerHTML = '';
        (signals || []).sort().forEach(function(s) {
            var opt = document.createElement('option');
            opt.value = s;
            opt.textContent = s;
            select.appendChild(opt);
        });
    }).catch(function() {
        select.innerHTML = '<option value="">Error loading signals</option>';
    });

    document.getElementById('reclassify-modal').classList.remove('hidden');
}

function closeReclassifyModal() {
    document.getElementById('reclassify-modal').classList.add('hidden');
    reclassifyTargetItemId = null;
    reclassifyCurrentSignal = null;
}

function applyReclassify() {
    var signal = document.getElementById('reclassify-signal-select').value;
    if (!signal || !reclassifyTargetItemId) return;
    reclassify(reclassifyTargetItemId, signal, false);
    closeReclassifyModal();
}

function reclassifyAsNoise() {
    if (!reclassifyTargetItemId || !reclassifyCurrentSignal) return;
    reclassify(reclassifyTargetItemId, reclassifyCurrentSignal, true);
    closeReclassifyModal();
}
