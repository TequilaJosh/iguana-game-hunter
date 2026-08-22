import { getPlayer } from './game/store.js';
import { getParty } from './activity.js';
import { getLook } from './game/cosmetics.js';
import { getFight } from './game/fights.js';
import { raidOverlayState } from './game/raids.js';
import { SPRITE_JS } from './game/spriteEngine.js';

// Party sprite overlay: little animated heroes doing what they're doing in Tavern Tales.
// Served two ways — a standalone transparent page at /party (drop into OBS as a browser
// source), and a CORS-enabled JSON feed at /party/api that Game Hunter's main overlay
// fetches to render the same sprites as a positionable element.
export function mountParty(app) {
  app.get('/party/api', (_req, res) => {
    res.set('Access-Control-Allow-Origin', '*');
    const party = getParty().map((e) => {
      const c = getPlayer(e.id);
      if (!c || !c.name) return null;
      const entry = { name: e.name || c.name, action: e.action || 'idle', look: getLook(c) };
      if (e.action === 'fight') {
        const f = getFight(e.id);
        if (f && f.monster) {
          entry.fight = {
            monster: f.monster.name, emoji: f.monster.emoji || '👹',
            mhp: Math.max(0, f.mhp), mmaxhp: f.mmaxhp,
            php: Math.max(0, f.php), pmaxhp: (f.pd && f.pd.maxhp) || f.php,
          };
        }
      }
      return entry;
    }).filter(Boolean);

    // Active raid: a shared boss everyone fights together in the overlay.
    let raid = raidOverlayState();
    if (raid) {
      raid = {
        ...raid,
        raiders: raid.raiders.map((rd) => {
          const rc = getPlayer(rd.id);
          return { ...rd, name: rd.name || (rc && rc.name) || '?', look: getLook(rc || {}) };
        }),
      };
    }
    res.json(raid ? { party, raid } : { party });
  });

  // The sprite engine, served standalone so Game Hunter's main overlay can load it
  // cross-origin and render the party element itself (one source of truth).
  app.get('/party/engine.js', (_req, res) => {
    res.set('Access-Control-Allow-Origin', '*');
    res.type('application/javascript').send(SPRITE_JS);
  });

  app.get('/party', (_req, res) => res.type('html').send(PARTY_HTML));
}

const PARTY_HTML = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>Tavern Tales — Party</title>
<style>html,body{margin:0;height:100%;background:transparent;overflow:hidden}#c{display:block;width:100vw;height:100vh}</style>
</head><body><canvas id="c"></canvas>
<script>
${SPRITE_JS}
var canvas=document.getElementById('c'), party=[], raidState=null;
function resize(){var dpr=Math.min(2,window.devicePixelRatio||1);canvas.width=Math.floor(innerWidth*dpr);canvas.height=Math.floor(innerHeight*dpr);}
window.addEventListener('resize',resize);resize();
async function poll(){try{var r=await fetch('/party/api',{cache:'no-store'});var d=await r.json();party=d.party||[];raidState=d.raid||null;}catch(e){}}
poll();setInterval(poll,2000);
function frame(now){TT_drawParty(canvas,party,now,raidState);requestAnimationFrame(frame);}
requestAnimationFrame(frame);
</script></body></html>`;
