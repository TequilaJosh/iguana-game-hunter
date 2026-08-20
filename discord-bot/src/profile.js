import { getPlayer, allPlayers } from './game/store.js';
import { CLASSES, RACES, ZONE_LIST, STAT_KEYS } from './game/content.js';
import { derive, xpToNext } from './game/engine.js';
import { PROFESSIONS } from './game/professions.js';
import { config } from './config.js';

const EQUIP_SLOTS = ['weapon', 'head', 'body', 'shield', 'feet', 'accessory'];
const PROF_KEYS = Object.keys(PROFESSIONS);

export const profileUrl = (discordId) => {
  if (!config.publicUrl) return null;
  const base = config.publicUrl.replace(/\/+$/, '') + '/profile';
  return discordId ? `${base}?id=${encodeURIComponent(discordId)}` : base;
};

/** Public, read-only character profile pages at /profile. */
export function mountProfile(app) {
  app.get('/profile', (_req, res) => res.type('html').send(PROFILE_HTML));

  app.get('/profile/api/players', (_req, res) => {
    const rows = Object.entries(allPlayers())
      .filter(([, c]) => c && c.name)
      .map(([id, c]) => ({ id, name: c.name, level: c.level || 1, cls: c.cls }))
      .sort((a, b) => (b.level - a.level) || a.name.localeCompare(b.name));
    res.json(rows);
  });

  app.get('/profile/api/player/:id', (req, res) => {
    const c = getPlayer(req.params.id);
    if (!c) return res.status(404).json({ error: 'No such hero.' });
    const pd = derive(c);
    const cls = CLASSES[c.cls], race = RACES[c.race];
    res.json({
      name: c.name,
      cls: c.cls, className: cls?.name || c.cls, blurb: cls?.blurb || '',
      race: c.race, raceName: race?.name || c.race,
      level: c.level || 1, xp: c.xp || 0, xpNext: xpToNext(c.level || 1),
      gold: c.gold || 0, ascension: c.ascension || 0,
      hp: c.hp ?? pd.maxhp, maxhp: pd.maxhp, maxmp: pd.maxmp,
      power: Math.round(pd.st[pd.scales] * 1.3 + pd.wpow * 1.4), def: pd.def, res: pd.res,
      stats: STAT_KEYS.map((k) => ({ k: k.toUpperCase(), v: pd.st[k] })),
      equipped: EQUIP_SLOTS.map((s) => ({ slot: s, name: c.equipped?.[s]?.name || null, rarity: c.equipped?.[s]?.rarity || '' })),
      professions: PROF_KEYS
        .map((k) => ({ name: PROFESSIONS[k].name, emoji: PROFESSIONS[k].emoji, level: (c.professions?.[k]?.level) || 1 }))
        .filter((p) => p.level > 1 || p.name === 'Worker'),
      zonesCleared: ZONE_LIST.filter((z) => c.cleared?.[z.id]).length,
      zonesTotal: ZONE_LIST.length,
    });
  });
}

const PROFILE_HTML = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>Tavern Tales — Hero Profile</title>
<style>
  :root{--accent:#7cc44a;--ink:#e8e0c4;--dim:#93a888;}
  *{box-sizing:border-box}
  body{margin:0;min-height:100vh;background:radial-gradient(1200px 700px at 50% -10%, #16241a 0%, #0a120d 55%, #070d09 100%);
       color:var(--ink);font-family:'Segoe UI',system-ui,Arial,sans-serif}
  .wrap{max-width:560px;margin:0 auto;padding:22px 16px 60px}
  h1{color:var(--accent);font-size:22px;margin:0 0 12px;text-align:center}
  .pick{display:flex;gap:8px;margin:0 auto 18px;max-width:420px}
  select,input{flex:1;background:#0a140e;color:var(--ink);border:1px solid #2c5233;border-radius:7px;padding:9px 10px;font-size:14px}
  .hint{color:var(--dim);font-size:12px;text-align:center;margin-top:6px}

  /* The card is themed off the character's class via --accent. */
  .card{position:relative;background:linear-gradient(180deg,rgba(255,255,255,.03),rgba(0,0,0,.25));
        border:1px solid var(--accent);border-radius:16px;overflow:hidden;
        box-shadow:0 10px 40px rgba(0,0,0,.55), inset 0 0 60px -30px var(--accent)}
  .banner{padding:20px 20px 16px;background:linear-gradient(120deg, color-mix(in srgb, var(--accent) 26%, transparent), transparent 70%);
          border-bottom:1px solid color-mix(in srgb, var(--accent) 40%, transparent)}
  .emoji{font-size:44px;line-height:1;filter:drop-shadow(0 2px 6px rgba(0,0,0,.6))}
  .name{font-size:26px;font-weight:800;margin:6px 0 2px;color:#fff}
  .sub{color:var(--accent);font-weight:700;font-size:14px}
  .stars{color:#f0c84a;font-size:14px;margin-left:6px}
  .blurb{color:var(--dim);font-size:12px;margin-top:6px;font-style:italic}
  .body{padding:16px 20px 20px}
  .barlab{display:flex;justify-content:space-between;font-size:11px;color:var(--dim);margin:10px 0 3px;text-transform:uppercase;letter-spacing:.05em}
  .bar{height:12px;border-radius:8px;background:#0a140e;border:1px solid #2c5233;overflow:hidden}
  .fill{height:100%;background:var(--accent);border-radius:8px}
  .fill.hp{background:#d46a6a}
  .grid{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin-top:14px}
  .stat{background:#0a140e;border:1px solid #23331f;border-radius:9px;padding:8px 10px;text-align:center}
  .stat b{display:block;font-size:18px;color:#fff}
  .stat span{font-size:10px;color:var(--dim);text-transform:uppercase;letter-spacing:.05em}
  .sec{margin-top:16px}
  .sec h3{font-size:11px;color:var(--accent);text-transform:uppercase;letter-spacing:.06em;margin:0 0 6px}
  .chips{display:flex;flex-wrap:wrap;gap:6px}
  .chip{background:#0a140e;border:1px solid #23331f;border-radius:20px;padding:3px 10px;font-size:12px}
  .chip .pill{color:var(--accent);font-weight:700}
  .eqrow{display:flex;justify-content:space-between;font-size:13px;padding:4px 0;border-bottom:1px solid rgba(35,51,31,.6)}
  .eqrow .slot{color:var(--dim);text-transform:capitalize}
  .eqrow .empty{color:#5f7355}
  .footer{display:flex;justify-content:space-between;color:var(--dim);font-size:12px;margin-top:16px;padding-top:10px;border-top:1px solid rgba(35,51,31,.6)}
  .hide{display:none}
</style></head><body><div class="wrap">
  <h1>🍺 Hero Profile</h1>
  <div class="pick">
    <input id="search" placeholder="Search heroes…" oninput="filter()"/>
    <select id="player" onchange="load()"></select>
  </div>
  <div id="status" class="hint"></div>
  <div id="card"></div>
</div>
<script>
var THEME={
  knight:{c:"#6fb0e0",e:"🛡️"},berserker:{c:"#e06a5a",e:"🪓"},paladin:{c:"#f0cf5a",e:"⚜️"},
  ranger:{c:"#7cc44a",e:"🏹"},rogue:{c:"#a884e0",e:"🗡️"},mage:{c:"#6a9ff0",e:"🔮"},
  cleric:{c:"#f0e6a6",e:"✨"},necromancer:{c:"#9a6ad0",e:"💀"},monk:{c:"#e0a45a",e:"👊"},bard:{c:"#e884b8",e:"🎵"}
};
var ALL=[];
function esc(s){return String(s==null?"":s).replace(/[&<>]/g,function(c){return{"&":"&amp;","<":"&lt;",">":"&gt;"}[c];});}
async function j(u){var r=await fetch(u);var d=await r.json();if(!r.ok)throw new Error(d.error||("HTTP "+r.status));return d;}

async function init(){
  try{
    ALL=await j("/profile/api/players"); fill(ALL);
    var q=new URLSearchParams(location.search).get("id");
    if(q){ var sel=document.getElementById("player"); for(var i=0;i<sel.options.length;i++){ if(sel.options[i].value===q){ sel.selectedIndex=i; break; } } }
    if(ALL.length) load();
  }
  catch(e){ document.getElementById("status").textContent="Couldn't load heroes: "+e.message; }
}
function fill(list){
  document.getElementById("player").innerHTML=list.map(function(p){
    return '<option value="'+esc(p.id)+'">'+esc(p.name)+' — Lv '+p.level+'</option>';
  }).join("");
}
function filter(){
  var q=document.getElementById("search").value.toLowerCase();
  var list=ALL.filter(function(p){return p.name.toLowerCase().indexOf(q)>=0;});
  fill(list); if(list.length) load();
}
async function load(){
  var id=document.getElementById("player").value; if(!id)return;
  try{ render(await j("/profile/api/player/"+encodeURIComponent(id))); }
  catch(e){ document.getElementById("card").innerHTML='<div class="hint">'+esc(e.message)+'</div>'; }
}
function render(p){
  var th=THEME[p.cls]||{c:"#7cc44a",e:"🎮"};
  document.documentElement.style.setProperty("--accent",th.c);
  var xpPct=Math.min(100,Math.round(100*(p.xp||0)/Math.max(1,p.xpNext)));
  var hpPct=Math.min(100,Math.round(100*(p.hp||0)/Math.max(1,p.maxhp)));
  var stars=p.ascension?'<span class="stars">'+"★".repeat(Math.min(5,p.ascension))+(p.ascension>5?"+":"")+'</span>':'';
  var stats=p.stats.map(function(s){return '<div class="stat"><b>'+s.v+'</b><span>'+esc(s.k)+'</span></div>';}).join("");
  var eq=p.equipped.map(function(s){
    return '<div class="eqrow"><span class="slot">'+esc(s.slot)+'</span>'+(s.name?'<span>'+esc(s.name)+'</span>':'<span class="empty">— empty —</span>')+'</div>';
  }).join("");
  var profs=p.professions.map(function(pr){return '<span class="chip">'+esc(pr.emoji)+' '+esc(pr.name)+' <span class="pill">'+pr.level+'</span></span>';}).join("") || '<span class="chip">—</span>';
  document.getElementById("card").innerHTML=
   '<div class="card"><div class="banner">'+
     '<div class="emoji">'+th.e+'</div>'+
     '<div class="name">'+esc(p.name)+'</div>'+
     '<div class="sub">Lv '+p.level+' '+esc(p.raceName)+' '+esc(p.className)+stars+'</div>'+
     (p.blurb?'<div class="blurb">"'+esc(p.blurb)+'"</div>':'')+
   '</div><div class="body">'+
     '<div class="barlab"><span>HP</span><span>'+p.hp+' / '+p.maxhp+'</span></div><div class="bar"><div class="fill hp" style="width:'+hpPct+'%"></div></div>'+
     '<div class="barlab"><span>XP</span><span>'+p.xp+' / '+p.xpNext+'</span></div><div class="bar"><div class="fill" style="width:'+xpPct+'%"></div></div>'+
     '<div class="grid">'+
       '<div class="stat"><b>'+p.power+'</b><span>Power</span></div>'+
       '<div class="stat"><b>'+p.def+'</b><span>Defense</span></div>'+
       '<div class="stat"><b>'+p.res+'</b><span>Resist</span></div>'+
     '</div>'+
     '<div class="grid">'+stats+'</div>'+
     '<div class="sec"><h3>Equipped</h3>'+eq+'</div>'+
     '<div class="sec"><h3>Professions</h3><div class="chips">'+profs+'</div></div>'+
     '<div class="footer"><span>🪙 '+p.gold+' gold</span><span>🗺️ '+p.zonesCleared+' / '+p.zonesTotal+' zones cleared</span></div>'+
   '</div></div>';
}
init();
</script>
</body></html>`;
