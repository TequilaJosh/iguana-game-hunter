// Server-side monster sprite renderer — draws the SAME procedural sprites the party
// overlay uses (spriteEngine.js) to a PNG, so the Discord bot can show a monster when
// it announces a fight. Uses @napi-rs/canvas (prebuilt, no system deps). Cached per id.
import { createCanvas } from '@napi-rs/canvas';
import { SPRITE_JS } from './spriteEngine.js';
import { MONSTERS } from './content.js';

// Rebuild the browser sprite functions in Node scope and grab the monster drawer.
// (SPRITE_JS is plain JS with no imports; new Function gives it its own scope.)
const _api = new Function(SPRITE_JS + '\n;return { TT_drawMonster: TT_drawMonster };')();

const _cache = new Map();

/** PNG Buffer for a monster's sprite, or null if the id is unknown. Cached. */
export function monsterPng(monId) {
  if (_cache.has(monId)) return _cache.get(monId);
  const m = MONSTERS[monId];
  if (!m) return null;
  const W = 200, H = 200;
  const canvas = createCanvas(W, H);
  const ctx = canvas.getContext('2d');
  const boss = m.rank === 'boss' || m.rank === 'elite';
  const s = boss ? 15 : 16;
  api_draw(ctx, W * 0.5, H - 26, s, m, boss);
  const buf = canvas.toBuffer('image/png');
  _cache.set(monId, buf);
  return buf;
}

function api_draw(ctx, cx, groundY, s, m, boss) {
  _api.TT_drawMonster(
    ctx, cx, groundY, s,
    { id: m.id, family: m.family, element: m.element, tier: m.tier, rank: m.rank, name: m.name },
    0, { boss }
  );
}
