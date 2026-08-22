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
  if(action!=='idle') _drawProp(ctx,action,propX,groundY,u,now);

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

  if(impact){ if(action==='craft') _spark(ctx,propX-0.5*u,groundY-2.5*u,u,'#ffd34a'); else if(CHOP) _spark(ctx,propX-0.9*u,groundY-1.8*u,u,'#e8dcc0'); else if(action==='fight') _spark(ctx,propX,groundY-2.5*u,u,'#ff6a6a'); }
  if(action==='fish'){ var dip=Math.sin(now/450)>0.7?1*u:0; ctx.strokeStyle='rgba(230,230,230,0.8)';ctx.lineWidth=0.35*u;ctx.beginPath();ctx.moveTo(cx+6.5*u,groundY-6*u);ctx.lineTo(propX+1.3*u,groundY-0.3*u+dip);ctx.stroke(); }

  // HP bars over heads during a live fight
  if(fight){
    _hpBar(ctx,cx,groundY-11*u,u,fight.php,fight.pmaxhp,'#5fc27e');
    _hpBar(ctx,propX,groundY-6*u,u,fight.mhp,fight.mmaxhp,'#d46a6a');
  }
}

// ---- Whole-party render: heroes walk around, then act in place ----------------
var TT_POS={};      // name -> { x, vx, wanderAt }
var TT_LAST=0;
function TT_drawParty(canvas,party,now){
  var ctx=canvas.getContext('2d');
  var W=canvas.width,H=canvas.height;
  ctx.clearRect(0,0,W,H);
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
