import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { config } from './config.js';
import { log } from './logger.js';
import { getPlayer } from './game/store.js';
import { CLASSES, RACES, CLASS_LIST, RACE_LIST, ZONE_LIST, STAT_KEYS, skillsForClass } from './game/content.js';
import { derive, xpToNext, shopInventory, sellValue, gearScore, isZoneUnlocked } from './game/engine.js';
import { getFight } from './game/fights.js';
import { PROFESSIONS } from './game/professions.js';
import { WORKER_COMMANDS } from './game/gather.js';
import { runForChat } from './game/rpg.js';

const GEAR_SLOTS = new Set(['weapon', 'head', 'body', 'shield', 'feet', 'accessory']);
const EQUIP_SLOTS = ['weapon', 'head', 'body', 'shield', 'feet', 'accessory'];
const PROF_KEYS = Object.keys(PROFESSIONS);
const MAX_STAMINA = 20;

// ── Web-play tokens ─────────────────────────────────────────────────────────
// A per-player secret that lets the browser act AS that hero. Players fetch their
// own link with `tt web` in chat; anyone holding the link can play that character,
// so it's treated like a password (never shown publicly).
const TOKEN_FILE = path.join(config.dataDir, 'webtokens.json');
let tokens = {}; // token -> discordId
try { tokens = JSON.parse(fs.readFileSync(TOKEN_FILE, 'utf8')) || {}; } catch { tokens = {}; }

function saveTokens() {
  try {
    fs.mkdirSync(config.dataDir, { recursive: true });
    fs.writeFileSync(TOKEN_FILE, JSON.stringify(tokens, null, 2));
  } catch (e) { log.error('Failed to persist web tokens:', e.message); }
}

function tokenFor(discordId) {
  for (const [t, id] of Object.entries(tokens)) if (id === discordId) return t;
  const t = crypto.randomBytes(24).toString('base64url');
  tokens[t] = discordId;
  saveTokens();
  return t;
}
function resolveToken(t) {
  if (!t) return null;
  if (tokens[t]) return tokens[t];
  // Miss: a token may have been minted by another process/instance — reload once.
  try {
    const disk = JSON.parse(fs.readFileSync(TOKEN_FILE, 'utf8')) || {};
    if (disk[t]) { tokens = disk; return tokens[t]; }
  } catch { /* no file yet */ }
  return null;
}

/** A player's personal, write-capable play link (or null if no public URL is set). */
export const playUrl = (discordId) => {
  if (!config.publicUrl || !discordId) return null;
  return `${config.publicUrl.replace(/\/+$/, '')}/play?t=${tokenFor(discordId)}`;
};

// ── State snapshot the browser renders from ─────────────────────────────────
function buildState(discordId) {
  const c = getPlayer(discordId);
  if (!c) {
    return {
      hasChar: false,
      classes: CLASS_LIST.map((x) => ({ id: x.id, name: x.name, blurb: x.blurb || '' })),
      races: RACE_LIST.map((x) => ({ id: x.id, name: x.name, blurb: x.blurb || '' })),
    };
  }
  const pd = derive(c);
  const cls = CLASSES[c.cls], race = RACES[c.race];
  const inv = c.inventory || [];
  const gear = inv.filter((i) => GEAR_SLOTS.has(i.slot));
  const fight = getFight(discordId);
  const shop = shopInventory(c);

  const skills = skillsForClass(c.cls, c.level).map((s, i) => ({
    n: i + 1, name: s.name, mp: s.mp, type: s.type,
    affordable: (fight ? fight.pmp : (c.mp ?? pd.maxmp)) >= s.mp,
  }));

  return {
    hasChar: true,
    name: c.name, cls: c.cls, className: cls?.name || c.cls, raceName: race?.name || c.race,
    level: c.level || 1, xp: c.xp || 0, xpNext: xpToNext(c.level || 1),
    gold: c.gold || 0, ascension: c.ascension || 0,
    hp: c.hp ?? pd.maxhp, maxhp: pd.maxhp, mp: c.mp ?? pd.maxmp, maxmp: pd.maxmp,
    stamina: c.stamina ?? MAX_STAMINA, maxStamina: MAX_STAMINA,
    power: Math.round(pd.st[pd.scales] * 1.3 + pd.wpow * 1.4), def: pd.def, res: pd.res,
    stats: STAT_KEYS.map((k) => ({ k: k.toUpperCase(), v: pd.st[k] })),
    skills,
    inFight: !!fight,
    fight: fight ? {
      monster: fight.monster?.name || 'Monster', emoji: fight.monster?.emoji || '👹',
      mhp: Math.max(0, fight.mhp), mmaxhp: fight.mmaxhp,
      php: Math.max(0, fight.php), pmaxhp: pd.maxhp, pmp: fight.pmp, pmaxmp: pd.maxmp,
      turn: fight.turn, log: (fight.log || []).slice(-6),
    } : null,
    equipped: EQUIP_SLOTS.map((s) => ({ slot: s, name: c.equipped?.[s]?.name || null, rarity: c.equipped?.[s]?.rarity || '' })),
    gear: gear.map((i, idx) => ({ n: idx + 1, name: i.name, slot: i.slot, rarity: i.rarity || '', score: gearScore(i) })),
    bag: inv.filter((i) => !GEAR_SLOTS.has(i.slot))
      .map((i) => ({ name: i.name, qty: i.qty || 1, slot: i.slot, potion: i.effect === 'heal_pct' })),
    shop: shop.map((i, idx) => ({
      n: idx + 1, name: i.name, price: i.price ?? i.value ?? 0, slot: i.slot, rarity: i.rarity || '',
      affordable: (c.gold || 0) >= (i.price ?? i.value ?? 0),
    })),
    zones: ZONE_LIST.map((z) => ({ name: z.name, unlocked: isZoneUnlocked(c, z), cleared: !!c.cleared?.[z.id] })),
    // Remaining gather cooldown per worker action (ms, 0 = ready) so the browser can
    // show a live countdown over each button.
    gatherCd: (() => {
      const now = Date.now(), cd = c.gatherCd || {};
      const o = {};
      for (const k of WORKER_COMMANDS) o[k] = Math.max(0, (cd[k] || 0) - now);
      return o;
    })(),
    professions: PROF_KEYS.map((k) => ({ name: PROFESSIONS[k].name, emoji: PROFESSIONS[k].emoji, level: (c.professions?.[k]?.level) || 1 }))
      .filter((p) => p.level > 1 || p.name === 'Worker'),
  };
}

/** Playable browser client at /play. Drives the SAME engine as chat via runForChat. */
export function mountPlay(app, client) {
  app.get('/play', (_req, res) => res.type('html').send(PLAY_HTML));

  // Every /play/api call carries the player's token (query ?t= or x-play-token header).
  const gate = (req, res, next) => {
    const t = req.query.t || req.get('x-play-token') || '';
    const id = resolveToken(t);
    if (!id) return res.status(401).json({ error: 'Bad or missing play link. Type `tt web` in chat to get yours.' });
    req.discordId = id;
    next();
  };

  app.get('/play/api/state', gate, (req, res) => res.json(buildState(req.discordId)));

  app.post('/play/api/cmd', gate, express_json_guard, async (req, res) => {
    const command = String(req.body?.command || '').trim().slice(0, 200);
    if (!command) return res.status(400).json({ error: 'no command' });
    const c = getPlayer(req.discordId);
    let reply = null;
    try {
      reply = await runForChat({
        discordId: req.discordId,
        username: c?.name || 'Adventurer',
        content: 'tt ' + command,
        guildId: undefined,
        client,
      });
    } catch (e) {
      log.error('play cmd failed:', e);
      return res.status(500).json({ error: 'Something went wrong running that.' });
    }
    res.json({ reply: reply || '…', state: buildState(req.discordId) });
  });
}

// express.json is already mounted app-wide in ingest.js, so req.body is parsed —
// this just guards against a missing body object.
function express_json_guard(req, _res, next) { req.body = req.body || {}; next(); }

const PLAY_HTML = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>Tavern Tales — Play</title>
<style>
  :root{--accent:#7cc44a;--ink:#e8e0c4;--dim:#93a888;--panel:#0c1610;--line:#23331f;--gold:#f0c84a;}
  *{box-sizing:border-box}
  body{margin:0;min-height:100vh;color:var(--ink);font-family:'Segoe UI',system-ui,Arial,sans-serif;
       background:radial-gradient(1200px 700px at 50% -10%, #16241a 0%, #0a120d 55%, #070d09 100%)}
  .wrap{max-width:860px;margin:0 auto;padding:16px 14px 70px}
  h1{color:var(--accent);text-align:center;font-size:20px;margin:4px 0 14px}
  .grid2{display:grid;grid-template-columns:1fr;gap:14px}
  @media(min-width:720px){.grid2{grid-template-columns:1.05fr .95fr}}
  .card{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:14px}
  .card.hero{border-color:var(--accent);box-shadow:inset 0 0 60px -30px var(--accent)}
  .row{display:flex;align-items:center;gap:10px}
  .emoji{font-size:38px;line-height:1}
  .name{font-size:20px;font-weight:800;color:#fff}
  .sub{color:var(--accent);font-weight:700;font-size:13px}
  .stars{color:var(--gold)}
  .barlab{display:flex;justify-content:space-between;font-size:11px;color:var(--dim);margin:9px 0 3px;text-transform:uppercase;letter-spacing:.05em}
  .bar{height:12px;border-radius:8px;background:#0a140e;border:1px solid var(--line);overflow:hidden}
  .fill{height:100%;border-radius:8px;transition:width .25s}
  .fill.hp{background:#d46a6a}.fill.mp{background:#5a9ad4}.fill.xp{background:var(--accent)}.fill.stam{background:#c8a24a}
  .grid{display:grid;grid-template-columns:repeat(3,1fr);gap:7px;margin-top:12px}
  .stat{background:#0a140e;border:1px solid var(--line);border-radius:9px;padding:7px;text-align:center}
  .stat b{display:block;font-size:16px;color:#fff}.stat span{font-size:9px;color:var(--dim);text-transform:uppercase}
  h3{font-size:11px;color:var(--accent);text-transform:uppercase;letter-spacing:.06em;margin:16px 0 7px}
  .btns{display:flex;flex-wrap:wrap;gap:7px}
  button{background:#13251a;color:var(--ink);border:1px solid #2c5233;border-radius:9px;
         padding:9px 12px;font-size:13px;font-weight:600;cursor:pointer;transition:.12s}
  button:hover:not(:disabled){background:#1b3524;border-color:var(--accent);transform:translateY(-1px)}
  button:disabled{opacity:.4;cursor:not-allowed}
  button.p{background:var(--accent);color:#0a140e;border-color:var(--accent)}
  button.atk{background:#3a1c1c;border-color:#7a3a3a}button.atk:hover:not(:disabled){background:#4d2424}
  button.sm{padding:6px 9px;font-size:12px}
  button.on{background:var(--accent);color:#0a140e;border-color:var(--accent)}
  /* Gather button with a cooldown overlay */
  button.gather{position:relative;overflow:hidden}
  button.gather.cooling{color:var(--dim)}
  .cd{position:absolute;inset:0;display:none;align-items:center;justify-content:center;
      background:rgba(8,15,10,.78);color:#f0c84a;font-weight:800;font-size:13px;letter-spacing:.03em}
  button.gather.cooling .cd{display:flex}
  .price{color:var(--gold);font-weight:700}
  .rar-uncommon{color:#6fd06f}.rar-rare{color:#5a9ad4}.rar-epic{color:#b06ad0}.rar-legendary{color:var(--gold)}
  .eqrow{display:flex;justify-content:space-between;font-size:13px;padding:3px 0;border-bottom:1px solid rgba(35,51,31,.5)}
  .eqrow .slot{color:var(--dim);text-transform:capitalize}.eqrow .empty{color:#5f7355}
  .listrow{display:flex;justify-content:space-between;align-items:center;gap:8px;padding:5px 0;border-bottom:1px solid rgba(35,51,31,.5);font-size:13px}
  .chips{display:flex;flex-wrap:wrap;gap:6px}
  .chip{background:#0a140e;border:1px solid var(--line);border-radius:20px;padding:3px 10px;font-size:12px}
  .chip .pill{color:var(--accent);font-weight:700}
  .log{background:#080f0a;border:1px solid var(--line);border-radius:10px;padding:10px 12px;font-size:13px;
       min-height:44px;white-space:pre-wrap;line-height:1.5}
  .foe{display:flex;justify-content:space-between;align-items:center}
  .foe .fn{font-weight:800;color:#fff}
  .tabs{display:flex;gap:6px;flex-wrap:wrap;margin-bottom:10px}
  .tabs button.active{background:var(--accent);color:#0a140e;border-color:var(--accent)}
  .hide{display:none}
  .muted{color:var(--dim);font-size:12px}
  .center{text-align:center}
  .cls-pick{display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:8px}
  .pk{background:#0a140e;border:1px solid var(--line);border-radius:10px;padding:10px;cursor:pointer;text-align:left}
  .pk:hover{border-color:var(--accent)}.pk.sel{border-color:var(--accent);background:#13251a}
  .pk b{color:#fff}.pk small{color:var(--dim);display:block;margin-top:3px;font-size:11px}
  .spin{color:var(--dim)}
  a{color:var(--accent)}
</style></head><body><div class="wrap">
  <h1>🍺 Tavern Tales</h1>
  <div id="root"><div class="center spin">Loading your hero…</div></div>
</div>
<script>
var THEME={
  knight:{c:"#6fb0e0",e:"🛡️"},berserker:{c:"#e06a5a",e:"🪓"},paladin:{c:"#f0cf5a",e:"⚜️"},
  ranger:{c:"#7cc44a",e:"🏹"},rogue:{c:"#a884e0",e:"🗡️"},mage:{c:"#6a9ff0",e:"🔮"},
  cleric:{c:"#f0e6a6",e:"✨"},necromancer:{c:"#9a6ad0",e:"💀"},monk:{c:"#e0a45a",e:"👊"},bard:{c:"#e884b8",e:"🎵"}
};
var TOKEN=new URLSearchParams(location.search).get("t")||"";
var S=null, TAB="adventure", NEWCLS=null, NEWRACE=null, BUSY=false, LASTMSG="";
function esc(s){return String(s==null?"":s).replace(/[&<>]/g,function(c){return{"&":"&amp;","<":"&lt;",">":"&gt;"}[c];});}
function rar(r){return r?'rar-'+String(r).toLowerCase():'';}
function pct(a,b){return Math.max(0,Math.min(100,Math.round(100*(a||0)/Math.max(1,b))));}

async function api(path,opts){
  opts=opts||{}; opts.headers=Object.assign({'x-play-token':TOKEN},opts.headers||{});
  var r=await fetch(path,opts); var d=await r.json().catch(function(){return {};});
  if(!r.ok) throw new Error(d.error||("HTTP "+r.status));
  return d;
}
async function refresh(){ try{ S=await api("/play/api/state"); syncCooldowns(); startCooldownTicker(); render(); }catch(e){ fail(e.message); } }
function fail(m){ document.getElementById("root").innerHTML='<div class="card center">⚠️ '+esc(m)+'</div>'; }

// Send a game command (as if the player typed "tt <cmd>"), then re-render from fresh state.
async function cmd(c){
  if(BUSY) return; BUSY=true; render();
  try{ var d=await api("/play/api/cmd",{method:"POST",headers:{'Content-Type':'application/json'},body:JSON.stringify({command:c})});
       LASTMSG=d.reply||""; S=d.state; syncCooldowns(); }
  catch(e){ LASTMSG="⚠️ "+e.message; }
  BUSY=false; render(); maybeAuto();
}

// ── Auto-attack: a client-side loop that repeats the normal attack command every
// ~0.85s until the fight ends or the player turns it off (replaces Discord's
// message-editing auto-battle, which can't run in the browser). ─────────────────
var AUTO=false, AUTO_TIMER=null;
function toggleAuto(){ AUTO=!AUTO; render(); maybeAuto(); }
function maybeAuto(){
  if(AUTO_TIMER){ clearTimeout(AUTO_TIMER); AUTO_TIMER=null; }
  if(S && !S.inFight){ AUTO=false; return; }        // fight over → stop
  if(AUTO && S && S.inFight && !BUSY){
    AUTO_TIMER=setTimeout(function(){ AUTO_TIMER=null; cmd('attack'); }, 850);
  }
}

function theme(){ var t=THEME[S&&S.cls]||{c:"#7cc44a",e:"🎮"}; document.documentElement.style.setProperty("--accent",t.c); return t; }

function render(){
  if(!S){return;}
  if(!S.hasChar){ return renderCreate(); }
  var t=theme();
  var root=document.getElementById("root");
  var stars=S.ascension?'<span class="stars"> '+"★".repeat(Math.min(5,S.ascension))+(S.ascension>5?"+":"")+'</span>':'';
  var statCells=S.stats.map(function(s){return '<div class="stat"><b>'+s.v+'</b><span>'+esc(s.k)+'</span></div>';}).join("");
  var hero=
   '<div class="card hero">'+
    '<div class="row"><div class="emoji">'+t.e+'</div><div>'+
      '<div class="name">'+esc(S.name)+'</div>'+
      '<div class="sub">Lv '+S.level+' '+esc(S.raceName)+' '+esc(S.className)+stars+'</div>'+
    '</div></div>'+
    bar("HP",S.hp,S.maxhp,"hp")+bar("MP",S.mp,S.maxmp,"mp")+
    bar("XP",S.xp,S.xpNext,"xp")+bar("Stamina",S.stamina,S.maxStamina,"stam")+
    '<div class="grid">'+
      '<div class="stat"><b>'+S.power+'</b><span>Power</span></div>'+
      '<div class="stat"><b>'+S.def+'</b><span>Def</span></div>'+
      '<div class="stat"><b>'+S.res+'</b><span>Res</span></div>'+
    '</div><div class="grid">'+statCells+'</div>'+
    '<div class="barlab" style="margin-top:12px"><span>🪙 '+S.gold+' gold</span><span></span></div>'+
   '</div>';

  var right = S.inFight ? panelFight() : panelTabs();
  root.innerHTML='<div class="grid2">'+hero+'<div>'+logBox()+right+'</div></div>';
}

function bar(label,a,b,cls){
  return '<div class="barlab"><span>'+label+'</span><span>'+a+' / '+b+'</span></div>'+
         '<div class="bar"><div class="fill '+cls+'" style="width:'+pct(a,b)+'%"></div></div>';
}
function logBox(){
  return '<div class="card" style="margin-bottom:14px"><div class="log">'+(BUSY?'<span class="spin">…thinking…</span>':(esc(LASTMSG)||'<span class="muted">Pick an action to begin your adventure.</span>'))+'</div></div>';
}

// ── Combat ──────────────────────────────────────────────────────────────────
function panelFight(){
  var f=S.fight||{};
  var skills=S.skills.map(function(s){
    return '<button class="sm" '+(BUSY||!s.affordable?'disabled':'')+' onclick="cmd(\\'skill '+s.n+'\\')">'+esc(s.name)+' <span class="muted">'+s.mp+'mp</span></button>';
  }).join("");
  var hasPot=S.bag.some(function(b){return b.potion&&b.qty>0;});
  return '<div class="card">'+
    '<div class="foe"><div class="fn">'+(f.emoji||'👹')+' '+esc(f.monster)+'</div><div class="muted">Turn '+f.turn+'</div></div>'+
    bar("Enemy HP",f.mhp,f.mmaxhp,"hp")+
    '<h3>Actions</h3><div class="btns">'+
      '<button class="atk" '+(BUSY?'disabled':'')+' onclick="cmd(\\'attack\\')">🗡️ Attack</button>'+
      '<button class="sm '+(AUTO?'on':'')+'" onclick="toggleAuto()">'+(AUTO?'⏸️ Auto: ON':'▶️ Auto')+'</button>'+
      '<button class="sm" '+(BUSY||!hasPot?'disabled':'')+' onclick="cmd(\\'use\\')">🧪 Potion</button>'+
      '<button class="sm" '+(BUSY?'disabled':'')+' onclick="cmd(\\'flee\\')">🏃 Flee</button>'+
    '</div>'+
    (skills?'<h3>Skills</h3><div class="btns">'+skills+'</div>':'')+
  '</div>';
}

// ── Out of combat: tabbed action panels ─────────────────────────────────────
function panelTabs(){
  var tabs=[["adventure","⚔️ Adventure"],["shop","🛒 Shop"],["bag","🎒 Bag"],["craft","🔨 Craft"],["more","✨ More"]];
  var bar='<div class="tabs">'+tabs.map(function(x){
    return '<button class="'+(TAB===x[0]?'active':'')+'" onclick="setTab(\\''+x[0]+'\\')">'+x[1]+'</button>';
  }).join("")+'</div>';
  return '<div class="card">'+bar+tabBody()+'</div>';
}
function setTab(t){ TAB=t; render(); }

function tabBody(){
  if(TAB==="adventure") return bodyAdventure();
  if(TAB==="shop") return bodyShop();
  if(TAB==="bag") return bodyBag();
  if(TAB==="craft") return bodyCraft();
  return bodyMore();
}
function actBtn(label,c,cls,dis){ return '<button class="'+(cls||'')+'" '+((BUSY||dis)?'disabled':'')+' onclick="cmd(\\''+c+'\\')">'+label+'</button>'; }

// Gather button: disabled + overlaid with a live countdown while on cooldown.
function gatherBtn(label,c){
  var rem=(S.gatherCd&&S.gatherCd[c])||0;
  var cooling=rem>0;
  return '<button id="gb-'+c+'" class="sm gather'+(cooling?' cooling':'')+'" '+((BUSY||cooling)?'disabled':'')+
    ' onclick="cmd(\\''+c+'\\')">'+label+'<span class="cd" id="cd-'+c+'">'+(cooling?fmtCd(rem):'')+'</span></button>';
}
function fmtCd(ms){ var s=Math.ceil(ms/1000); return Math.floor(s/60)+':'+('0'+(s%60)).slice(-2); }

// Turn the per-action remaining-ms from state into absolute local target times, so
// the countdown keeps ticking between server fetches without clock-skew issues.
var GCD_TARGET={};
function syncCooldowns(){
  if(!S||!S.gatherCd) return;
  var now=Date.now();
  for(var k in S.gatherCd){ var rem=S.gatherCd[k]; if(rem>0) GCD_TARGET[k]=now+rem; else delete GCD_TARGET[k]; }
}
// One shared 1s ticker updates the gather overlays in place (no full re-render).
var COOLDOWN_TICKER=null;
function startCooldownTicker(){
  if(COOLDOWN_TICKER) return;
  COOLDOWN_TICKER=setInterval(function(){
    if(BUSY) return;
    for(var k in GCD_TARGET){
      var btn=document.getElementById('gb-'+k), el=document.getElementById('cd-'+k);
      var rem=(GCD_TARGET[k]||0)-Date.now();
      if(rem>0){ if(el)el.textContent=fmtCd(rem); if(btn){btn.classList.add('cooling');btn.disabled=true;} }
      else { delete GCD_TARGET[k]; if(el)el.textContent=''; if(btn){btn.classList.remove('cooling');btn.disabled=false;} }
    }
  },1000);
}

function bodyAdventure(){
  var zonesCleared=S.zones.filter(function(z){return z.cleared;}).length;
  return '<div class="btns">'+
      actBtn("⚔️ Adventure","adventure","p")+
      actBtn("👑 Boss","boss")+
      actBtn("🛌 Rest","rest")+
    '</div>'+
    '<h3>Gather</h3><div class="btns">'+
      gatherBtn("🪓 Chop","chop")+gatherBtn("⛏️ Mine","mine")+gatherBtn("🎣 Fish","fish")+
      gatherBtn("🌿 Forage","forage")+gatherBtn("🪏 Dig","dig")+gatherBtn("🔦 Scavenge","scavenge")+
    '</div>'+
    '<h3>Zones ('+zonesCleared+'/'+S.zones.length+' cleared)</h3><div class="chips">'+
      S.zones.map(function(z){return '<span class="chip">'+(z.cleared?'✅':(z.unlocked?'🔓':'🔒'))+' '+esc(z.name)+'</span>';}).join("")+
    '</div>';
}

function bodyShop(){
  if(!S.shop.length) return '<div class="muted">The shop is empty right now.</div>';
  var rows=S.shop.map(function(it){
    return '<div class="listrow"><span class="'+rar(it.rarity)+'">'+esc(it.name)+' <span class="muted">'+esc(it.slot)+'</span></span>'+
      '<span><span class="price">🪙 '+it.price+'</span> '+
      '<button class="sm" '+(BUSY||!it.affordable?'disabled':'')+' onclick="cmd(\\'buy '+it.n+'\\')">Buy</button></span></div>';
  }).join("");
  return '<div class="muted">Sell from the 🎒 Bag tab.</div>'+rows;
}

function bodyBag(){
  var eq='<h3>Equipped</h3>'+S.equipped.map(function(s){
    return '<div class="eqrow"><span class="slot">'+esc(s.slot)+'</span>'+
      (s.name?'<span class="'+rar(s.rarity)+'">'+esc(s.name)+'</span>':'<span class="empty">— empty —</span>')+'</div>';
  }).join("");
  var gear=S.gear.length?('<h3>Gear — equip or sell</h3>'+S.gear.map(function(g){
    return '<div class="listrow"><span class="'+rar(g.rarity)+'">'+esc(g.name)+' <span class="muted">'+esc(g.slot)+' · '+g.score+'</span></span>'+
      '<span><button class="sm p" '+(BUSY?'disabled':'')+' onclick="cmd(\\'equip '+g.n+'\\')">Equip</button> '+
      '<button class="sm" '+(BUSY?'disabled':'')+' onclick="cmd(\\'sell '+g.n+'\\')">Sell</button></span></div>';
  }).join("")+'<div class="btns" style="margin-top:8px">'+actBtn("Sell all gear","sell allgear","sm")+actBtn("Sell all materials","sell all","sm")+'</div>'):'<h3>Gear</h3><div class="muted">No spare gear.</div>';
  var items=S.bag.length?('<h3>Items & materials</h3>'+S.bag.map(function(b){
    return '<div class="listrow"><span>'+(b.potion?'🧪 ':'')+esc(b.name)+'</span><span class="muted">×'+b.qty+'</span></div>';
  }).join("")):'';
  return eq+gear+items;
}

function bodyCraft(){
  return '<div class="muted">Open a list, then tap a number to make it.</div>'+
    '<div class="btns" style="margin-top:8px">'+
      actBtn("🔨 Recipes","recipes","sm")+actBtn("⚗️ Brew list","brew","sm")+actBtn("🔮 Enchant list","enchant","sm")+
    '</div>'+numPad(["craft","brew","enchant"])+
    '<h3>Professions</h3><div class="chips">'+
      S.professions.map(function(p){return '<span class="chip">'+esc(p.emoji)+' '+esc(p.name)+' <span class="pill">'+p.level+'</span></span>';}).join("")+
    '</div>';
}

function bodyMore(){
  return '<div class="btns">'+
      actBtn("📜 Quest","quest","sm")+actBtn("🎁 Lootbox","lootbox","sm")+
      actBtn("🏆 Leaderboard","leaderboard","sm")+actBtn("✨ Skills","skills","sm")+
      actBtn("⭐ Ascend","ascend","sm")+
    '</div>'+numPad(["lootbox","quest"])+
    '<div class="muted" style="margin-top:10px">Tip: your browser hero and your chat hero are the same character.</div>';
}

// A 1–9 number pad that repeats the last "list" command with a chosen index —
// e.g. after "Recipes", tapping 2 sends "craft 2".
var PADCMD=null;
function numPad(cmds){
  // Show a pad only after one of these list commands was the last thing run.
  var hint = PADCMD && cmds.indexOf(PADCMD)>=0 ? PADCMD : null;
  var pick = hint || cmds[0];
  var nums="";
  for(var i=1;i<=9;i++) nums+='<button class="sm" '+(BUSY?'disabled':'')+' onclick="cmd(\\''+pick+' '+i+'\\')">'+i+'</button>';
  return '<div class="barlab" style="margin-top:10px"><span>Make #</span><span class="muted">'+esc(pick)+' &lt;#&gt;</span></div><div class="btns">'+nums+'</div>';
}

// ── Character creation ──────────────────────────────────────────────────────
function renderCreate(){
  document.documentElement.style.setProperty("--accent","#7cc44a");
  var cls=S.classes.map(function(c){
    return '<div class="pk '+(NEWCLS===c.id?'sel':'')+'" onclick="pickCls(\\''+c.id+'\\')"><b>'+(THEME[c.id]?THEME[c.id].e+' ':'')+esc(c.name)+'</b><small>'+esc(c.blurb||'')+'</small></div>';
  }).join("");
  var race=S.races.map(function(r){
    return '<div class="pk '+(NEWRACE===r.id?'sel':'')+'" onclick="pickRace(\\''+r.id+'\\')"><b>'+esc(r.name)+'</b><small>'+esc(r.blurb||'')+'</small></div>';
  }).join("");
  document.getElementById("root").innerHTML=
    '<div class="card"><h3>Choose a class</h3><div class="cls-pick">'+cls+'</div>'+
    '<h3>Choose a race</h3><div class="cls-pick">'+race+'</div>'+
    '<div class="center" style="margin-top:16px">'+
      '<button class="p" '+(NEWCLS&&NEWRACE&&!BUSY?'':'disabled')+' onclick="doCreate()">🎉 Create Hero</button>'+
    '</div>'+(LASTMSG?'<div class="log" style="margin-top:12px">'+esc(LASTMSG)+'</div>':'')+'</div>';
}
function pickCls(id){ NEWCLS=id; renderCreate(); }
function pickRace(id){ NEWRACE=id; renderCreate(); }
async function doCreate(){ if(!NEWCLS||!NEWRACE)return; await cmd("create "+NEWCLS+" "+NEWRACE); }

// Track which list command was last run so the craft/more number pads target it.
var _cmd=cmd;
cmd=function(c){ var head=c.split(" ")[0]; if(["recipes","brew","enchant","lootbox","quest"].indexOf(head)>=0) PADCMD=(head==="recipes"?"craft":head); return _cmd(c); };

if(!TOKEN){ fail("No play link — type 'tt web' in chat to get your personal link."); }
else refresh();
</script>
</body></html>`;
