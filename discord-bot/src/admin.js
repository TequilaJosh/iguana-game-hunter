import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { config } from './config.js';
import { getPlayer, savePlayer, allPlayers } from './game/store.js';
import { ITEMS, RARITIES, ZONE_LIST, CLASS_LIST } from './game/content.js';
import { makeGear } from './game/engine.js';
import { giveStack } from './game/invutil.js';
import { log } from './logger.js';

const GEAR_SLOTS = new Set(['weapon', 'head', 'body', 'shield', 'feet', 'accessory']);

// Gathered materials (not in ITEMS) — for gifting raw crafting mats.
const dir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'game-data');
let GATHER = [];
try { GATHER = JSON.parse(fs.readFileSync(path.join(dir, 'gather-materials.json'), 'utf8')); } catch { GATHER = []; }
const GATHER_BY_KEY = Object.fromEntries(GATHER.map((m) => [m.key, m]));

function catalog() {
  const items = [];
  for (const [id, it] of Object.entries(ITEMS)) {
    const kind = GEAR_SLOTS.has(it.slot) ? 'gear' : 'item';
    items.push({ id, name: it.name, kind, slot: it.slot });
  }
  for (const m of GATHER) items.push({ id: m.key, name: m.name, kind: 'material', slot: 'material' });
  items.sort((a, b) => a.kind.localeCompare(b.kind) || a.name.localeCompare(b.name));
  return {
    items,
    rarities: RARITIES.map((r) => ({ id: r.id, name: r.name || r.id })),
    zones: ZONE_LIST.map((z) => ({ id: z.id, name: z.name })),
  };
}

function summarizeInventory(char) {
  return (char.inventory || []).map((i, idx) => ({
    idx,
    name: i.name || i.base,
    slot: i.slot,
    qty: i.qty || 1,
    rarity: i.rarity || '',
    gear: GEAR_SLOTS.has(i.slot),
  }));
}

/** Add routes for the web player-editor to an existing Express app. */
export function mountAdmin(app) {
  // Gate every /admin/api/* call on the shared token.
  app.use('/admin/api', (req, res, next) => {
    if (!config.adminToken) return res.status(503).json({ error: 'Admin panel disabled — set ADMIN_TOKEN in the bot .env.' });
    const t = (req.get('x-admin-token') || (req.get('authorization') || '').replace(/^Bearer\s+/i, '') || req.query.token || '').toString();
    if (t !== config.adminToken) return res.status(401).json({ error: 'Wrong password.' });
    next();
  });

  app.get('/admin', (_req, res) => res.type('html').send(ADMIN_HTML));

  app.get('/admin/api/catalog', (_req, res) => res.json(catalog()));

  app.get('/admin/api/players', (_req, res) => {
    const rows = Object.entries(allPlayers())
      .filter(([, c]) => c)
      .map(([id, c]) => ({ id, name: c.name || '(unnamed)', level: c.level || 1, cls: c.cls, race: c.race }))
      .sort((a, b) => a.name.localeCompare(b.name));
    res.json(rows);
  });

  app.get('/admin/api/player/:id', (req, res) => {
    const c = getPlayer(req.params.id);
    if (!c) return res.status(404).json({ error: 'No such player.' });
    res.json({
      id: req.params.id, name: c.name, cls: c.cls, race: c.race,
      level: c.level || 1, xp: c.xp || 0, gold: c.gold || 0,
      stamina: c.stamina ?? 20, ascension: c.ascension || 0,
      cleared: c.cleared || {},
      inventory: summarizeInventory(c),
      professions: c.professions || {},
    });
  });

  // Save core stats + cleared zones.
  app.post('/admin/api/player/:id', (req, res) => {
    const c = getPlayer(req.params.id);
    if (!c) return res.status(404).json({ error: 'No such player.' });
    const b = req.body || {};
    const num = (v, cur) => (Number.isFinite(+v) ? Math.round(+v) : cur);
    c.level = Math.max(1, num(b.level, c.level));
    c.xp = Math.max(0, num(b.xp, c.xp || 0));
    c.gold = Math.max(0, num(b.gold, c.gold || 0));
    c.stamina = Math.max(0, num(b.stamina, c.stamina ?? 20));
    c.ascension = Math.max(0, num(b.ascension, c.ascension || 0));
    if (b.cleared && typeof b.cleared === 'object') {
      c.cleared = {};
      for (const z of ZONE_LIST) if (b.cleared[z.id]) c.cleared[z.id] = true;
    }
    savePlayer(req.params.id, c);
    log.info(`Admin edited ${c.name} (${req.params.id}) stats/cleared.`);
    res.json({ ok: true });
  });

  // Give an item (gift).
  app.post('/admin/api/player/:id/give', (req, res) => {
    const c = getPlayer(req.params.id);
    if (!c) return res.status(404).json({ error: 'No such player.' });
    const { kind, id, rarity, qty } = req.body || {};
    const n = Math.max(1, Math.min(999, Math.round(+qty || 1)));
    c.inventory = c.inventory || [];
    try {
      if (kind === 'gear') {
        const base = ITEMS[id];
        if (!base) return res.status(400).json({ error: 'Unknown gear.' });
        const r = RARITIES.find((x) => x.id === rarity) || RARITIES[0];
        const g = makeGear(id, r);
        if (!g) return res.status(400).json({ error: 'Could not make that gear.' });
        c.inventory.push(g);
      } else if (kind === 'material') {
        const m = GATHER_BY_KEY[id];
        if (!m) return res.status(400).json({ error: 'Unknown material.' });
        const ex = c.inventory.find((i) => i.base === id && i.slot === 'material');
        if (ex) ex.qty = (ex.qty || 1) + n;
        else c.inventory.push({ base: id, slot: 'material', name: m.name, qty: n, stackable: true, value: (m.tier || 1) * 6 });
      } else {
        if (!ITEMS[id]) return res.status(400).json({ error: 'Unknown item.' });
        giveStack(c, id, n);
      }
    } catch (e) { return res.status(500).json({ error: e.message }); }
    savePlayer(req.params.id, c);
    log.info(`Admin gave ${c.name} ${n}x ${id} (${kind}).`);
    res.json({ ok: true, inventory: summarizeInventory(c) });
  });

  // Remove one inventory entry by index.
  app.post('/admin/api/player/:id/removeitem', (req, res) => {
    const c = getPlayer(req.params.id);
    if (!c) return res.status(404).json({ error: 'No such player.' });
    const idx = Math.round(+(req.body?.idx));
    if (!Array.isArray(c.inventory) || idx < 0 || idx >= c.inventory.length) return res.status(400).json({ error: 'Bad index.' });
    const removed = c.inventory.splice(idx, 1)[0];
    savePlayer(req.params.id, c);
    log.info(`Admin removed ${removed?.name} from ${c.name}.`);
    res.json({ ok: true, inventory: summarizeInventory(c) });
  });

  log.info(config.adminToken ? 'Player-editor panel available at /admin' : 'Player-editor panel /admin is disabled (no ADMIN_TOKEN).');
}

const ADMIN_HTML = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>Tavern Tales — Player Editor</title>
<style>
  :root{--bg:#0c140f;--card:#101c14;--accent:#7cc44a;--ink:#e8e0c4;--dim:#8aa07c;--line:#254a2e;--danger:#d46a6a;}
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--ink);font-family:'Segoe UI',system-ui,Arial,sans-serif}
  .wrap{max-width:820px;margin:0 auto;padding:20px 16px 60px}
  h1{color:var(--accent);font-size:24px;margin:0 0 4px}
  .sub{color:var(--dim);margin:0 0 16px;font-size:13px}
  .card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px 16px;margin:12px 0}
  h2{color:var(--accent);font-size:15px;margin:0 0 10px;text-transform:uppercase;letter-spacing:.04em}
  label{display:block;color:var(--dim);font-size:11px;text-transform:uppercase;margin:8px 0 3px}
  input,select{background:#08110b;color:var(--ink);border:1px solid var(--line);border-radius:5px;padding:6px 8px;font-size:14px}
  input[type=number]{width:110px}
  button{background:#14241c;color:var(--accent);border:1px solid var(--accent);border-radius:5px;padding:7px 14px;font-weight:700;cursor:pointer;font-size:13px}
  button:hover{background:var(--accent);color:#08110b}
  button.danger{color:var(--danger);border-color:var(--danger)}
  button.danger:hover{background:var(--danger);color:#08110b}
  .row{display:flex;flex-wrap:wrap;gap:14px;align-items:flex-end}
  .zones{display:flex;flex-wrap:wrap;gap:6px 16px;margin-top:4px}
  .zones label{display:inline-flex;align-items:center;gap:5px;text-transform:none;font-size:13px;color:var(--ink);margin:0}
  table{width:100%;border-collapse:collapse}
  td,th{text-align:left;padding:5px 6px;border-bottom:1px solid rgba(37,74,46,.5);font-size:13px}
  th{color:var(--dim);font-size:11px;text-transform:uppercase}
  .status{color:var(--accent);font-size:13px;min-height:18px;margin-top:8px}
  .hide{display:none}
  .pill{display:inline-block;padding:1px 8px;border-radius:10px;font-size:11px;border:1px solid var(--line);color:var(--dim)}
</style></head><body><div class="wrap">
  <h1>🛠️ Tavern Tales — Player Editor</h1>
  <p class="sub">Fix stuck players, hand out gifts, and edit stats. Edits go through the live bot.</p>

  <div class="card" id="loginCard">
    <label>Admin password</label>
    <div class="row">
      <input type="password" id="token" placeholder="ADMIN_TOKEN"/>
      <button onclick="loadPlayers()">Unlock</button>
    </div>
    <div class="status" id="loginStatus"></div>
  </div>

  <div id="editor" class="hide">
    <div class="card">
      <label>Player</label>
      <div class="row">
        <select id="player" style="min-width:280px" onchange="loadPlayer()"></select>
        <button onclick="loadPlayers()">↻ Refresh list</button>
      </div>
      <div class="sub" id="who"></div>
    </div>

    <div class="card" id="statsCard">
      <h2>Stats</h2>
      <div class="row">
        <div><label>Level</label><input type="number" id="level"/></div>
        <div><label>XP</label><input type="number" id="xp"/></div>
        <div><label>Gold</label><input type="number" id="gold"/></div>
        <div><label>Stamina</label><input type="number" id="stamina"/></div>
        <div><label>Ascension</label><input type="number" id="ascension"/></div>
      </div>
      <label>Zones cleared (tick to unlock the next zone)</label>
      <div class="zones" id="zones"></div>
      <div class="row" style="margin-top:12px"><button onclick="saveStats()">Save stats</button></div>
      <div class="status" id="statsStatus"></div>
    </div>

    <div class="card">
      <h2>Give item (gift)</h2>
      <div class="row">
        <div><label>Item</label><select id="giveItem" style="min-width:280px" onchange="onGiveKind()"></select></div>
        <div id="rarityWrap"><label>Rarity</label><select id="rarity"></select></div>
        <div id="qtyWrap"><label>Qty</label><input type="number" id="qty" value="1" min="1"/></div>
        <button onclick="giveItem()">Give</button>
      </div>
      <div class="status" id="giveStatus"></div>
    </div>

    <div class="card">
      <h2>Inventory</h2>
      <table><thead><tr><th>Item</th><th>Slot</th><th>Qty</th><th></th></tr></thead><tbody id="inv"></tbody></table>
    </div>
  </div>

<script>
var TOKEN="", CAT=null, PID="";
function H(){return {"Content-Type":"application/json","x-admin-token":TOKEN};}
function esc(s){return String(s==null?"":s).replace(/[&<>]/g,function(c){return{"&":"&amp;","<":"&lt;",">":"&gt;"}[c];});}
async function api(url,opts){opts=opts||{};opts.headers=H();const r=await fetch(url,opts);const j=await r.json().catch(function(){return{};});if(!r.ok)throw new Error(j.error||("HTTP "+r.status));return j;}

async function loadPlayers(){
  TOKEN=document.getElementById("token").value.trim();
  var st=document.getElementById("loginStatus");
  try{
    if(!CAT) CAT=await api("/admin/api/catalog");
    var rows=await api("/admin/api/players");
    var sel=document.getElementById("player");
    sel.innerHTML=rows.map(function(p){return '<option value="'+esc(p.id)+'">'+esc(p.name)+' — Lv '+p.level+' '+esc(p.cls||"")+'</option>';}).join("");
    fillCatalog();
    document.getElementById("loginCard").classList.add("hide");
    document.getElementById("editor").classList.remove("hide");
    if(rows.length) loadPlayer();
  }catch(e){ st.textContent="⚠ "+e.message; }
}

function fillCatalog(){
  var gi=document.getElementById("giveItem");
  var groups={gear:"Gear",item:"Potions / Items",material:"Materials"};
  var html="";
  ["gear","item","material"].forEach(function(k){
    var list=CAT.items.filter(function(i){return i.kind===k;});
    if(!list.length)return;
    html+='<optgroup label="'+groups[k]+'">'+list.map(function(i){return '<option value="'+esc(i.id)+'" data-kind="'+i.kind+'">'+esc(i.name)+'</option>';}).join("")+'</optgroup>';
  });
  gi.innerHTML=html;
  document.getElementById("rarity").innerHTML=CAT.rarities.map(function(r){return '<option value="'+esc(r.id)+'">'+esc(r.name)+'</option>';}).join("");
  onGiveKind();
}
function onGiveKind(){
  var opt=document.getElementById("giveItem").selectedOptions[0];
  var kind=opt?opt.dataset.kind:"item";
  document.getElementById("rarityWrap").style.display=kind==="gear"?"":"none";
  document.getElementById("qtyWrap").style.display=kind==="gear"?"none":"";
}

async function loadPlayer(){
  PID=document.getElementById("player").value;
  var st=document.getElementById("statsStatus");st.textContent="";
  try{
    var c=await api("/admin/api/player/"+encodeURIComponent(PID));
    document.getElementById("who").textContent=c.name+" · "+(c.race||"")+" "+(c.cls||"");
    document.getElementById("level").value=c.level;
    document.getElementById("xp").value=c.xp;
    document.getElementById("gold").value=c.gold;
    document.getElementById("stamina").value=c.stamina;
    document.getElementById("ascension").value=c.ascension;
    document.getElementById("zones").innerHTML=CAT.zones.map(function(z){
      var on=c.cleared&&c.cleared[z.id]?"checked":"";
      return '<label><input type="checkbox" class="zc" value="'+esc(z.id)+'" '+on+'/> '+esc(z.name)+'</label>';
    }).join("");
    renderInv(c.inventory);
  }catch(e){ st.textContent="⚠ "+e.message; }
}

function renderInv(inv){
  document.getElementById("inv").innerHTML=(inv||[]).map(function(i){
    return '<tr><td>'+esc(i.name)+(i.rarity?' <span class="pill">'+esc(i.rarity)+'</span>':'')+'</td><td>'+esc(i.slot)+'</td><td>'+i.qty+'</td>'+
      '<td><button class="danger" onclick="removeItem('+i.idx+')">Remove</button></td></tr>';
  }).join("") || '<tr><td colspan="4" style="color:var(--dim)">Empty</td></tr>';
}

async function saveStats(){
  var st=document.getElementById("statsStatus");
  var cleared={};document.querySelectorAll(".zc").forEach(function(cb){if(cb.checked)cleared[cb.value]=true;});
  try{
    await api("/admin/api/player/"+encodeURIComponent(PID),{method:"POST",body:JSON.stringify({
      level:+document.getElementById("level").value,xp:+document.getElementById("xp").value,
      gold:+document.getElementById("gold").value,stamina:+document.getElementById("stamina").value,
      ascension:+document.getElementById("ascension").value,cleared:cleared})});
    st.textContent="✓ Saved.";
  }catch(e){ st.textContent="⚠ "+e.message; }
}

async function giveItem(){
  var st=document.getElementById("giveStatus");
  var opt=document.getElementById("giveItem").selectedOptions[0];
  if(!opt){st.textContent="Pick an item.";return;}
  try{
    var r=await api("/admin/api/player/"+encodeURIComponent(PID)+"/give",{method:"POST",body:JSON.stringify({
      kind:opt.dataset.kind,id:opt.value,rarity:document.getElementById("rarity").value,qty:+document.getElementById("qty").value})});
    renderInv(r.inventory);
    st.textContent="✓ Gave "+opt.textContent+".";
  }catch(e){ st.textContent="⚠ "+e.message; }
}

async function removeItem(idx){
  try{ var r=await api("/admin/api/player/"+encodeURIComponent(PID)+"/removeitem",{method:"POST",body:JSON.stringify({idx:idx})}); renderInv(r.inventory); }
  catch(e){ alert(e.message); }
}
</script>
</div></body></html>`;
