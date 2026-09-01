// Reaction-driven pager for long lists (e.g. the recipe book) in Discord.
// The bot posts page 1 and adds numbered keycap reactions; whichever page emoji has
// the most user reactions is the one shown (so the community "votes" the page). Edits
// the same message in place. State is in-memory and expires after a while.
import { log } from '../logger.js';

const KEYCAPS = ['1️⃣', '2️⃣', '3️⃣', '4️⃣', '5️⃣', '6️⃣', '7️⃣', '8️⃣', '9️⃣'];
const pagers = new Map();          // messageId -> { pages, emojis }
const TTL_MS = 10 * 60 * 1000;     // stop tracking after 10 minutes

// Emoji names vary by a trailing variation selector (U+FE0F); compare without it.
const norm = (s) => String(s || '').replace(/️/g, '');

/**
 * Turn a sent Discord message into a pager. `emojis` (one per page) are the reactions
 * to add — pass category symbols, or omit for numbered pages. Whichever emoji has the
 * most user reactions selects the shown page.
 */
export async function registerPager(message, pages, emojis) {
  if (!message || !message.id || !Array.isArray(pages) || pages.length < 2) return;
  const marks = (Array.isArray(emojis) && emojis.length === pages.length) ? emojis : KEYCAPS.slice(0, pages.length);
  pagers.set(message.id, { pages, emojis: marks });
  setTimeout(() => pagers.delete(message.id), TTL_MS);
  for (let i = 0; i < marks.length; i++) {
    try { await message.react(marks[i]); } catch { break; }
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
    for (let i = 0; i < st.emojis.length; i++) {
      const want = norm(st.emojis[i]);
      const r = cache.find((rc) => norm(rc.emoji.name) === want);
      const count = r ? Math.max(0, (r.count || 0) - (r.me ? 1 : 0)) : 0;   // exclude the bot's own
      if (count > bestCount) { bestCount = count; best = i; }
    }
    await msg.edit(st.pages[best]).catch(() => {});
  } catch (e) { log.error('recipe pager failed:', e.message); }
}
