// Reaction-driven pager for long lists (e.g. the recipe book) in Discord.
// The bot posts page 1 and adds numbered keycap reactions; whichever page emoji has
// the most user reactions is the one shown (so the community "votes" the page). Edits
// the same message in place. State is in-memory and expires after a while.
import { log } from '../logger.js';

const KEYCAPS = ['1️⃣', '2️⃣', '3️⃣', '4️⃣', '5️⃣', '6️⃣', '7️⃣', '8️⃣', '9️⃣'];
const pagers = new Map();          // messageId -> { pages }
const TTL_MS = 10 * 60 * 1000;     // stop tracking after 10 minutes

/** Post-process a sent Discord message into a pager: add reactions, remember pages. */
export async function registerPager(message, pages) {
  if (!message || !message.id || !Array.isArray(pages) || pages.length < 2) return;
  const n = Math.min(pages.length, KEYCAPS.length);
  pagers.set(message.id, { pages });
  setTimeout(() => pagers.delete(message.id), TTL_MS);
  for (let i = 0; i < n; i++) {
    try { await message.react(KEYCAPS[i]); } catch { break; }
  }
}

export function isPager(id) { return pagers.has(id); }

/** A reaction changed on a tracked message — recount and show the winning page. */
export async function handlePagerReaction(reaction, user) {
  try {
    if (user && user.bot) return;
    if (reaction.partial) { try { await reaction.fetch(); } catch { return; } }
    const msg = reaction.message;
    if (!msg || !pagers.has(msg.id)) return;
    const st = pagers.get(msg.id);
    const cache = msg.reactions.cache;
    let best = 0, bestCount = -1;
    for (let i = 0; i < st.pages.length && i < KEYCAPS.length; i++) {
      const r = cache.get(KEYCAPS[i]);
      const count = r ? Math.max(0, (r.count || 0) - (r.me ? 1 : 0)) : 0;   // exclude the bot's own
      if (count > bestCount) { bestCount = count; best = i; }
    }
    await msg.edit(st.pages[best]).catch(() => {});
  } catch (e) { log.error('recipe pager failed:', e.message); }
}
