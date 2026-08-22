// Shared browser-side sprite engine for the Tavern Tales party overlay. Exported as a
// plain-JS string (NO backticks / ${ } inside) so it can be embedded into overlay pages
// AND pasted into Game Hunter's LiveOverlay.html. Entry point: TT_drawParty(canvas, party, now).
export const SPRITE_JS = `
function _rr(ctx,x,y,w,h,r){ctx.beginPath();ctx.moveTo(x+r,y);ctx.arcTo(x+w,y,x+w,y+h,r);ctx.arcTo(x+w,y+h,x,y+h,r);ctx.arcTo(x,y+h,x,y,r);ctx.arcTo(x,y,x+w,y,r);ctx.closePath();}
function _lerp(a,b,t){return a+(b-a)*t;}
function _shade(hex,f){var n=parseInt(hex.slice(1),16),r=(n>>16)&255,g=(n>>8)&255,b=n&255;r=Math.max(0,Math.min(255,Math.round(r*f)));g=Math.max(0,Math.min(255,Math.round(g*f)));b=Math.max(0,Math.min(255,Math.round(b*f)));return 'rgb('+r+','+g+','+b+')';}

// A weapon/tool drawn from the hand along +x (blade points right); caller rotates.
function _drawItem(ctx,kind,u){
  ctx.lineCap='round';
  if(kind==='sword'){ctx.strokeStyle='#c9ccd6';ctx.lineWidth=1.4*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(6.5*u,0);ctx.stroke();ctx.strokeStyle='#6b5330';ctx.lineWidth=1.6*u;ctx.beginPath();ctx.moveTo(-0.2*u,-1.3*u);ctx.lineTo(-0.2*u,1.3*u);ctx.stroke();}
  else if(kind==='greatsword'){ctx.strokeStyle='#d5d8e2';ctx.lineWidth=2*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(8.5*u,0);ctx.stroke();ctx.strokeStyle='#5a4326';ctx.lineWidth=2*u;ctx.beginPath();ctx.moveTo(0,-1.8*u);ctx.lineTo(0,1.8*u);ctx.stroke();}
  else if(kind==='axe'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.3*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(6*u,0);ctx.stroke();ctx.fillStyle='#c9ccd6';ctx.beginPath();ctx.moveTo(5*u,-0.4*u);ctx.quadraticCurveTo(8*u,-2.6*u,8.2*u,0.2*u);ctx.quadraticCurveTo(8*u,2.6*u,5*u,0.6*u);ctx.closePath();ctx.fill();}
  else if(kind==='mace'){ctx.strokeStyle='#6b5330';ctx.lineWidth=1.4*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(5.5*u,0);ctx.stroke();ctx.fillStyle='#9aa0ad';ctx.beginPath();ctx.arc(6.6*u,0,1.8*u,0,7);ctx.fill();}
  else if(kind==='dagger'){ctx.strokeStyle='#d5d8e2';ctx.lineWidth=1.2*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(4*u,0);ctx.stroke();ctx.strokeStyle='#5a4326';ctx.lineWidth=1.3*u;ctx.beginPath();ctx.moveTo(0,-1*u);ctx.lineTo(0,1*u);ctx.stroke();}
  else if(kind==='bow'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.2*u;ctx.beginPath();ctx.arc(1*u,0,4*u,-1.1,1.1);ctx.stroke();ctx.strokeStyle='#eee';ctx.lineWidth=0.5*u;ctx.beginPath();ctx.moveTo(1*u+4*u*Math.cos(-1.1),4*u*Math.sin(-1.1));ctx.lineTo(1*u+4*u*Math.cos(1.1),4*u*Math.sin(1.1));ctx.stroke();}
  else if(kind==='staff'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.3*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(8.5*u,0);ctx.stroke();ctx.fillStyle='#6fd0ff';ctx.beginPath();ctx.arc(8.7*u,0,1.6*u,0,7);ctx.fill();}
  else if(kind==='hammer'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.5*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(6.2*u,0);ctx.stroke();ctx.fillStyle='#9aa0ad';_rr(ctx,5.6*u,-2*u,2.6*u,4*u,0.6*u);ctx.fill();}
  // tools
  else if(kind==='pick'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.3*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(7*u,0);ctx.stroke();ctx.strokeStyle='#9aa0ad';ctx.lineWidth=1.2*u;ctx.beginPath();ctx.moveTo(7*u,-2.6*u);ctx.quadraticCurveTo(8.2*u,0,7*u,2.6*u);ctx.stroke();}
  else if(kind==='axe_tool'){_drawItem(ctx,'axe',u);}
  else if(kind==='rod'){ctx.strokeStyle='#8a6a3a';ctx.lineWidth=1*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(9*u,-1.5*u);ctx.stroke();}
  else if(kind==='shovel'){ctx.strokeStyle='#7a5a34';ctx.lineWidth=1.3*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(6.5*u,0);ctx.stroke();ctx.fillStyle='#b8bcc6';ctx.beginPath();ctx.moveTo(6.3*u,-1.6*u);ctx.lineTo(8.6*u,-1.6*u);ctx.lineTo(8.9*u,1.6*u);ctx.lineTo(6.3*u,1.6*u);ctx.closePath();ctx.fill();}
  else if(kind==='basket'){ctx.fillStyle='#8a6a3a';_rr(ctx,3*u,-1.2*u,3*u,2.6*u,0.6*u);ctx.fill();}
  ctx.lineCap='butt';
}

// A ground prop for the current action, drawn to the RIGHT of the character.
function _drawProp(ctx,action,px,groundY,u,now){
  ctx.save();
  if(action==='fight'){ // a little monster blob
    var bob=Math.sin(now/220)*0.6*u;
    ctx.fillStyle='#8a5db0';ctx.beginPath();ctx.ellipse(px,groundY-2.6*u+bob,3*u,2.8*u,0,0,7);ctx.fill();
    ctx.fillStyle='#fff';ctx.beginPath();ctx.arc(px-0.9*u,groundY-3*u+bob,0.6*u,0,7);ctx.arc(px+0.9*u,groundY-3*u+bob,0.6*u,0,7);ctx.fill();
    ctx.fillStyle='#201';ctx.beginPath();ctx.arc(px-0.9*u,groundY-3*u+bob,0.3*u,0,7);ctx.arc(px+0.9*u,groundY-3*u+bob,0.3*u,0,7);ctx.fill();
  } else if(action==='mine'){ ctx.fillStyle='#77747c';ctx.beginPath();ctx.moveTo(px-2.4*u,groundY);ctx.lineTo(px-1.6*u,groundY-3*u);ctx.lineTo(px+1.4*u,groundY-3.4*u);ctx.lineTo(px+2.6*u,groundY);ctx.closePath();ctx.fill();ctx.fillStyle='#b9c7ff';ctx.fillRect(px-0.4*u,groundY-2.2*u,0.9*u,0.9*u);ctx.fillRect(px+1*u,groundY-1.4*u,0.7*u,0.7*u);
  } else if(action==='chop'){ ctx.fillStyle='#6b4a2a';ctx.fillRect(px-1*u,groundY-6*u,2*u,6*u);ctx.fillStyle='#3f8a45';ctx.beginPath();ctx.arc(px,groundY-7*u,3.2*u,0,7);ctx.fill();
  } else if(action==='dig'){ ctx.fillStyle='#6a4a2c';ctx.beginPath();ctx.ellipse(px,groundY-0.4*u,3*u,1.4*u,0,0,7);ctx.fill();ctx.fillStyle='#4f3720';ctx.beginPath();ctx.ellipse(px,groundY-1.2*u,1.8*u,0.9*u,0,0,7);ctx.fill();
  } else if(action==='fish'){ ctx.fillStyle='rgba(80,150,220,0.55)';ctx.beginPath();ctx.ellipse(px+1.5*u,groundY-0.2*u,4.5*u,1.6*u,0,0,7);ctx.fill();
  } else if(action==='forage'){ ctx.fillStyle='#357a3e';ctx.beginPath();ctx.arc(px,groundY-1.6*u,2.4*u,0,7);ctx.fill();ctx.fillStyle='#e46b9c';ctx.beginPath();ctx.arc(px-1*u,groundY-2.4*u,0.6*u,0,7);ctx.arc(px+1.2*u,groundY-1.6*u,0.6*u,0,7);ctx.fill();
  } else if(action==='scavenge'){ ctx.fillStyle='#7c6a52';ctx.beginPath();ctx.moveTo(px-2.6*u,groundY);ctx.lineTo(px,groundY-2.6*u);ctx.lineTo(px+2.6*u,groundY);ctx.closePath();ctx.fill();ctx.fillStyle='#9a8a70';ctx.fillRect(px-0.6*u,groundY-1.8*u,1.2*u,0.8*u);
  } else if(action==='craft'){ ctx.fillStyle='#3a3f47';_rr(ctx,px-2.6*u,groundY-2.4*u,5.2*u,1.6*u,0.4*u);ctx.fill();ctx.fillStyle='#2b2f36';ctx.fillRect(px-1*u,groundY-1.2*u,2*u,1.2*u);ctx.fillStyle='#4a5059';_rr(ctx,px-2*u,groundY-3.1*u,3*u,1.1*u,0.3*u);ctx.fill(); }
  ctx.restore();
}

function _spark(ctx,x,y,u,c){ctx.strokeStyle=c||'#ffd34a';ctx.lineWidth=0.6*u;for(var i=0;i<5;i++){var a=i/5*6.28;ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x+Math.cos(a)*2*u,y+Math.sin(a)*2*u);ctx.stroke();}}

// Draw one hero: feet at (cx,groundY), unit u; action drives the pose/prop.
function TT_drawChar(ctx,cx,groundY,u,look,action,now){
  var CHOP=(action==='mine'||action==='chop'||action==='dig'||action==='craft');
  var period = action==='fight'?560:(CHOP?720:(action==='fish'?1700:(action==='forage'||action==='scavenge'?900:1400)));
  var p=(now%period)/period;
  var bob=Math.sin(now/ (action==='idle'?520:340))*(action==='idle'?0.7:0.35)*u;
  var bend=0, lunge=0, impact=false, armA=0.4;
  if(action==='fight'){ var A_UP=-0.9,A_DN=0.7; if(p<0.5) armA=_lerp(A_DN,A_UP,p/0.5); else { var q=(p-0.5)/0.5; armA=_lerp(A_UP,A_DN,q*q); } lunge=(p>0.5?(1-Math.abs(p-0.72)/0.28):0)*1.4*u; impact=(p>0.66&&p<0.8); }
  else if(CHOP){ var U=-2.35,D=0.55; if(p<0.45) armA=_lerp(D,U,p/0.45); else if(p<0.72){var q2=(p-0.45)/0.27; armA=_lerp(U,D,q2*q2);} else armA=D; impact=(p>0.66&&p<0.78); }
  else if(action==='fish'){ armA=-0.35+Math.sin(now/500)*0.05; }
  else if(action==='forage'||action==='scavenge'){ bend=(Math.sin(p*6.28)*0.5+0.5)*0.5; armA=0.9+ (action==='scavenge'?Math.sin(now/90)*0.4:Math.sin(p*6.28)*0.3); }

  var propX=cx+7*u;
  _drawProp(ctx,action,propX,groundY,u,now);

  ctx.save();
  ctx.translate(cx+lunge, groundY+bob);
  ctx.rotate(bend*0.5); // bend forward for forage/scavenge
  var skin=look.skin,hair=look.hair,out=look.outfit,style=look.style;

  // shadow
  ctx.fillStyle='rgba(0,0,0,0.25)';ctx.beginPath();ctx.ellipse(0,0.6*u,3.2*u,1*u,0,0,7);ctx.fill();
  // legs
  ctx.strokeStyle=_shade(out,0.6);ctx.lineWidth=1.5*u;ctx.lineCap='round';
  ctx.beginPath();ctx.moveTo(-1.2*u,-3*u);ctx.lineTo(-1.2*u,-0.2*u);ctx.moveTo(1.2*u,-3*u);ctx.lineTo(1.2*u,-0.2*u);ctx.stroke();
  // torso / outfit
  ctx.fillStyle=out;
  if(style==='robe'){ ctx.beginPath();ctx.moveTo(-2.6*u,-8*u);ctx.lineTo(2.6*u,-8*u);ctx.lineTo(3.4*u,-0.2*u);ctx.lineTo(-3.4*u,-0.2*u);ctx.closePath();ctx.fill(); }
  else { _rr(ctx,-2.6*u,-8*u,5.2*u,5.6*u,1*u);ctx.fill(); if(style==='armor'){ctx.strokeStyle=_shade(out,1.4);ctx.lineWidth=0.5*u;ctx.beginPath();ctx.moveTo(-2.6*u,-5.4*u);ctx.lineTo(2.6*u,-5.4*u);ctx.moveTo(0,-8*u);ctx.lineTo(0,-2.6*u);ctx.stroke();} }
  if(style==='cloak'){ ctx.fillStyle=_shade(out,0.7);ctx.beginPath();ctx.moveTo(-2.8*u,-8.2*u);ctx.lineTo(-4*u,-1*u);ctx.lineTo(-1.5*u,-2*u);ctx.closePath();ctx.fill(); }
  // back arm
  ctx.strokeStyle=skin;ctx.lineWidth=1.3*u;ctx.beginPath();ctx.moveTo(-2*u,-7.2*u);ctx.lineTo(-3*u,-4.4*u);ctx.stroke();
  // head
  ctx.fillStyle=skin;ctx.beginPath();ctx.arc(0,-10.4*u,2.3*u,0,7);ctx.fill();
  // hair
  ctx.fillStyle=hair;ctx.beginPath();ctx.arc(0,-10.9*u,2.35*u,3.14,6.28);ctx.fill();ctx.fillRect(-2.35*u,-11*u,4.7*u,1.1*u);
  // eyes (blink)
  var blink=(now%3200)<120;ctx.fillStyle='#20242c';if(!blink){ctx.beginPath();ctx.arc(-0.9*u,-10.2*u,0.32*u,0,7);ctx.arc(0.7*u,-10.2*u,0.32*u,0,7);ctx.fill();}else{ctx.fillRect(-1.2*u,-10.2*u,0.7*u,0.2*u);ctx.fillRect(0.4*u,-10.2*u,0.7*u,0.2*u);}

  // front arm + held item
  var sx=2*u, sy=-7.2*u, aLen=3.2*u;
  ctx.save();
  ctx.translate(sx,sy);
  ctx.rotate(armA);
  ctx.strokeStyle=skin;ctx.lineWidth=1.3*u;ctx.beginPath();ctx.moveTo(0,0);ctx.lineTo(aLen,0);ctx.stroke();
  ctx.save();ctx.translate(aLen,0);
  var held = action==='mine'?'pick':action==='chop'?'axe':action==='dig'?'shovel':action==='craft'?'hammer':action==='fish'?'rod':(action==='forage'||action==='scavenge')?'basket':look.weapon;
  _drawItem(ctx,held,u);
  ctx.restore();
  ctx.restore();

  ctx.restore();

  // impact spark on the prop
  if(impact){ if(action==='craft') _spark(ctx,propX-0.5*u,groundY-2.8*u,u,'#ffd34a'); else if(CHOP) _spark(ctx,propX-1*u,groundY-2*u,u,'#e8dcc0'); else if(action==='fight') _spark(ctx,propX,groundY-2.8*u,u,'#ff6a6a'); }
  // fishing line
  if(action==='fish'){ var dip=Math.sin(now/450)>0.7?1.2*u:0; ctx.strokeStyle='rgba(230,230,230,0.8)';ctx.lineWidth=0.4*u;ctx.beginPath();ctx.moveTo(cx+7.5*u,groundY-6.5*u);ctx.lineTo(propX+1.5*u,groundY-0.4*u+dip);ctx.stroke(); }
}

// Render the whole party across the canvas (single row, auto-scaled to fit).
function TT_drawParty(canvas,party,now){
  var ctx=canvas.getContext('2d');
  var W=canvas.width,H=canvas.height;
  ctx.clearRect(0,0,W,H);
  if(!party||!party.length) return;
  var n=Math.min(party.length,16);
  var cell=Math.min(W/n, H*0.9);
  var u=Math.max(1.4, Math.min(cell/22, H/26));
  var groundY=H-Math.max(6, u*1.5);
  for(var i=0;i<n;i++){
    var pc=party[i];
    var cx=cell*(i+0.5);
    // name label
    ctx.font='700 '+Math.max(9,Math.round(u*3))+'px Segoe UI, sans-serif';
    ctx.textAlign='center';ctx.textBaseline='alphabetic';
    var nm=(pc.name||'').slice(0,12);
    ctx.lineWidth=Math.max(2,u*0.8);ctx.strokeStyle='rgba(0,0,0,0.85)';ctx.strokeText(nm,cx,groundY-u*15.5);
    ctx.fillStyle='#fff';ctx.fillText(nm,cx,groundY-u*15.5);
    TT_drawChar(ctx,cx,groundY,u,pc.look,pc.action||'idle',now + i*137);
  }
}
`;
