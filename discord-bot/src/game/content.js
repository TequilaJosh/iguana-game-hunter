import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const dir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'game-data');
const content = JSON.parse(fs.readFileSync(path.join(dir, 'content.json'), 'utf8'));
const monstersFile = JSON.parse(fs.readFileSync(path.join(dir, 'monsters.json'), 'utf8'));

const byId = (arr) => Object.fromEntries(arr.map((x) => [x.id, x]));

export const RACES = byId(content.races);
export const CLASSES = byId(content.classes);
export const SKILLS = byId(content.skills);
export const ITEMS = byId(content.items);
export const ZONES = byId(content.zones);
export const RARITIES = content.rarities; // ordered common..legendary
export const AFFIXES = content.affixes; // { prefix:[], suffix:[] }
export const MONSTERS = byId(monstersFile.monsters);

export const RACE_LIST = content.races;
export const CLASS_LIST = content.classes;
export const ZONE_LIST = content.zones;

export const skillsForClass = (clsId, level = 99) =>
  content.skills.filter((s) => s.class === clsId && s.unlock_level <= level);

// Items in a given gear drop-table (e.g. "gear_t3"), excluding consumables/materials.
export const gearInTable = (table) =>
  content.items.filter((i) => i.drop_table === table && ['weapon', 'head', 'body', 'shield', 'feet', 'accessory'].includes(i.slot));

export const STAT_KEYS = ['str', 'mag', 'vit', 'spr', 'agi', 'lck'];
