// Shared browser-side sprite engine for the Tavern Tales party overlay. Exported as a
// plain-JS string (NO backticks / ${ } inside) so it can be embedded into overlay pages
// AND loaded by Game Hunter's LiveOverlay.html. Entry points:
//   TT_drawParty(canvas, party, now)  — the whole party (walks around, fights, etc.)
//   TT_drawChar(ctx, cx, groundY, u, look, action, now, fight, opts)  — one hero
export const SPRITE_JS = `
function _rr(ctx,x,y,w,h,r){ctx.beginPath();ctx.moveTo(x+r,y);ctx.arcTo(x+w,y,x+w,y+h,r);ctx.arcTo(x+w,y+h,x,y+h,r);ctx.arcTo(x,y+h,x,y,r);ctx.arcTo(x,y,x+w,y,r);ctx.closePath();}
function _lerp(a,b,t){return a+(b-a)*t;}
function _shade(hex,f){var n=parseInt((hex||'#888').slice(1),16),r=(n>>16)&255,g=(n>>8)&255,b=n&255;r=Math.max(0,Math.min(255,Math.round(r*f)));g=Math.max(0,Math.min(255,Math.round(g*f)));b=Math.max(0,Math.min(255,Math.round(b*f)));return 'rgb('+r+','+g+','+b+')';}

function _drawItem(ctx,kind,u){
  ctx.lineCap='round';
  if(kind==='sword'){ctx.strokeStyle='#c9ccd6';ctx.lineWidth=1.3*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(6*u,0);ctx.stroke();ctx.strokeStyle='#6b5330';ctx.lineWidth=1.5*u;ctx.beginPath();ctx.moveTo(-0.2*u,-1.2*u);ctx.lineTo(-0.2*u,1.2*u);ctx.stroke();}
  else if(kind==='greatsword'){ctx.strokeStyle='#d5d8e2';ctx.lineWidth=1.8*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(8*u,0);ctx.stroke();ctx.strokeStyle='#5a4326';ctx.lineWidth=1.8*u;ctx.beginPath();ctx.moveTo(0,-1.6*u);ctx.lineTo(0,1.6*u);ctx.stroke();}
  else if(kind==='axe'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.2*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(5.5*u,0);ctx.stroke();ctx.fillStyle='#c9ccd6';ctx.beginPath();ctx.moveTo(4.6*u,-0.4*u);ctx.quadraticCurveTo(7.4*u,-2.4*u,7.6*u,0.2*u);ctx.quadraticCurveTo(7.4*u,2.4*u,4.6*u,0.6*u);ctx.closePath();ctx.fill();}
  else if(kind==='mace'){ctx.strokeStyle='#6b5330';ctx.lineWidth=1.3*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(5*u,0);ctx.stroke();ctx.fillStyle='#9aa0ad';ctx.beginPath();ctx.arc(6*u,0,1.6*u,0,7);ctx.fill();}
  else if(kind==='dagger'){ctx.strokeStyle='#d5d8e2';ctx.lineWidth=1.1*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(3.6*u,0);ctx.stroke();ctx.strokeStyle='#5a4326';ctx.lineWidth=1.2*u;ctx.beginPath();ctx.moveTo(0,-0.9*u);ctx.lineTo(0,0.9*u);ctx.stroke();}
  else if(kind==='bow'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.1*u;ctx.beginPath();ctx.arc(1*u,0,3.6*u,-1.1,1.1);ctx.stroke();ctx.strokeStyle='#eee';ctx.lineWidth=0.4*u;ctx.beginPath();ctx.moveTo(1*u+3.6*u*Math.cos(-1.1),3.6*u*Math.sin(-1.1));ctx.lineTo(1*u+3.6*u*Math.cos(1.1),3.6*u*Math.sin(1.1));ctx.stroke();}
  else if(kind==='staff'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.2*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(7.5*u,0);ctx.stroke();ctx.fillStyle='#6fd0ff';ctx.beginPath();ctx.arc(7.7*u,0,1.4*u,0,7);ctx.fill();}
  else if(kind==='hammer'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.4*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(5.6*u,0);ctx.stroke();ctx.fillStyle='#9aa0ad';_rr(ctx,5*u,-1.8*u,2.4*u,3.6*u,0.5*u);ctx.fill();}
  else if(kind==='pick'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.2*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(6.4*u,0);ctx.stroke();ctx.strokeStyle='#9aa0ad';ctx.lineWidth=1.1*u;ctx.beginPath();ctx.moveTo(6.4*u,-2.4*u);ctx.quadraticCurveTo(7.6*u,0,6.4*u,2.4*u);ctx.stroke();}
  else if(kind==='rod'){ctx.strokeStyle='#8a6a3a';ctx.lineWidth=0.9*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(8.5*u,-1.4*u);ctx.stroke();}
  else if(kind==='shovel'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.2*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(6*u,0);ctx.stroke();ctx.fillStyle='#b8bcc6';ctx.beginPath();ctx.moveTo(5.8*u,-1.5*u);ctx.lineTo(8*u,-1.5*u);ctx.lineTo(8.3*u,1.5*u);ctx.lineTo(5.8*u,1.5*u);ctx.closePath();ctx.fill();}
  else if(kind==='basket'){ctx.fillStyle='#8a6a3a';_rr(ctx,2.6*u,-1.1*u,2.8*u,2.4*u,0.5*u);ctx.fill();}
  ctx.lineCap='butt';
}

// ── Procedural monster sprites ─────────────────────────────────────────────
// Every monster/boss gets a distinct, deterministic sprite from its family (body
// shape), element (palette) and id (per-monster variation). Drawn facing LEFT
// (toward the party/raiders), around (cx, groundY) at unit size s.
function _hash(s){var h=2166136261;s=String(s||'');for(var i=0;i<s.length;i++){h^=s.charCodeAt(i);h=Math.imul(h,16777619);}return (h>>>0);}
function _elem(el){
  var P={none:['#8a8f9a','#6f7480','#c0c6d0','#ffd34a'],fire:['#e0552a','#a83418','#ffb038','#fff2a8'],
    water:['#3f7fd0','#295a9e','#8fd0ff','#eaf6ff'],earth:['#8a6a3a','#5f4826','#c2a06a','#ffe8b0'],
    dark:['#5a3a70','#37214e','#b070e0','#ff5a7a'],wind:['#8fc9a0','#5f9e78','#d8f0e0','#eaffe0'],
    poison:['#6fae2a','#4a7c1a','#c8f06a','#d6ff6a'],lightning:['#e0c23a','#b0902a','#fff2a8','#fffbe0'],
    ice:['#7fc9e0','#4f9ec0','#d8f4ff','#ffffff'],holy:['#e8d89a','#c0a860','#fff2c0','#ffffff']};
  var a=P[el]||P.none;return {body:a[0],dark:a[1],accent:a[2],eye:a[3]};
}
function _poly(ctx,pts){ctx.beginPath();ctx.moveTo(pts[0][0],pts[0][1]);for(var i=1;i<pts.length;i++)ctx.lineTo(pts[i][0],pts[i][1]);ctx.closePath();}
function _meye(ctx,x,y,r,pal){ctx.fillStyle=pal.eye;ctx.beginPath();ctx.arc(x,y,r,0,7);ctx.fill();ctx.fillStyle='#180a14';ctx.beginPath();ctx.arc(x,y,r*0.46,0,7);ctx.fill();}
function _ell(ctx,x,y,rx,ry,col){ctx.fillStyle=col;ctx.beginPath();ctx.ellipse(x,y,rx,ry,0,0,7);ctx.fill();}

function TT_drawMonster(ctx,cx,groundY,s,mon,now,opts){
  opts=opts||{};
  mon=mon||{};
  var fam=mon.family||'beast', el=mon.element||'none', h=_hash(mon.id||mon.name||fam);
  var pal=_elem(el);
  if(fam==='undead') pal={body:'#e6e0cf',dark:'#b3ab93',accent:pal.accent,eye:(el==='none'?'#8fe0ff':pal.eye)};
  if(fam==='construct') pal={body:'#9aa0ad',dark:'#666c78',accent:pal.accent,eye:(el==='none'?'#ff8a3a':pal.eye)};
  s=s*(0.9+((h>>3)%18)/100);                 // subtle per-monster size jitter
  var boss=!!opts.boss;
  var breathe=1+Math.sin(now/(boss?620:360))*0.05;
  var per=boss?1600:920, pp=(now%per)/per;
  var lunge=pp>0.72?(1-Math.min(1,Math.abs(pp-0.83)/0.11)):0;
  var x=cx-lunge*(boss?1.5:0.9)*s;           // lurch toward the party on the beat
  var B=s, gy=groundY;
  var horns=(h&1), spikes=(h&2), extraEye=(h&4);
  ctx.save();
  if(opts.phase==='lobby') ctx.globalAlpha=0.7;
  // shadow
  ctx.fillStyle='rgba(0,0,0,0.28)';ctx.beginPath();ctx.ellipse(cx,gy+0.4*B,3*B,0.9*B,0,0,7);ctx.fill();

  function legs(n,topY,col){ctx.strokeStyle=col;ctx.lineWidth=0.7*B;ctx.lineCap='round';for(var i=0;i<n;i++){var lx=x-1.8*B+(3.6*B)*(i/(n-1||1));ctx.beginPath();ctx.moveTo(lx,gy-topY);ctx.lineTo(lx+Math.sin(now/200+i)*0.3*B,gy-0.1*B);ctx.stroke();}ctx.lineCap='butt';}

  if(fam==='slime'){
    var wob=1+Math.sin(now/240)*0.08;
    _ell(ctx,x,gy-2*B*wob,2.8*B,2.3*B*wob,pal.body);
    _ell(ctx,x-0.7*B,gy-2.9*B*wob,1.1*B,0.9*B,'rgba(255,255,255,0.25)');
    _meye(ctx,x-1.1*B,gy-2.1*B,0.5*B,pal);_meye(ctx,x+0.4*B,gy-2.1*B,0.5*B,pal);
  } else if(fam==='avian'){
    _ell(ctx,x,gy-2.6*B,1.7*B,2.1*B*breathe,pal.body);           // body
    _poly(ctx,[[x+0.6*B,gy-3*B],[x+3.4*B,gy-4.4*B],[x+2.2*B,gy-1.8*B]]);ctx.fillStyle=pal.dark;ctx.fill(); // wing
    _ell(ctx,x-1*B,gy-4*B,1.1*B,1.1*B,pal.body);                 // head
    _poly(ctx,[[x-1.9*B,gy-4*B],[x-3.1*B,gy-3.6*B],[x-1.9*B,gy-3.3*B]]);ctx.fillStyle=pal.accent;ctx.fill(); // beak
    legs(2,1.1*B,pal.dark);_meye(ctx,x-1.3*B,gy-4.1*B,0.32*B,pal);
  } else if(fam==='insect'){
    _ell(ctx,x+1.4*B,gy-1.9*B,1.5*B,1.2*B,pal.dark);
    _ell(ctx,x,gy-2*B,1.6*B,1.3*B,pal.body);
    _ell(ctx,x-1.5*B,gy-2.2*B,1.2*B,1*B,pal.dark);               // head
    ctx.strokeStyle=pal.dark;ctx.lineWidth=0.35*B;ctx.lineCap='round';
    for(var li=0;li<3;li++){var bx=x-0.5*B+li*0.9*B;ctx.beginPath();ctx.moveTo(bx,gy-1.4*B);ctx.lineTo(bx-0.8*B,gy);ctx.moveTo(bx,gy-1.4*B);ctx.lineTo(bx+0.8*B,gy);ctx.stroke();}
    ctx.beginPath();ctx.moveTo(x-2.1*B,gy-2.8*B);ctx.lineTo(x-3*B,gy-3.8*B);ctx.moveTo(x-1.9*B,gy-2.9*B);ctx.lineTo(x-2.5*B,gy-4*B);ctx.stroke();ctx.lineCap='butt';
    _meye(ctx,x-1.8*B,gy-2.3*B,0.34*B,pal);
  } else if(fam==='elemental'){
    var hov=Math.sin(now/300)*0.4*B;
    for(var pi=0;pi<6;pi++){var a=now/500+pi;_ell(ctx,x+Math.cos(a)*2.6*B,gy-2.8*B+hov+Math.sin(a)*2.6*B,0.3*B,0.3*B,pal.accent);}
    _ell(ctx,x,gy-2.8*B+hov,2*B*breathe,2*B*breathe,pal.body);
    _ell(ctx,x,gy-2.8*B+hov,1.1*B,1.1*B,pal.accent);
    _meye(ctx,x-0.6*B,gy-2.9*B+hov,0.4*B,pal);_meye(ctx,x+0.6*B,gy-2.9*B+hov,0.4*B,pal);
  } else if(fam==='fae'){
    var hov2=Math.sin(now/260)*0.5*B;
    ctx.globalAlpha*=0.9;_ell(ctx,x+1.2*B,gy-3*B+hov2,1.6*B,2.4*B,'rgba(200,230,255,0.4)');_ell(ctx,x-1.2*B,gy-3*B+hov2,1.6*B,2.4*B,'rgba(200,230,255,0.4)');
    _ell(ctx,x,gy-3*B+hov2,0.9*B,1.5*B,pal.body);_ell(ctx,x,gy-4.3*B+hov2,0.8*B,0.8*B,pal.accent);
    _meye(ctx,x-0.3*B,gy-4.3*B+hov2,0.22*B,pal);_meye(ctx,x+0.3*B,gy-4.3*B+hov2,0.22*B,pal);
  } else if(fam==='plant'){
    ctx.strokeStyle=pal.dark;ctx.lineWidth=0.8*B;ctx.beginPath();ctx.moveTo(x,gy);ctx.lineTo(x,gy-3*B);ctx.stroke();
    ctx.fillStyle='#3f8a45';_poly(ctx,[[x,gy-1.6*B],[x-2.4*B,gy-2.4*B],[x,gy-2.6*B]]);ctx.fill();_poly(ctx,[[x,gy-2*B],[x+2.4*B,gy-2.8*B],[x,gy-3*B]]);ctx.fill();
    for(var q=0;q<6;q++){var pa=q/6*6.28;_ell(ctx,x+Math.cos(pa)*1.7*B,gy-4.6*B+Math.sin(pa)*1.7*B,0.8*B,0.8*B,pal.accent);}
    _ell(ctx,x,gy-4.6*B*breathe,1.5*B,1.5*B,pal.body);
    _meye(ctx,x-0.6*B,gy-4.7*B,0.34*B,pal);_meye(ctx,x+0.5*B,gy-4.7*B,0.34*B,pal);
  } else if(fam==='aquatic'){
    _poly(ctx,[[x+2.6*B,gy-2.6*B],[x+4*B,gy-1.4*B],[x+4*B,gy-3.8*B]]);ctx.fillStyle=pal.dark;ctx.fill(); // tail
    _ell(ctx,x,gy-2.6*B,2.4*B,1.7*B*breathe,pal.body);
    _poly(ctx,[[x,gy-4*B],[x+1*B,gy-5.4*B],[x+1.6*B,gy-3.8*B]]);ctx.fillStyle=pal.dark;ctx.fill(); // dorsal
    _meye(ctx,x-1.4*B,gy-2.8*B,0.42*B,pal);
    ctx.strokeStyle=pal.dark;ctx.lineWidth=0.3*B;for(var gi=0;gi<3;gi++){ctx.beginPath();ctx.arc(x-0.4*B+gi*0.5*B,gy-2.6*B,0.9*B,-0.8,0.8);ctx.stroke();}
  } else if(fam==='undead'){
    legs(2,2.4*B,pal.dark);
    ctx.strokeStyle=pal.body;ctx.lineWidth=0.9*B;ctx.beginPath();ctx.moveTo(x,gy-2.4*B);ctx.lineTo(x,gy-4.4*B);ctx.stroke(); // spine
    ctx.lineWidth=0.35*B;for(var ri=0;ri<3;ri++){var ry=gy-2.8*B-ri*0.6*B;ctx.beginPath();ctx.moveTo(x-1.3*B,ry);ctx.quadraticCurveTo(x,ry+0.5*B,x+1.3*B,ry);ctx.stroke();}
    _ell(ctx,x-0.4*B,gy-5.4*B,1.4*B,1.4*B,pal.body);            // skull
    ctx.fillStyle=pal.body;ctx.fillRect(x-1.3*B,gy-4.6*B,1.8*B,0.7*B); // jaw
    ctx.fillStyle='#180a14';ctx.beginPath();ctx.arc(x-0.9*B,gy-5.5*B,0.42*B,0,7);ctx.arc(x+0.1*B,gy-5.5*B,0.42*B,0,7);ctx.fill();
    ctx.fillStyle=pal.eye;ctx.beginPath();ctx.arc(x-0.9*B,gy-5.5*B,0.2*B,0,7);ctx.arc(x+0.1*B,gy-5.5*B,0.2*B,0,7);ctx.fill();
  } else if(fam==='construct'){
    ctx.fillStyle=pal.dark;ctx.fillRect(x-1.4*B,gy-2.4*B,1.1*B,2.4*B);ctx.fillRect(x+0.3*B,gy-2.4*B,1.1*B,2.4*B); // legs
    ctx.fillStyle=pal.body;ctx.fillRect(x-2*B,gy-6*B*breathe,4*B,4*B*breathe);                                     // torso
    ctx.strokeStyle=pal.dark;ctx.lineWidth=0.4*B;ctx.strokeRect(x-2*B,gy-6*B*breathe,4*B,4*B*breathe);
    ctx.fillStyle=pal.dark;ctx.fillRect(x-3*B,gy-5.4*B,1*B,3*B);ctx.fillRect(x+2*B,gy-5.4*B,1*B,3*B);            // arms
    ctx.fillStyle=pal.body;ctx.fillRect(x-1.5*B,gy-8.2*B,3*B,2.2*B);                                              // head
    ctx.fillStyle=pal.eye;ctx.fillRect(x-1.1*B,gy-7.5*B,2.2*B,0.6*B);
  } else if(fam==='demon'){
    legs(2,2.2*B,pal.dark);
    _poly(ctx,[[x+1.4*B,gy-5*B],[x+4.4*B,gy-6.4*B],[x+3.8*B,gy-3.2*B],[x+2.4*B,gy-4*B]]);ctx.fillStyle=pal.dark;ctx.fill(); // wing
    _ell(ctx,x,gy-3.4*B,2.3*B,2.6*B*breathe,pal.body);
    _ell(ctx,x-0.4*B,gy-6*B,1.5*B,1.4*B,pal.body);              // head
    ctx.fillStyle=pal.dark;_poly(ctx,[[x-1.4*B,gy-6.8*B],[x-2.4*B,gy-8.4*B],[x-0.9*B,gy-7*B]]);ctx.fill();_poly(ctx,[[x+0.6*B,gy-6.9*B],[x+1.6*B,gy-8.4*B],[x+0.2*B,gy-7*B]]);ctx.fill(); // horns
    _meye(ctx,x-1*B,gy-6*B,0.4*B,pal);_meye(ctx,x+0.2*B,gy-6*B,0.4*B,pal);
    ctx.strokeStyle=pal.dark;ctx.lineWidth=0.4*B;ctx.beginPath();ctx.moveTo(x+2*B,gy-2.8*B);ctx.quadraticCurveTo(x+4*B,gy-2*B,x+3.4*B,gy-0.4*B);ctx.stroke(); // tail
  } else if(fam==='dragon'){
    _poly(ctx,[[x+1.6*B,gy-4.6*B],[x+5*B,gy-7*B],[x+4.6*B,gy-3*B],[x+2.6*B,gy-3.6*B]]);ctx.fillStyle=pal.dark;ctx.fill(); // wing
    ctx.strokeStyle=pal.body;ctx.lineWidth=1.4*B;ctx.lineCap='round';ctx.beginPath();ctx.moveTo(x+2.6*B,gy-2.6*B);ctx.quadraticCurveTo(x+5*B,gy-1.4*B,x+5.6*B,gy-3.2*B);ctx.stroke();ctx.lineCap='butt'; // tail
    _ell(ctx,x,gy-3*B,2.4*B,2*B*breathe,pal.body);              // body
    ctx.strokeStyle=pal.body;ctx.lineWidth=1.2*B;ctx.beginPath();ctx.moveTo(x-1.4*B,gy-3.8*B);ctx.quadraticCurveTo(x-3*B,gy-5*B,x-2.6*B,gy-6.4*B);ctx.stroke(); // neck
    _ell(ctx,x-2.7*B,gy-6.8*B,1.4*B,1.1*B,pal.body);            // head
    ctx.fillStyle=pal.accent;_poly(ctx,[[x-3.1*B,gy-7.6*B],[x-3.6*B,gy-8.8*B],[x-2.5*B,gy-7.7*B]]);ctx.fill(); // horn
    ctx.fillStyle=pal.dark;for(var di=0;di<4;di++){var sx=x-0.8*B+di*0.9*B;_poly(ctx,[[sx,gy-4.8*B],[sx+0.4*B,gy-5.8*B],[sx+0.8*B,gy-4.8*B]]);ctx.fill();} // back spikes
    _meye(ctx,x-3.1*B,gy-6.9*B,0.36*B,pal);
  } else if(fam==='giant'){
    ctx.strokeStyle=pal.dark;ctx.lineWidth=1.5*B;ctx.lineCap='round';ctx.beginPath();ctx.moveTo(x-1.2*B,gy-3*B);ctx.lineTo(x-1.2*B,gy);ctx.moveTo(x+1.2*B,gy-3*B);ctx.lineTo(x+1.2*B,gy);ctx.stroke();ctx.lineCap='butt';
    _ell(ctx,x,gy-5*B,3*B,2.8*B*breathe,pal.body);              // big torso
    ctx.strokeStyle=pal.body;ctx.lineWidth=1.6*B;ctx.lineCap='round';ctx.beginPath();ctx.moveTo(x-2.4*B,gy-6*B);ctx.lineTo(x-3.4*B,gy-2.6*B);ctx.moveTo(x+2.4*B,gy-6*B);ctx.lineTo(x+3.4*B,gy-2.6*B);ctx.stroke();ctx.lineCap='butt';
    _ell(ctx,x-0.3*B,gy-8*B,1.5*B,1.5*B,pal.body);              // small head
    _meye(ctx,x-0.9*B,gy-8.1*B,0.34*B,pal);_meye(ctx,x+0.2*B,gy-8.1*B,0.34*B,pal);
  } else if(fam==='aberration'){
    _ell(ctx,x,gy-2.8*B,2.6*B*breathe,2.4*B,pal.body);
    ctx.strokeStyle=pal.dark;ctx.lineWidth=0.6*B;ctx.lineCap='round';
    for(var ti=0;ti<5;ti++){var tx=x-2*B+ti*B;ctx.beginPath();ctx.moveTo(tx,gy-1.6*B);ctx.quadraticCurveTo(tx+Math.sin(now/300+ti)*1.2*B,gy-0.6*B,tx,gy);ctx.stroke();}ctx.lineCap='butt';
    var ne=3+(h%3);for(var ei=0;ei<ne;ei++){var ea=ei/ne*6.28;_meye(ctx,x+Math.cos(ea)*1.3*B,gy-2.8*B+Math.sin(ea)*1.2*B,0.36*B,pal);}
  } else if(fam==='humanoid'){
    ctx.strokeStyle=pal.dark;ctx.lineWidth=0.9*B;ctx.lineCap='round';ctx.beginPath();ctx.moveTo(x-0.9*B,gy-2.6*B);ctx.lineTo(x-0.9*B,gy);ctx.moveTo(x+0.9*B,gy-2.6*B);ctx.lineTo(x+0.9*B,gy);ctx.stroke();ctx.lineCap='butt';
    ctx.fillStyle=pal.body;_rr(ctx,x-1.7*B,gy-6.4*B*breathe,3.4*B,4*B*breathe,0.7*B);ctx.fill();
    ctx.strokeStyle=pal.body;ctx.lineWidth=0.9*B;ctx.lineCap='round';ctx.beginPath();ctx.moveTo(x-1.6*B,gy-5.6*B);ctx.lineTo(x-2.8*B,gy-3.6*B);ctx.moveTo(x+1.6*B,gy-5.6*B);ctx.lineTo(x+2.8*B,gy-3.6*B);ctx.stroke();ctx.lineCap='butt';
    _ell(ctx,x,gy-7.6*B,1.5*B,1.5*B,pal.body);
    if(horns){ctx.fillStyle=pal.accent;_poly(ctx,[[x-1.2*B,gy-8.4*B],[x-1.8*B,gy-9.6*B],[x-0.6*B,gy-8.6*B]]);ctx.fill();_poly(ctx,[[x+1.2*B,gy-8.4*B],[x+1.8*B,gy-9.6*B],[x+0.6*B,gy-8.6*B]]);ctx.fill();}
    _meye(ctx,x-0.7*B,gy-7.7*B,0.32*B,pal);_meye(ctx,x+0.5*B,gy-7.7*B,0.32*B,pal);
  } else { // beast (default)
    legs(4,1.8*B,pal.dark);
    ctx.strokeStyle=pal.body;ctx.lineWidth=1.1*B;ctx.lineCap='round';ctx.beginPath();ctx.moveTo(x+2.2*B,gy-2.4*B);ctx.quadraticCurveTo(x+3.8*B,gy-2.8*B,x+3.4*B,gy-4*B);ctx.stroke();ctx.lineCap='butt'; // tail
    _ell(ctx,x+0.3*B,gy-2.6*B,2.6*B,1.7*B*breathe,pal.body);    // body
    _ell(ctx,x-2*B,gy-3.2*B,1.5*B,1.4*B,pal.body);              // head
    ctx.fillStyle=pal.dark;_poly(ctx,[[x-2.6*B,gy-4.2*B],[x-3*B,gy-5.4*B],[x-1.9*B,gy-4.4*B]]);ctx.fill();_poly(ctx,[[x-1.6*B,gy-4.3*B],[x-1.4*B,gy-5.4*B],[x-0.9*B,gy-4.3*B]]);ctx.fill(); // ears
    _meye(ctx,x-2.4*B,gy-3.2*B,0.36*B,pal);
    if(spikes){ctx.fillStyle=pal.dark;for(var bi=0;bi<3;bi++){var sx2=x-0.5*B+bi*B;_poly(ctx,[[sx2,gy-4*B],[sx2+0.35*B,gy-4.9*B],[sx2+0.7*B,gy-4*B]]);ctx.fill();}}
  }
  ctx.restore();
}

function _drawProp(ctx,action,px,groundY,u,now){
  ctx.save();
  if(action==='fight'){ var bob=Math.sin(now/220)*0.6*u; ctx.fillStyle='#8a5db0';ctx.beginPath();ctx.ellipse(px,groundY-2.4*u+bob,2.7*u,2.5*u,0,0,7);ctx.fill();ctx.fillStyle='#fff';ctx.beginPath();ctx.arc(px-0.8*u,groundY-2.8*u+bob,0.55*u,0,7);ctx.arc(px+0.8*u,groundY-2.8*u+bob,0.55*u,0,7);ctx.fill();ctx.fillStyle='#201';ctx.beginPath();ctx.arc(px-0.8*u,groundY-2.8*u+bob,0.28*u,0,7);ctx.arc(px+0.8*u,groundY-2.8*u+bob,0.28*u,0,7);ctx.fill(); }
  else if(action==='mine'){ ctx.fillStyle='#77747c';ctx.beginPath();ctx.moveTo(px-2.2*u,groundY);ctx.lineTo(px-1.4*u,groundY-2.6*u);ctx.lineTo(px+1.2*u,groundY-3*u);ctx.lineTo(px+2.4*u,groundY);ctx.closePath();ctx.fill();ctx.fillStyle='#b9c7ff';ctx.fillRect(px-0.3*u,groundY-1.9*u,0.8*u,0.8*u); }
  else if(action==='chop'){ ctx.fillStyle='#6b4a2a';ctx.fillRect(px-0.9*u,groundY-5*u,1.8*u,5*u);ctx.fillStyle='#3f8a45';ctx.beginPath();ctx.arc(px,groundY-5.8*u,2.8*u,0,7);ctx.fill(); }
  else if(action==='dig'){ ctx.fillStyle='#6a4a2c';ctx.beginPath();ctx.ellipse(px,groundY-0.3*u,2.6*u,1.2*u,0,0,7);ctx.fill(); }
  else if(action==='fish'){ ctx.fillStyle='rgba(80,150,220,0.55)';ctx.beginPath();ctx.ellipse(px+1.3*u,groundY-0.2*u,4*u,1.4*u,0,0,7);ctx.fill(); }
  else if(action==='forage'){ ctx.fillStyle='#357a3e';ctx.beginPath();ctx.arc(px,groundY-1.4*u,2.1*u,0,7);ctx.fill();ctx.fillStyle='#e46b9c';ctx.beginPath();ctx.arc(px-0.9*u,groundY-2.1*u,0.5*u,0,7);ctx.arc(px+1*u,groundY-1.4*u,0.5*u,0,7);ctx.fill(); }
  else if(action==='scavenge'){ ctx.fillStyle='#7c6a52';ctx.beginPath();ctx.moveTo(px-2.3*u,groundY);ctx.lineTo(px,groundY-2.3*u);ctx.lineTo(px+2.3*u,groundY);ctx.closePath();ctx.fill(); }
  else if(action==='craft'){ ctx.fillStyle='#3a3f47';_rr(ctx,px-2.3*u,groundY-2.1*u,4.6*u,1.4*u,0.4*u);ctx.fill();ctx.fillStyle='#2b2f36';ctx.fillRect(px-0.9*u,groundY-1*u,1.8*u,1*u);ctx.fillStyle='#4a5059';_rr(ctx,px-1.8*u,groundY-2.7*u,2.6*u,1*u,0.3*u);ctx.fill(); }
  ctx.restore();
}
function _spark(ctx,x,y,u,c){ctx.strokeStyle=c||'#ffd34a';ctx.lineWidth=0.5*u;for(var i=0;i<5;i++){var a=i/5*6.28;ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x+Math.cos(a)*1.8*u,y+Math.sin(a)*1.8*u);ctx.stroke();}}

// A little HP bar with numbers, centred at (x, y).
function _hpBar(ctx,x,y,u,cur,max,color){
  var w=7*u, h=1.1*u, p=Math.max(0,Math.min(1,cur/Math.max(1,max)));
  ctx.fillStyle='rgba(0,0,0,0.55)';_rr(ctx,x-w/2-0.4*u,y-0.4*u,w+0.8*u,h+0.8*u,0.6*u);ctx.fill();
  ctx.fillStyle='#0a140e';_rr(ctx,x-w/2,y,w,h,0.5*u);ctx.fill();
  ctx.fillStyle=color;_rr(ctx,x-w/2,y,w*p,h,0.5*u);ctx.fill();
  ctx.fillStyle='#fff';ctx.font='700 '+Math.max(7,Math.round(1.7*u))+'px Segoe UI, sans-serif';ctx.textAlign='center';ctx.textBaseline='bottom';
  ctx.lineWidth=Math.max(1.5,u*0.5);ctx.strokeStyle='rgba(0,0,0,0.85)';
  var t=Math.round(cur)+'/'+Math.round(max);ctx.strokeText(t,x,y-0.5*u);ctx.fillText(t,x,y-0.5*u);
}

// Draw one hero. opts = { flip:bool (mirror, idle only), moving:bool (walk legs) }.
function TT_drawChar(ctx,cx,groundY,u,look,action,now,fight,opts){
  opts=opts||{};
  var CHOP=(action==='mine'||action==='chop'||action==='dig'||action==='craft');
  var period=action==='fight'?560:(CHOP?720:(action==='fish'?1700:(action==='forage'||action==='scavenge'?900:1400)));
  var p=(now%period)/period;
  var bob=Math.sin(now/(action==='idle'?520:340))*(action==='idle'?0.6:0.3)*u;
  var bend=0,lunge=0,impact=false,armA=0.4;
  if(action==='fight'){ var A_UP=-0.9,A_DN=0.7; if(p<0.5) armA=_lerp(A_DN,A_UP,p/0.5); else { var q=(p-0.5)/0.5; armA=_lerp(A_UP,A_DN,q*q); } lunge=(p>0.5?(1-Math.abs(p-0.72)/0.28):0)*1.2*u; impact=(p>0.66&&p<0.8); }
  else if(CHOP){ var U=-2.35,D=0.55; if(p<0.45) armA=_lerp(D,U,p/0.45); else if(p<0.72){var q2=(p-0.45)/0.27; armA=_lerp(U,D,q2*q2);} else armA=D; impact=(p>0.66&&p<0.78); }
  else if(action==='fish'){ armA=-0.35+Math.sin(now/500)*0.05; }
  else if(action==='forage'||action==='scavenge'){ bend=(Math.sin(p*6.28)*0.5+0.5)*0.5; armA=0.9+(action==='scavenge'?Math.sin(now/90)*0.4:Math.sin(p*6.28)*0.3); }

  var propX=cx+6*u;
  if(action!=='idle' && !opts.noProp){
    if(action==='fight' && fight && fight.foe) TT_drawMonster(ctx,propX,groundY,u*1.15,fight.foe,now,{});
    else _drawProp(ctx,action,propX,groundY,u,now);
  }

  ctx.save();
  // mirror for left-walking idle heroes (no props/HP in that state, so it's safe)
  if(opts.flip){ ctx.translate(cx,0); ctx.scale(-1,1); ctx.translate(-cx,0); }
  ctx.translate(cx+lunge,groundY+bob);
  ctx.rotate(bend*0.5);
  var skin=look.skin,hair=look.hair,out=look.outfit,style=look.style;

  ctx.fillStyle='rgba(0,0,0,0.22)';ctx.beginPath();ctx.ellipse(0,0.5*u,2.8*u,0.9*u,0,0,7);ctx.fill();
  // legs (walk cycle when moving)
  var legSwing=opts.moving?Math.sin(now/140)*1*u:0;
  ctx.strokeStyle=_shade(out,0.6);ctx.lineWidth=1.4*u;ctx.lineCap='round';
  ctx.beginPath();ctx.moveTo(-1*u,-2.6*u);ctx.lineTo(-1*u+legSwing,-0.2*u);ctx.moveTo(1*u,-2.6*u);ctx.lineTo(1*u-legSwing,-0.2*u);ctx.stroke();
  // torso
  ctx.fillStyle=out;
  if(style==='robe'){ ctx.beginPath();ctx.moveTo(-2.2*u,-7*u);ctx.lineTo(2.2*u,-7*u);ctx.lineTo(2.9*u,-0.2*u);ctx.lineTo(-2.9*u,-0.2*u);ctx.closePath();ctx.fill(); }
  else { _rr(ctx,-2.2*u,-7*u,4.4*u,4.9*u,0.9*u);ctx.fill(); if(style==='armor'){ctx.strokeStyle=_shade(out,1.4);ctx.lineWidth=0.4*u;ctx.beginPath();ctx.moveTo(-2.2*u,-4.7*u);ctx.lineTo(2.2*u,-4.7*u);ctx.moveTo(0,-7*u);ctx.lineTo(0,-2.2*u);ctx.stroke();} }
  if(style==='cloak'){ ctx.fillStyle=_shade(out,0.7);ctx.beginPath();ctx.moveTo(-2.4*u,-7.2*u);ctx.lineTo(-3.4*u,-0.8*u);ctx.lineTo(-1.3*u,-1.8*u);ctx.closePath();ctx.fill(); }
  // back arm
  ctx.strokeStyle=skin;ctx.lineWidth=1.15*u;ctx.beginPath();ctx.moveTo(-1.7*u,-6.3*u);ctx.lineTo(-2.6*u,-3.8*u);ctx.stroke();
  // head + hair + eyes
  ctx.fillStyle=skin;ctx.beginPath();ctx.arc(0,-9*u,2*u,0,7);ctx.fill();
  ctx.fillStyle=hair;ctx.beginPath();ctx.arc(0,-9.4*u,2.05*u,3.14,6.28);ctx.fill();ctx.fillRect(-2.05*u,-9.5*u,4.1*u,1*u);
  var blink=(now%3200)<120;ctx.fillStyle='#20242c';if(!blink){ctx.beginPath();ctx.arc(-0.8*u,-8.8*u,0.28*u,0,7);ctx.arc(0.6*u,-8.8*u,0.28*u,0,7);ctx.fill();}else{ctx.fillRect(-1.1*u,-8.8*u,0.6*u,0.18*u);ctx.fillRect(0.3*u,-8.8*u,0.6*u,0.18*u);}
  // front arm + item
  ctx.save();ctx.translate(1.7*u,-6.3*u);ctx.rotate(armA);
  ctx.strokeStyle=skin;ctx.lineWidth=1.15*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(2.8*u,0);ctx.stroke();
  ctx.save();ctx.translate(2.8*u,0);
  var held=action==='mine'?'pick':action==='chop'?'axe':action==='dig'?'shovel':action==='craft'?'hammer':action==='fish'?'rod':(action==='forage'||action==='scavenge')?'basket':look.weapon;
  _drawItem(ctx,held,u);ctx.restore();ctx.restore();
  ctx.restore();

  if(impact && !opts.noProp){ if(action==='craft') _spark(ctx,propX-0.5*u,groundY-2.5*u,u,'#ffd34a'); else if(CHOP) _spark(ctx,propX-0.9*u,groundY-1.8*u,u,'#e8dcc0'); else if(action==='fight') _spark(ctx,propX,groundY-2.5*u,u,'#ff6a6a'); }
  if(action==='fish' && !opts.noProp){ var dip=Math.sin(now/450)>0.7?1*u:0; ctx.strokeStyle='rgba(230,230,230,0.8)';ctx.lineWidth=0.35*u;ctx.beginPath();ctx.moveTo(cx+6.5*u,groundY-6*u);ctx.lineTo(propX+1.3*u,groundY-0.3*u+dip);ctx.stroke(); }

  // HP bars over heads during a live fight
  if(fight){
    _hpBar(ctx,cx,groundY-11*u,u,fight.php,fight.pmaxhp,'#5fc27e');
    _hpBar(ctx,propX,groundY-6*u,u,fight.mhp,fight.mmaxhp,'#d46a6a');
  }
}

// ---- Whole-party render: heroes walk around, then act in place ----------------
var TT_POS={};      // name -> { x, vx, wanderAt }
var TT_LAST=0;
function _mmss(ms){var s=Math.max(0,Math.ceil(ms/1000));return Math.floor(s/60)+':'+('0'+(s%60)).slice(-2);}
function _miniBar(ctx,x,y,u,cur,max,color){var w=5*u,h=0.8*u,p=Math.max(0,Math.min(1,cur/Math.max(1,max)));ctx.fillStyle='#0a140e';_rr(ctx,x-w/2,y,w,h,0.4*u);ctx.fill();ctx.fillStyle=color;_rr(ctx,x-w/2,y,w*p,h,0.4*u);ctx.fill();}
function _bossBar(ctx,W,H,raid,u,now){
  var boss=raid.boss,pad=u*3,x=pad,y=pad,w=W-pad*2,h=u*3;
  ctx.fillStyle='rgba(0,0,0,0.55)';_rr(ctx,x-2,y-2,w+4,h+4,6);ctx.fill();
  ctx.fillStyle='#0a140e';_rr(ctx,x,y,w,h,5);ctx.fill();
  var p=Math.max(0,Math.min(1,boss.hp/Math.max(1,boss.maxhp)));
  var grd=ctx.createLinearGradient(x,0,x+w,0);grd.addColorStop(0,'#d64f4f');grd.addColorStop(1,'#9a2fd6');
  ctx.fillStyle=grd;_rr(ctx,x,y,w*p,h,5);ctx.fill();
  ctx.font='800 '+Math.round(u*3)+'px Segoe UI, sans-serif';ctx.textBaseline='middle';ctx.lineWidth=u*0.8;ctx.strokeStyle='rgba(0,0,0,0.9)';ctx.fillStyle='#fff';
  ctx.textAlign='left';var lab='🐉 '+boss.name+'  (T'+raid.tier+')';ctx.strokeText(lab,x+u,y+h/2);ctx.fillText(lab,x+u,y+h/2);
  ctx.textAlign='right';var r=raid.phase==='lobby'?('STARTS IN '+_mmss(raid.startsInMs)+' · tt raid join'):(Math.round(boss.hp)+' / '+Math.round(boss.maxhp));ctx.strokeText(r,x+w-u,y+h/2);ctx.fillText(r,x+w-u,y+h/2);
}
function _drawBoss(ctx,bx,groundY,bs,boss,now,phase){
  // A big procedural sprite for the raid boss, from its family/element/id.
  TT_drawMonster(ctx,bx,groundY,bs*0.62,boss,now,{boss:true,phase:phase});
}
function TT_drawRaid(ctx,W,H,raid,now){
  var u=Math.max(1.2,Math.min(H/22,4.2));
  var groundY=H-Math.max(4,u*1.4);
  var bs=Math.max(3,Math.min(H/7,W/18));
  var bx=W-bs*4;
  _bossBar(ctx,W,H,raid,u,now);
  _drawBoss(ctx,bx,groundY,bs,raid.boss,now,raid.phase);
  var rs=raid.raiders||[],n=Math.min(rs.length,12);
  var left=u*5,right=bx-bs*3.5;
  var slot=n>0?Math.min((right-left)/n,u*9):u*9;
  for(var i=0;i<n;i++){
    var rd=rs[i],cx=left+slot*(i+0.5);
    ctx.font='700 '+Math.max(8,Math.round(u*2.3))+'px Segoe UI, sans-serif';ctx.textAlign='center';ctx.textBaseline='alphabetic';
    var nm=(rd.name||'').slice(0,10);ctx.lineWidth=Math.max(2,u*0.7);ctx.strokeStyle='rgba(0,0,0,0.85)';ctx.strokeText(nm,cx,groundY-u*12);
    ctx.fillStyle=rd.downed?'#e08a8a':'#fff';ctx.fillText(nm,cx,groundY-u*12);
    if(rd.downed){ ctx.globalAlpha=0.45;TT_drawChar(ctx,cx,groundY,u,rd.look,'idle',now,null,{noProp:true});ctx.globalAlpha=1;ctx.font=Math.round(u*3)+'px serif';ctx.fillText('💀',cx,groundY-u*8.5); }
    else { TT_drawChar(ctx,cx,groundY,u,rd.look,'fight',now+i*151,null,{noProp:true});_miniBar(ctx,cx,groundY-u*10.5,u,rd.hp,rd.maxhp,'#5fc27e');
      if(raid.phase==='combat' && (Math.floor((now+i*151)/560)%2===0)) _spark(ctx,cx+u*5,groundY-u*4,u,'#ffd34a'); }
  }
  if(n===0 && raid.phase==='lobby'){ ctx.fillStyle='#cfe';ctx.font='700 '+Math.round(u*3)+'px Segoe UI, sans-serif';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText('Type  tt raid join  to fight!',W*0.35,groundY-u*5); }
}

function TT_drawParty(canvas,party,now,raid){
  var ctx=canvas.getContext('2d');
  var W=canvas.width,H=canvas.height;
  ctx.clearRect(0,0,W,H);
  if(raid){ TT_drawRaid(ctx,W,H,raid,now); return; }
  if(!party||!party.length){ return; }
  var dt=Math.min(60, now-(TT_LAST||now)); TT_LAST=now;
  var n=Math.min(party.length,20);
  // smaller sprites: base the unit on height, capped small
  var u=Math.max(1.1, Math.min(H/22, 4.2));
  var groundY=H-Math.max(4,u*1.4);
  var margin=u*6;
  var present={};
  for(var i=0;i<n;i++){
    var pc=party[i]; var key=pc.name||('#'+i); present[key]=1;
    var st=TT_POS[key];
    if(!st){ st={ x: margin + (W-2*margin)*((i+0.5)/n), vx:0, wanderAt:0 }; TT_POS[key]=st; }
    var moving=false, flip=false;
    if(pc.action==='idle'){
      if(now>=st.wanderAt){ var dir=Math.random()<0.5?-1:1; st.vx=(Math.random()<0.35?0:1)*dir*(0.012+Math.random()*0.02)*u; st.wanderAt=now+1200+Math.random()*2600; }
      st.x+=st.vx*dt;
      if(st.x<margin){ st.x=margin; st.vx=Math.abs(st.vx); } if(st.x>W-margin){ st.x=W-margin; st.vx=-Math.abs(st.vx); }
      moving=Math.abs(st.vx)>0.002*u; flip=st.vx<0;
    } else { st.vx=0; } // acting: stand still
    // name
    ctx.font='700 '+Math.max(8,Math.round(u*2.6))+'px Segoe UI, sans-serif';ctx.textAlign='center';ctx.textBaseline='alphabetic';
    var nm=(pc.name||'').slice(0,12);
    ctx.lineWidth=Math.max(2,u*0.7);ctx.strokeStyle='rgba(0,0,0,0.85)';ctx.strokeText(nm,st.x,groundY-u*(pc.fight?14:12.5));
    ctx.fillStyle='#fff';ctx.fillText(nm,st.x,groundY-u*(pc.fight?14:12.5));
    TT_drawChar(ctx,st.x,groundY,u,pc.look,pc.action||'idle',now+i*137,pc.fight||null,{flip:flip,moving:moving});
  }
  // prune positions of heroes who left
  for(var k in TT_POS){ if(!present[k]) delete TT_POS[k]; }
}
`;
