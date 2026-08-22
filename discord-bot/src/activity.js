// Tracks which heroes have been active recently and what they're currently doing,
// so the /party sprite overlay can show them fighting / gathering / crafting / idling.

const WINDOW_MS = 15 * 60 * 1000; // "active in chat in the last 15 minutes"
const GATHER = new Set(['chop', 'mine', 'fish', 'forage', 'dig', 'scavenge']);

// How long an action's animation lingers after the command (fights auto-resolve fast
// in chat, so we hold the pose a beat so viewers can see it).
const DUR = { fight: 9000, craft: 6500, gather: 6000 };

const active = new Map(); // id -> { id, name, cls, action, actionStart, until, lastSeen }

// Map a raw command word to an overlay action, or null to just keep the hero alive (idle).
export function actionForCommand(cmd) {
  const c = (cmd || '').toLowerCase();
  if (['adventure', 'explore', 'hunt', 'boss', 'raid', 'attack', 'a', 'skill', 'cast', 'use', 'potion', 'flee', 'run'].includes(c)) return 'fight';
  if (GATHER.has(c)) return c;
  if (['craft', 'brew', 'enchant'].includes(c)) return 'craft';
  return null; // other commands: keep them on screen, but idle
}

export function recordActivity(id, name, cls, action) {
  if (!id) return;
  const now = Date.now();
  const prev = active.get(id);
  let act = 'idle', start = now, until = now;
  if (action) {
    act = action;
    until = now + (action === 'fight' ? DUR.fight : action === 'craft' ? DUR.craft : DUR.gather);
  } else if (prev && prev.until > now) {
    // a non-action command mid-animation shouldn't cancel the current pose
    act = prev.action; start = prev.actionStart; until = prev.until;
  }
  active.set(id, { id, name: name || prev?.name || '?', cls: cls || prev?.cls || '', action: act, actionStart: start, until, lastSeen: now });
}

// The current party: heroes seen within the window, with their live action.
export function getParty() {
  const now = Date.now();
  const out = [];
  for (const [id, e] of active) {
    if (now - e.lastSeen > WINDOW_MS) { active.delete(id); continue; }
    out.push({ id, name: e.name, cls: e.cls, action: e.until > now ? e.action : 'idle', actionStart: e.actionStart });
  }
  return out;
}
