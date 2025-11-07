async function fetchLogs() {
  const apiKey = localStorage.getItem('deploy_api_key');
  if (!apiKey) { document.getElementById('results').innerText = 'API key required - enter and Save above.'; return; }

  const filter = document.getElementById('filter').value;
  const sort = document.getElementById('sort').value;
  const limit = document.getElementById('limit').value;
  const params = new URLSearchParams();
  if (filter) params.append('filter', filter);
  if (sort) params.append('sort', sort);
  if (limit) params.append('limit', limit);
  const uri = '/api/logs?' + params.toString();
  try {
    const r = await fetch(uri, { headers: { 'X-Api-Key': apiKey } });
    if (!r.ok) {
      document.getElementById('results').innerText = 'Error: ' + r.statusText;
      return;
    }
    const data = await r.json();
    renderTable(data);
  } catch (e) {
    document.getElementById('results').innerText = 'Fetch failed: ' + e;
  }
}

function renderTable(rows) {
  if (!rows || rows.length === 0) {
    document.getElementById('results').innerText = 'No rows';
    return;
  }
  const cols = Object.keys(rows[0]);
  let html = '<table><thead><tr>';
  html += '<th>datestamp</th><th>Level</th><th>E</th><th>Message</th>';
  html += '</tr></thead><tbody>';
  for (const r of rows) {
    const datestamp = r.datestamp ?? '';
    const level = r.Level ?? '';
    const e = r.E ?? '';
    let msg = '';
    try { msg = r.payload?.Message ?? ''; } catch { msg = ''; }
    html += `<tr><td>${escapeHtml(datestamp)}</td><td>${escapeHtml(level)}</td><td>${escapeHtml(e)}</td><td><pre>${escapeHtml(msg)}</pre></td></tr>`;
  }
  html += '</tbody></table>';
  document.getElementById('results').innerHTML = html;
}

function escapeHtml(s) {
  if (!s) return '';
  return String(s).replace(/[&<>"']/g, function (c) { return ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' })[c]; });
}

document.getElementById('apply').addEventListener('click', fetchLogs);
document.getElementById('refresh').addEventListener('click', fetchLogs);
document.getElementById('saveKey').addEventListener('click', () => {
  const k = document.getElementById('apiKey').value;
  if (k) { localStorage.setItem('deploy_api_key', k); alert('Saved API key to localStorage'); }
});

// fill api key field from storage if present
const stored = localStorage.getItem('deploy_api_key');
if (stored) document.getElementById('apiKey').value = stored;

// initial load only attempts fetch if key present
if (stored) fetchLogs();
