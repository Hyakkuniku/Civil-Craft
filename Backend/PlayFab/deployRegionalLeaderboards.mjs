// Explicit rollout steps. Reads credentials from the environment; never writes player scores.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const mode = process.argv[2];
assert.ok(['stage', 'publish', 'enable', 'verify'].includes(mode), 'Use stage, publish, enable or verify.');
const folder = path.resolve(process.argv[3] ?? 'LocalBackups/RegionalLeaderboardRollout');
const manifestFile = path.join(folder, 'rollout.json');
const title = process.env.VITE_PLAYFAB_TITLE_ID;
const secret = process.env.PLAYFAB_SECRET_KEY;
assert.ok(title && /^[A-Fa-f0-9]+$/.test(title) && secret, 'Configured PlayFab title credentials are required.');
const added = ['MarshID', 'FordMainContract', 'BrooksMainContract', 'RonanMainContract', 'ClarkMainContract', 'ClarkOffsetRoad', 'MillsMainContract', 'MillsWorkersCrossing', 'MillsReconnectingArcadia'];
const policyKey = 'civilcraft.leaderboard.visibility.v1';
const tablePattern = /var ccLeaderboardContracts\s*=\s*\{[\s\S]*?\};/g;
const hash = text => crypto.createHash('sha256').update(text).digest('hex');
const handlers = text => [...text.matchAll(/handlers\.(\w+)\s*=/g)].map(value => value[1]).sort();
async function admin(operation, body = {}, attempt = 0) {
  const response = await fetch(`https://${title}.playfabapi.com/Admin/${operation}`, {
    method: 'POST', headers: {'Content-Type': 'application/json', 'X-SecretKey': secret},
    body: JSON.stringify(body), signal: AbortSignal.timeout(20000),
  });
  const envelope = await response.json();
  if ((response.status === 429 || envelope.error === 'APIClientRequestRateLimitExceeded') && attempt < 6) {
    const seconds = Number(response.headers.get('retry-after'));
    const delay = Number.isFinite(seconds) && seconds > 0 ? Math.min(15000, seconds * 1000) : Math.min(12000, 1500 * 2 ** attempt);
    await new Promise(resolve => setTimeout(resolve, delay));
    return admin(operation, body, attempt + 1);
  }
  if (!response.ok || envelope.code !== 200) throw new Error(`${operation}: ${envelope.error ?? response.status}`);
  return envelope.data;
}
async function live() {
  const versions = await admin('GetCloudScriptVersions');
  const version = versions.Versions?.findLast(value => value.PublishedRevision > 0);
  assert.ok(version, 'A published CloudScript revision is required.');
  const result = await admin('GetCloudScriptRevision', {Version: version.Version, Revision: version.PublishedRevision});
  assert.equal(result.IsPublished, true); assert.equal(result.Files.length, 1);
  return result;
}
async function save(name, value) {
  try { await fs.writeFile(path.join(folder, name), JSON.stringify(value, null, 2), {flag: name.endsWith('-before.json') ? 'wx' : 'w'}); }
  catch (error) { if (!(name.endsWith('-before.json') && error.code === 'EEXIST')) throw error; }
}
async function verifyDefinitions(manifest) {
  const result = await admin('GetPlayerStatisticDefinitions');
  for (const [id, suffix] of Object.entries(manifest.contracts)) for (const prefix of ['CC_E_', 'CC_S_']) {
    const definition = result.Statistics.find(value => value.StatisticName === prefix + suffix);
    assert.ok(definition, `Missing statistic for ${id}.`);
    assert.equal(definition.AggregationMethod, 'Max'); assert.equal(definition.VersionChangeInterval, 'Never');
  }
  for (const original of manifest.originalStatistics) {
    const current = result.Statistics.find(value => value.StatisticName === original.StatisticName);
    assert.ok(current, 'An existing statistic disappeared.');
    assert.equal(current.CurrentVersion, original.CurrentVersion, 'An existing leaderboard version changed.');
    assert.equal(current.AggregationMethod, original.AggregationMethod);
    assert.equal(current.VersionChangeInterval, original.VersionChangeInterval);
  }
  return result;
}
await fs.mkdir(folder, {recursive: true});
if (mode === 'stage') {
  const current = await live();
  const oldSource = current.Files[0].FileContents;
  const feature = await fs.readFile(new URL('./leaderboardCloudScript.js', import.meta.url), 'utf8');
  const tables = feature.match(tablePattern); assert.equal(tables?.length, 1);
  assert.equal(oldSource.match(tablePattern)?.length, 1, 'Expected the live leaderboard allowlist exactly once.');
  const parseTable = table => JSON.parse(table.slice(table.indexOf('{'), table.lastIndexOf('}') + 1));
  const contracts = parseTable(tables[0]);
  const oldContracts = parseTable(oldSource.match(tablePattern)[0]);
  assert.equal(Object.keys(contracts).length, 18);
  for (const [id, suffix] of Object.entries(oldContracts)) assert.equal(contracts[id], suffix, 'Existing statistic identity changed.');
  const source = oldSource.replace(tablePattern, tables[0]);
  assert.equal(source.replace(tablePattern, ''), oldSource.replace(tablePattern, ''), 'Unrelated live source changed.');
  assert.deepEqual(handlers(source), handlers(oldSource));
  const sandbox = {handlers: {}}; vm.runInNewContext(source, sandbox, {timeout: 1000});
  assert.equal(Object.keys(sandbox.handlers).length, 18, 'Existing CloudScript handlers must all survive.');
  const definitions = await admin('GetPlayerStatisticDefinitions');
  const visibility = await admin('GetTitleInternalData', {Keys: [policyKey]});
  await save('cloudscript-before.json', current); await save('statistics-before.json', definitions);
  await save('visibility-before.json', visibility); await fs.writeFile(path.join(folder, 'cloudscript-staged.js'), source);
  const manifest = {title, version: current.Version, beforeRevision: current.Revision, beforeHash: hash(oldSource),
    sourceHash: hash(source), contracts, originalStatistics: JSON.parse(await fs.readFile(path.join(folder, 'statistics-before.json'), 'utf8')).Statistics, created: [], handlers: handlers(source)};
  // Validate every existing board before creating any new definitions.
  for (const suffix of Object.values(contracts)) for (const prefix of ['CC_E_', 'CC_S_']) {
    const existing = definitions.Statistics.find(value => value.StatisticName === prefix + suffix);
    if (existing) { assert.equal(existing.AggregationMethod, 'Max'); assert.equal(existing.VersionChangeInterval, 'Never'); }
  }
  for (const suffix of Object.values(contracts)) for (const prefix of ['CC_E_', 'CC_S_']) {
    const name = prefix + suffix;
    if (definitions.Statistics.some(value => value.StatisticName === name)) continue;
    await admin('CreatePlayerStatisticDefinition', {StatisticName: name, AggregationMethod: 'Max', VersionChangeInterval: 'Never'});
    manifest.created.push(name); await save('rollout-progress.json', manifest);
    await new Promise(resolve => setTimeout(resolve, 1100));
  }
  const unchanged = await live(); assert.equal(hash(unchanged.Files[0].FileContents), manifest.beforeHash, 'Live source changed during preparation.');
  const staged = await admin('UpdateCloudScript', {Files: [{Filename: current.Files[0].Filename, FileContents: source}], Publish: false});
  manifest.version = staged.Version; manifest.stagedRevision = staged.Revision;
  const uploaded = await admin('GetCloudScriptRevision', {Version: staged.Version, Revision: staged.Revision});
  assert.equal(hash(uploaded.Files[0].FileContents), manifest.sourceHash); assert.equal(uploaded.IsPublished, false);
  await verifyDefinitions(manifest); await save('rollout.json', manifest);
  console.log(JSON.stringify({stage: 'verified', title, liveRevision: current.Revision, stagedRevision: staged.Revision,
    contracts: Object.keys(contracts).length, newStatistics: manifest.created.length, existingHandlersPreserved: manifest.handlers.length}));
} else {
  const manifest = JSON.parse(await fs.readFile(manifestFile, 'utf8')); assert.equal(manifest.title, title);
  if (mode === 'publish') {
    const current = await live();
    assert.ok(current.Revision === manifest.beforeRevision || current.Revision === manifest.stagedRevision, 'Another live revision was published. Review before continuing.');
    if (current.Revision === manifest.beforeRevision) assert.equal(hash(current.Files[0].FileContents), manifest.beforeHash);
    const staged = await admin('GetCloudScriptRevision', {Version: manifest.version, Revision: manifest.stagedRevision});
    assert.equal(hash(staged.Files[0].FileContents), manifest.sourceHash); await verifyDefinitions(manifest);
    if (current.Revision !== manifest.stagedRevision) await admin('SetPublishedRevision', {Version: manifest.version, Revision: manifest.stagedRevision});
    const published = await live(); assert.equal(published.Revision, manifest.stagedRevision);
    assert.equal(hash(published.Files[0].FileContents), manifest.sourceHash);
    console.log(JSON.stringify({published: true, revision: published.Revision, contracts: 18, existingScoresReset: false}));
  } else if (mode === 'enable') {
    // The new versioned endpoint must be live before adding IDs older clients reject.
    const response = await fetch('https://civil-craft.vercel.app/api/leaderboard/contracts?catalogVersion=2', {cache: 'no-store'});
    assert.equal(response.status, 200, 'Publish the updated website before enabling regional visibility.');
    const current = await live(); assert.equal(hash(current.Files[0].FileContents), manifest.sourceHash);
    const data = await admin('GetTitleInternalData', {Keys: [policyKey]});
    const selection = JSON.parse(data.Data?.[policyKey] ?? 'null');
    assert.equal(selection.schemaVersion, 1); assert.ok(Array.isArray(selection.contractIds));
    assert.ok(selection.contractIds.every(id => Object.hasOwn(manifest.contracts, id)));
    const next = {schemaVersion: 1, contractIds: [...new Set([...selection.contractIds, ...added])]};
    assert.deepEqual(next.contractIds.slice(0, selection.contractIds.length), selection.contractIds);
    await save('visibility-immediately-before-enable.json', data);
    await admin('SetTitleInternalData', {Key: policyKey, Value: JSON.stringify(next)});
    const check = await admin('GetTitleInternalData', {Keys: [policyKey]});
    assert.deepEqual(JSON.parse(check.Data[policyKey]), next);
    manifest.enabledIds = next.contractIds; await save('rollout.json', manifest);
    console.log(JSON.stringify({regionalBoardsEnabled: added.length, totalVisible: next.contractIds.length, previousChoicesPreserved: true}));
  } else {
    const current = await live(); assert.equal(hash(current.Files[0].FileContents), manifest.sourceHash);
    await verifyDefinitions(manifest);
    console.log(JSON.stringify({verified: true, revision: current.Revision, contractBoards: 18, statistics: 36,
      oldStatisticVersionsPreserved: true, allOtherHandlersPreserved: handlers(current.Files[0].FileContents).join() === manifest.handlers.join()}));
  }
}
