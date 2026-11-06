import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';
const website = 'D:/THESIS/CivilCraftWebsiteNiRem/CivilCraftWebsite-LeaderboardUpdate';
const shared = await import(pathToFileURL(website + '/src/lib/playfab/leaderboard-shared.ts').href);
const assets = await fs.readdir(website + '/.output/public/assets');
const bundle = assets.find(file => /^leaderboard-shared-.*\.js$/.test(file));
assert.ok(bundle);
const response = await fetch('https://civil-craft.vercel.app/assets/' + bundle, {cache: 'no-store'});
assert.equal(response.status, 200);
const text = await response.text();
for (const [id, label] of Object.entries(shared.LEADERBOARD_CONTRACT_LABELS)) {
  assert.ok(text.includes(id), 'Missing permanent ID in live website catalog: ' + id);
  assert.ok(text.includes(label), 'Missing current title in live website catalog: ' + id);
}
console.log('PASS: The deployed website bundle contains all 18 permanent IDs and current display labels.');
