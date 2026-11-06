import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const origin = 'https://civil-craft.vercel.app';
const added = ['MarshID', 'FordMainContract', 'BrooksMainContract', 'RonanMainContract', 'ClarkMainContract', 'ClarkOffsetRoad', 'MillsMainContract', 'MillsWorkersCrossing', 'MillsReconnectingArcadia'];
const legacy = ['ShopKeeper', 'ReedsContract', 'VancesContract', 'ReedSideInspection', 'VanceSideRealignment', 'SilasMainContract', 'MainContractSilas'];
async function get(route) {
  for (let attempt = 0; attempt < 4; attempt++) {
    const response = await fetch(origin + route, {cache: 'no-store', signal: AbortSignal.timeout(30000)});
    if ((response.status === 429 || response.status >= 500) && attempt < 3) {
      await new Promise(resolve => setTimeout(resolve, 2000 * (attempt + 1)));
      continue;
    }
    return {status: response.status, data: await response.json()};
  }
}
const oldFeed = await get('/api/leaderboard/contracts');
const newFeed = await get('/api/leaderboard/contracts?catalogVersion=2');
assert.equal(oldFeed.status, 200); assert.equal(newFeed.status, 200);
assert.deepEqual(new Set(oldFeed.data.contractIds), new Set(legacy));
assert.deepEqual(new Set(newFeed.data.contractIds), new Set([...legacy, ...added]));
for (const id of ['TUT_CONTRACT1', 'TUT_CONTRACT2']) {
  assert.ok(!newFeed.data.contractIds.includes(id));
  assert.equal((await get('/api/leaderboard/public?' + new URLSearchParams({contractId: id, mode: 'efficient', start: '0'}))).status, 400);
}
const boards = [];
for (const id of [...added, 'ShopKeeper']) for (const mode of ['efficient', 'strongest']) {
  const result = await get('/api/leaderboard/public?' + new URLSearchParams({contractId: id, mode, start: '0'}));
  assert.equal(result.status, 200, `${id}/${mode} must load.`);
  assert.ok(Array.isArray(result.data.entries));
  assert.ok(Object.hasOwn(result.data, 'version'));
  // Only counts are recorded; no player names or account identifiers are retained.
  boards.push({id, mode, status: result.status, rows: result.data.entries.length, version: result.data.version});
  console.log(JSON.stringify(boards.at(-1)));
  await new Promise(resolve => setTimeout(resolve, 250));
}
const report = {verified: true, legacyVisible: legacy.length, regionalVisible: added.length,
  totalVisible: newFeed.data.contractIds.length, hiddenTutorialsPreserved: true, boards, checkedAt: new Date().toISOString()};
await fs.writeFile('LocalBackups/RegionalLeaderboardRollout-20261107/production-verification.json', JSON.stringify(report, null, 2));
console.log(JSON.stringify({verified: true, legacyVisible: legacy.length, regionalVisible: added.length,
  totalVisible: newFeed.data.contractIds.length, hiddenTutorialsPreserved: true}));
