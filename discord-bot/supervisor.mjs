// Tavern Tales Bot — Control Panel + Supervisor (standalone).
// Runs the bot as a child process, keeps it alive (auto-restart on crash), and
// serves a small admin-styled web UI on http://localhost:8090 with Start / Stop /
// Restart buttons, live status (uptime + deployed /health build) and a log tail.
//
// Start it with:  node supervisor.mjs   (or run start-bot-control.bat)
import http from 'node:http';
import { spawn, exec } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const DIR = path.dirname(fileURLToPath(import.meta.url));

function readEnv() {
  const out = {};
  try {
    for (const line of fs.readFileSync(path.join(DIR, '.env'), 'utf8').split(/\r?\n/)) {
      const m = /^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/.exec(line);
      if (m) out[m[1]] = m[2].replace(/^["']|["']$/g, '');
    }
  } catch { /* no .env */ }
  return out;
}
const env = readEnv();
const ADMIN_TOKEN = env.ADMIN_TOKEN || '';
const PORT = parseInt(env.SUPERVISOR_PORT || '8642', 10);
const BOT_PORT = parseInt(env.PORT || '8080', 10);

let child = null;
let desired = 'running';       // 'running' | 'stopped'
let startedAt = 0;
let restarts = 0;
const logs = [];
function pushLog(s) {
  for (const line of String(s).split(/\r?\n/)) {
    if (!line.trim()) continue;
    logs.push(line.length > 500 ? line.slice(0, 500) : line);
    while (logs.length > 400) logs.shift();
  }
}

// Kill whatever is already listening on the bot port (a previously-launched bot),
// so we don't get a port clash when we spawn our own child. Windows-specific.
function freeBotPort(cb) {
  exec(`netstat -ano -p tcp | findstr :${BOT_PORT}`, (err, stdout) => {
    const pids = new Set();
    for (const line of (stdout || '').split(/\r?\n/)) {
      const m = /LISTENING\s+(\d+)\s*$/.exec(line.trim());
      if (m) pids.add(m[1]);
    }
    if (!pids.size) return cb();
    let left = pids.size;
    for (const pid of pids) {
      pushLog(`Freeing bot port ${BOT_PORT}: killing pid ${pid}`);
      exec(`taskkill /PID ${pid} /F`, () => { if (--left === 0) setTimeout(cb, 800); });
    }
  });
}

function spawnBot() {
  if (child) return;
  child = spawn(process.execPath, ['src/index.js'], { cwd: DIR });
  startedAt = Date.now();
  pushLog(`▶ bot started (pid ${child.pid})`);
  child.stdout.on('data', (d) => pushLog(d.toString()));
  child.stderr.on('data', (d) => pushLog('ERR ' + d.toString()));
  child.on('exit', (code) => {
    pushLog(`■ bot exited (code ${code})`);
    child = null;
    if (desired === 'running') { restarts++; pushLog('…auto-restarting in 3s'); setTimeout(spawnBot, 3000); }
  });
}
function startBot() { desired = 'running'; if (!child) freeBotPort(spawnBot); }
function stopBot() { desired = 'stopped'; if (child) { try { child.kill(); } catch { /* ignore */ } } }
function restartBot() {
  desired = 'running';
  if (child) { try { child.kill(); } catch { /* ignore */ } }   // exit handler respawns
  else freeBotPort(spawnBot);
}

// Fetch the bot's /health so the panel can show the deployed build + uptime.
function botHealth(cb) {
  const req = http.get({ host: '127.0.0.1', port: BOT_PORT, path: '/health', timeout: 2500 }, (res) => {
    let b = ''; res.on('data', (c) => (b += c)); res.on('end', () => { try { cb(JSON.parse(b)); } catch { cb(null); } });
  });
  req.on('error', () => cb(null));
  req.on('timeout', () => { req.destroy(); cb(null); });
}

// ── Game-data editor ─────────────────────────────────────────────────────────
// Every Tavern Tales data file is editable here. content.* are keys inside
// content.json; the others are whole files. Saves validate JSON, back up the old
// file to data-backups/, then take effect on the next bot restart.
const GD = path.join(DIR, 'game-data');
const BACKUPS = path.join(DIR, 'data-backups');
const SECTIONS = [
  { key: 'items', label: 'Items — weapons/armor/rings/materials/consumables', file: 'content.json', sub: 'items', kind: 'array' },
  { key: 'skills', label: 'Skills', file: 'content.json', sub: 'skills', kind: 'array' },
  { key: 'classes', label: 'Classes', file: 'content.json', sub: 'classes', kind: 'array' },
  { key: 'races', label: 'Races', file: 'content.json', sub: 'races', kind: 'array' },
  { key: 'zones', label: 'Zones', file: 'content.json', sub: 'zones', kind: 'array' },
  { key: 'rarities', label: 'Rarities', file: 'content.json', sub: 'rarities', kind: 'array' },
  { key: 'affixes', label: 'Affixes (prefix / suffix)', file: 'content.json', sub: 'affixes', kind: 'object' },
  { key: 'version', label: 'Content version/meta', file: 'content.json', sub: 'version', kind: 'object' },
  { key: 'monsters', label: 'Monsters', file: 'monsters.json', sub: 'monsters', kind: 'array' },
  { key: 'recipes', label: 'Recipes — crafting & alchemy', file: 'recipes.json', sub: null, kind: 'array' },
  { key: 'gather-materials', label: 'Gathering materials', file: 'gather-materials.json', sub: null, kind: 'array' },
  { key: 'gather-areas', label: 'Gathering areas', file: 'gather-areas.json', sub: null, kind: 'array' },
  { key: 'gather-drops', label: 'Gathering drops', file: 'gather-drops.json', sub: null, kind: 'array' },
];
const sectionByKey = (k) => SECTIONS.find((s) => s.key === k);
const readJson = (file) => JSON.parse(fs.readFileSync(path.join(GD, file), 'utf8'));
const getSection = (s) => { const doc = readJson(s.file); return s.sub ? doc[s.sub] : doc; };
const countOf = (v) => (Array.isArray(v) ? v.length : v && typeof v === 'object' ? Object.keys(v).length : 0);
function backupFile(file) {
  try { fs.mkdirSync(BACKUPS, { recursive: true }); const ts = new Date().toISOString().replace(/[:.]/g, '-'); fs.copyFileSync(path.join(GD, file), path.join(BACKUPS, `${file}.${ts}.bak`)); } catch { /* best-effort */ }
}
function writeSection(s, value) {
  if (s.kind === 'array' && !Array.isArray(value)) throw new Error('This section must be a JSON array [ … ].');
  if (s.kind === 'object' && (typeof value !== 'object' || Array.isArray(value) || value === null)) throw new Error('This section must be a JSON object { … }.');
  backupFile(s.file);
  if (s.sub) {
    const doc = readJson(s.file);
    doc[s.sub] = value;
    if (s.file === 'monsters.json' && Array.isArray(value)) doc.count = value.length;
    fs.writeFileSync(path.join(GD, s.file), JSON.stringify(doc, null, 2) + '\n');
  } else {
    fs.writeFileSync(path.join(GD, s.file), JSON.stringify(value, null, 2) + '\n');
  }
}
// "Add new" skeleton: clone the first existing entry and blank its id/name.
function templateFor(s) {
  try {
    const cur = getSection(s);
    if (Array.isArray(cur) && cur.length) {
      const t = JSON.parse(JSON.stringify(cur[0]));
      if ('id' in t) t.id = 'new_' + (t.id || 'entry');
      if ('key' in t) t.key = 'new_' + (t.key || 'entry');
      if ('name' in t) t.name = 'New ' + (t.name || 'Entry');
      return t;
    }
  } catch { /* ignore */ }
  return {};
}
function readBody(req, cb) {
  let b = '';
  req.on('data', (c) => { b += c; if (b.length > 8e6) req.destroy(); });
  req.on('end', () => { try { cb(b ? JSON.parse(b) : {}); } catch { cb(null); } });
}

// ── HTTP server (control panel) ──────────────────────────────────────────────
function authed(req, url) {
  const t = (req.headers['x-admin-token'] || url.searchParams.get('token') || '').toString();
  return ADMIN_TOKEN && t === ADMIN_TOKEN;
}
function send(res, code, obj) { res.writeHead(code, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(obj)); }

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://localhost:${PORT}`);
  if (req.method === 'GET' && url.pathname === '/') {
    res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(PAGE); return;
  }
  // Everything below is token-gated.
  if (!ADMIN_TOKEN) return send(res, 503, { error: 'Set ADMIN_TOKEN in the bot .env to use the control panel.' });
  if (!authed(req, url)) return send(res, 401, { error: 'Wrong password.' });

  if (req.method === 'GET' && url.pathname === '/api/status') {
    botHealth((h) => send(res, 200, {
      running: !!child, pid: child?.pid || null,
      uptimeSec: child ? Math.round((Date.now() - startedAt) / 1000) : 0,
      restarts, desired, build: h?.build || null, healthUptime: h?.uptime ?? null,
      botPort: BOT_PORT,
    }));
    return;
  }
  if (req.method === 'GET' && url.pathname === '/api/logs') {
    return send(res, 200, { lines: logs.slice(-200) });
  }
  if (req.method === 'POST' && url.pathname === '/api/restart') { restartBot(); return send(res, 200, { ok: true }); }
  if (req.method === 'POST' && url.pathname === '/api/start') { startBot(); return send(res, 200, { ok: true }); }
  if (req.method === 'POST' && url.pathname === '/api/stop') { stopBot(); return send(res, 200, { ok: true }); }

  // Game-data editor.
  if (req.method === 'GET' && url.pathname === '/api/data/sections') {
    return send(res, 200, { sections: SECTIONS.map((s) => { let count = null; try { count = countOf(getSection(s)); } catch { /* file missing */ } return { key: s.key, label: s.label, kind: s.kind, count }; }) });
  }
  if (req.method === 'GET' && url.pathname === '/api/data') {
    const s = sectionByKey(url.searchParams.get('section'));
    if (!s) return send(res, 404, { error: 'unknown section' });
    try { const v = getSection(s); return send(res, 200, { json: JSON.stringify(v, null, 2), count: countOf(v), kind: s.kind, file: s.file }); }
    catch (e) { return send(res, 500, { error: e.message }); }
  }
  if (req.method === 'GET' && url.pathname === '/api/data/template') {
    const s = sectionByKey(url.searchParams.get('section'));
    if (!s) return send(res, 404, { error: 'unknown section' });
    return send(res, 200, { template: templateFor(s) });
  }
  if (req.method === 'POST' && url.pathname === '/api/data') {
    readBody(req, (body) => {
      if (!body) return send(res, 400, { error: 'Bad request body.' });
      const s = sectionByKey(body.section);
      if (!s) return send(res, 404, { error: 'unknown section' });
      let parsed;
      try { parsed = JSON.parse(body.json); } catch (e) { return send(res, 400, { error: 'Invalid JSON — ' + e.message }); }
      try { writeSection(s, parsed); } catch (e) { return send(res, 400, { error: e.message }); }
      pushLog(`✎ game-data saved: ${s.key} (${countOf(parsed)} entries)`);
      if (body.restart) restartBot();
      return send(res, 200, { ok: true, count: countOf(parsed), restarted: !!body.restart });
    });
    return;
  }

  send(res, 404, { error: 'not found' });
});

server.listen(PORT, () => {
  pushLog(`Control panel on http://localhost:${PORT}  (bot port ${BOT_PORT})`);
  if (!ADMIN_TOKEN) pushLog('WARNING: ADMIN_TOKEN not set — panel controls are disabled.');
  startBot();   // adopt the port and bring the bot up under supervision
});

const PAGE = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>Tavern Tales — Bot Control</title>
<style>
  :root{--bg:#0c140f;--card:#101c14;--accent:#7cc44a;--ink:#e8e0c4;--dim:#8aa07c;--line:#254a2e;--danger:#d46a6a;--amber:#d4a437;}
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--ink);font-family:'Segoe UI',system-ui,Arial,sans-serif}
  .wrap{max-width:820px;margin:0 auto;padding:20px 16px 60px}
  h1{color:var(--accent);font-size:24px;margin:0 0 4px}
  .sub{color:var(--dim);margin:0 0 16px;font-size:13px}
  .card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px 16px;margin:12px 0}
  h2{color:var(--accent);font-size:15px;margin:0 0 10px;text-transform:uppercase;letter-spacing:.04em}
  label{display:block;color:var(--dim);font-size:11px;text-transform:uppercase;margin:8px 0 3px}
  input{background:#08110b;color:var(--ink);border:1px solid var(--line);border-radius:5px;padding:6px 8px;font-size:14px}
  button{background:#14241c;color:var(--accent);border:1px solid var(--accent);border-radius:5px;padding:8px 16px;font-weight:700;cursor:pointer;font-size:13px;margin-right:8px}
  button:hover{background:var(--accent);color:#08110b}
  button.danger{color:var(--danger);border-color:var(--danger)}
  button.danger:hover{background:var(--danger);color:#08110b}
  button.amber{color:var(--amber);border-color:var(--amber)}
  button.amber:hover{background:var(--amber);color:#08110b}
  .row{display:flex;flex-wrap:wrap;gap:14px;align-items:flex-end}
  .status{font-size:14px;margin:2px 0}
  .dot{display:inline-block;width:10px;height:10px;border-radius:50%;margin-right:6px;vertical-align:middle}
  .up{background:var(--accent)} .down{background:var(--danger)}
  .k{color:var(--dim);font-size:12px;text-transform:uppercase;letter-spacing:.03em}
  pre{background:#08110b;border:1px solid var(--line);border-radius:6px;padding:10px;max-height:340px;overflow:auto;font-size:12px;line-height:1.5;white-space:pre-wrap;word-break:break-word}
  a{color:var(--accent)}
  .msg{color:var(--accent);min-height:18px;font-size:13px;margin-top:8px}
  .hide{display:none}
</style></head><body><div class="wrap">
  <h1>🛠️ Tavern Tales — Bot Control</h1>
  <p class="sub">Start, stop and restart the Discord bot. Runs locally; not exposed to the internet.</p>

  <div class="card" id="loginCard">
    <label>Admin password</label>
    <div class="row">
      <input type="password" id="token" placeholder="ADMIN_TOKEN"/>
      <button onclick="unlock()">Unlock</button>
    </div>
    <div class="msg" id="loginMsg"></div>
  </div>

  <div id="app" class="hide">
    <div class="card">
      <h2>Status</h2>
      <div class="status"><span id="dot" class="dot down"></span><span id="state">…</span></div>
      <div class="status"><span class="k">Deployed build:</span> <span id="build">—</span></div>
      <div class="status"><span class="k">Uptime:</span> <span id="uptime">—</span> · <span class="k">Restarts:</span> <span id="restarts">0</span> · <span class="k">PID:</span> <span id="pid">—</span></div>
      <div class="row" style="margin-top:12px">
        <button class="amber" onclick="act('restart')">🔄 Restart Bot</button>
        <button onclick="act('start')">▶ Start</button>
        <button class="danger" onclick="act('stop')">■ Stop</button>
      </div>
      <div class="msg" id="msg"></div>
      <div style="margin-top:10px;font-size:13px" id="links"></div>
    </div>

    <div class="card">
      <h2>Log</h2>
      <pre id="log">…</pre>
    </div>

    <div class="card">
      <h2>Game Data Editor</h2>
      <p class="sub" style="margin:0 0 10px">Edit everything about Tavern Tales — items, skills, classes, races, zones, rarities, affixes, monsters, recipes and the gathering tables. Add new entries with the template button. Edits apply after a bot restart.</p>
      <div class="row">
        <div>
          <label>Section</label>
          <select id="ds" onchange="loadSection()"></select>
        </div>
        <button onclick="loadSection()">↻ Reload</button>
        <button onclick="addTemplate()">＋ Add entry</button>
      </div>
      <label style="margin-top:10px">JSON <span id="dcount" class="k"></span></label>
      <textarea id="djson" spellcheck="false" style="width:100%;height:360px;background:#08110b;color:var(--ink);border:1px solid var(--line);border-radius:6px;padding:10px;font-family:Consolas,'Courier New',monospace;font-size:12px;white-space:pre;overflow:auto"></textarea>
      <div class="row" style="margin-top:10px">
        <button onclick="validateJson()">✓ Validate</button>
        <button class="amber" onclick="saveData(false)">💾 Save</button>
        <button class="amber" onclick="saveData(true)">💾 Save &amp; Restart</button>
      </div>
      <div class="msg" id="dmsg"></div>
      <p class="sub" style="margin-top:6px">Every save backs up the old file to <b>data-backups/</b>. Keep <b>id</b>s unique. Broken JSON is rejected — the game keeps running the last good data until you restart.</p>
    </div>
  </div>

<script>
var TOKEN='';
function h(){ return {'x-admin-token':TOKEN}; }
function unlock(){
  TOKEN=document.getElementById('token').value.trim();
  fetch('/api/status',{headers:h()}).then(function(r){
    if(!r.ok){ document.getElementById('loginMsg').textContent='Wrong password.'; return; }
    document.getElementById('loginCard').classList.add('hide');
    document.getElementById('app').classList.remove('hide');
    tick(); tickLog(); loadSections(); setInterval(tick,2000); setInterval(tickLog,2000);
  });
}
function fmt(s){ if(!s) return '0s'; var m=Math.floor(s/60), h=Math.floor(m/60); if(h) return h+'h '+(m%60)+'m'; if(m) return m+'m '+(s%60)+'s'; return s+'s'; }
function tick(){
  fetch('/api/status',{headers:h()}).then(function(r){return r.json();}).then(function(d){
    document.getElementById('dot').className='dot '+(d.running?'up':'down');
    document.getElementById('state').textContent=d.running?('Running on port '+d.botPort):(d.desired==='stopped'?'Stopped':'Down — restarting…');
    document.getElementById('build').textContent=d.build||'(bot not answering /health yet)';
    document.getElementById('uptime').textContent=fmt(d.uptimeSec);
    document.getElementById('restarts').textContent=d.restarts;
    document.getElementById('pid').textContent=d.pid||'—';
    var base='http://localhost:'+d.botPort;
    document.getElementById('links').innerHTML='🔗 <a href="'+base+'/admin" target="_blank">Player Editor</a> &nbsp; <a href="'+base+'/guide" target="_blank">Player Guide</a>';
  }).catch(function(){});
}
function tickLog(){
  fetch('/api/logs',{headers:h()}).then(function(r){return r.json();}).then(function(d){
    var el=document.getElementById('log'); var atBottom=el.scrollTop+el.clientHeight>=el.scrollHeight-8;
    el.textContent=(d.lines||[]).join('\\n'); if(atBottom) el.scrollTop=el.scrollHeight;
  }).catch(function(){});
}
function act(a){
  var m=document.getElementById('msg'); m.textContent='Working…';
  fetch('/api/'+a,{method:'POST',headers:h()}).then(function(r){return r.json();}).then(function(){
    m.textContent=(a==='restart'?'🔄 Restart triggered.':a==='start'?'▶ Start triggered.':'■ Stop triggered.');
    setTimeout(tick,1500);
  }).catch(function(){ m.textContent='Failed.'; });
}
// ── Game Data editor ──
function loadSections(){
  fetch('/api/data/sections',{headers:h()}).then(function(r){return r.json();}).then(function(d){
    var sel=document.getElementById('ds'); var prev=sel.value; sel.innerHTML='';
    (d.sections||[]).forEach(function(s){ var o=document.createElement('option'); o.value=s.key; o.textContent=s.label+(s.count!=null?' ('+s.count+')':''); sel.appendChild(o); });
    if(prev) sel.value=prev;
    if(!document.getElementById('djson').value) loadSection();
  });
}
function loadSection(){
  var key=document.getElementById('ds').value; if(!key) return;
  document.getElementById('dmsg').textContent='Loading…';
  fetch('/api/data?section='+encodeURIComponent(key),{headers:h()}).then(function(r){return r.json();}).then(function(d){
    if(d.error){ document.getElementById('dmsg').textContent=d.error; return; }
    document.getElementById('djson').value=d.json;
    document.getElementById('dcount').textContent='· '+d.count+' · '+d.file;
    document.getElementById('dmsg').textContent='';
  });
}
function validateJson(){
  try{ JSON.parse(document.getElementById('djson').value); document.getElementById('dmsg').textContent='✓ Valid JSON.'; return true; }
  catch(e){ document.getElementById('dmsg').textContent='✗ '+e.message; return false; }
}
function addTemplate(){
  var key=document.getElementById('ds').value;
  fetch('/api/data/template?section='+encodeURIComponent(key),{headers:h()}).then(function(r){return r.json();}).then(function(d){
    var ta=document.getElementById('djson'); var arr;
    try{ arr=JSON.parse(ta.value); }catch(e){ document.getElementById('dmsg').textContent='Fix JSON first: '+e.message; return; }
    if(!Array.isArray(arr)){ document.getElementById('dmsg').textContent='Add-entry only works on list sections.'; return; }
    arr.push(d.template||{}); ta.value=JSON.stringify(arr,null,2);
    ta.scrollTop=ta.scrollHeight; document.getElementById('dmsg').textContent='＋ New entry appended at the bottom — edit its fields (esp. id/name), then Save.';
  });
}
function saveData(restart){
  if(!validateJson()) return;
  var key=document.getElementById('ds').value;
  document.getElementById('dmsg').textContent='Saving…';
  fetch('/api/data',{method:'POST',headers:Object.assign({'Content-Type':'application/json'},h()),body:JSON.stringify({section:key,json:document.getElementById('djson').value,restart:!!restart})})
    .then(function(r){return r.json();}).then(function(d){
      if(d.error){ document.getElementById('dmsg').textContent='✗ '+d.error; return; }
      document.getElementById('dmsg').textContent='✓ Saved '+d.count+' entries'+(d.restarted?' — bot restarting…':' — click Restart (or Save & Restart) to apply.');
      loadSections();
    }).catch(function(){ document.getElementById('dmsg').textContent='Save failed.'; });
}
</script>
</div></body></html>`;
