import { getPlayer } from './game/store.js';
import { getParty } from './activity.js';
import { getLook } from './game/cosmetics.js';
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
      return { name: e.name || c.name, action: e.action || 'idle', look: getLook(c) };
    }).filter(Boolean);
    res.json({ party });
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
var canvas=document.getElementById('c'), party=[];
function resize(){var dpr=Math.min(2,window.devicePixelRatio||1);canvas.width=Math.floor(innerWidth*dpr);canvas.height=Math.floor(innerHeight*dpr);}
window.addEventListener('resize',resize);resize();
async function poll(){try{var r=await fetch('/party/api',{cache:'no-store'});var d=await r.json();party=d.party||[];}catch(e){}}
poll();setInterval(poll,2500);
function frame(now){TT_drawParty(canvas,party,now);requestAnimationFrame(frame);}
requestAnimationFrame(frame);
</script></body></html>`;
