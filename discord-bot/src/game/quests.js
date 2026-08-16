import { grantXp } from './engine.js';

// A rotating daily quest per hero. One quest a day, of one type; matching actions
// count toward it automatically.
const QUEST_TYPES = [
  { type: 'gather', target: [10, 20], gold: [120, 220], xp: [40, 70],  desc: (t) => `Gather ${t} materials` },
  { type: 'craft',  target: [3, 6],   gold: [140, 240], xp: [50, 80],  desc: (t) => `Craft or brew ${t} items` },
  { type: 'win',    target: [3, 6],   gold: [160, 260], xp: [60, 100], desc: (t) => `Win ${t} battles` },
  { type: 'box',    target: [2, 4],   gold: [120, 200], xp: [40, 70],  desc: (t) => `Open ${t} mystery boxes` },
];

const today = () => Math.floor(Date.now() / 86400000);   // UTC day index
const randInt = (lo, hi) => lo + Math.floor(Math.random() * (hi - lo + 1));

// Get today's quest, rolling a fresh one at the start of each day.
export function ensureQuest(char) {
  const day = today();
  if (!char.quest || char.quest.day !== day) {
    const q = QUEST_TYPES[Math.floor(Math.random() * QUEST_TYPES.length)];
    const target = randInt(q.target[0], q.target[1]);
    char.quest = {
      day, type: q.type, target, progress: 0, claimed: false,
      gold: randInt(q.gold[0], q.gold[1]), xp: randInt(q.xp[0], q.xp[1]), desc: q.desc(target),
    };
  }
  return char.quest;
}

// Record progress from a matching action (called from gather/craft/win/box handlers).
export function questProgress(char, type, amount = 1) {
  const q = ensureQuest(char);
  if (q.claimed || q.type !== type) return;
  q.progress = Math.min(q.target, q.progress + amount);
}

// Claim the reward once complete. Returns { ok, reason?, q, levels? }.
export function questClaim(char) {
  const q = ensureQuest(char);
  if (q.claimed) return { ok: false, reason: 'claimed', q };
  if (q.progress < q.target) return { ok: false, reason: 'incomplete', q };
  q.claimed = true;
  char.gold = (char.gold || 0) + q.gold;
  const levels = grantXp(char, q.xp);
  return { ok: true, q, levels };
}
