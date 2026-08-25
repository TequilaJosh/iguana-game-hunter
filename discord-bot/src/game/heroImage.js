// Server-side hero sprite renderer — draws a character's sprite (TT_drawChar from
// spriteEngine.js, the same art the party overlay uses) to a PNG so the Discord bot
// can show it on the character sheet. Cached by the character's look signature, so a
// wardrobe change produces a fresh image.
import { createCanvas } from '@napi-rs/canvas';
import { SPRITE_JS } from './spriteEngine.js';
import { getLook } from './cosmetics.js';

const _api = new Function(SPRITE_JS + '\n;return { TT_drawChar: TT_drawChar };')();
const _cache = new Map();

/** PNG Buffer of a hero's idle sprite for the given character, or null on failure. */
export function heroPng(char) {
  try {
    const look = getLook(char) || {};
    const key = [look.skin, look.hair, look.outfit, look.style, look.weapon].join('|');
    if (_cache.has(key)) return _cache.get(key);
    const W = 180, H = 200;
    const canvas = createCanvas(W, H);
    const ctx = canvas.getContext('2d');
    // idle pose, eyes open (avoid the blink window), facing right holding their weapon
    _api.TT_drawChar(ctx, W * 0.5, H - 22, 13, look, 'idle', 800, null, {});
    const buf = canvas.toBuffer('image/png');
    _cache.set(key, buf);
    return buf;
  } catch { return null; }
}
