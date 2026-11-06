import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
const title = process.env.VITE_PLAYFAB_TITLE_ID;
const secret = process.env.PLAYFAB_SECRET_KEY;
if (!title || !secret || !/^[A-Fa-f0-9]+$/.test(title)) throw new Error('Missing configured PlayFab title credentials.');
async function admin(method, body) {
  const response = await fetch(`https://${title}.playfabapi.com/Admin/${method}`, {
    method:'POST', headers:{'Content-Type':'application/json','X-SecretKey':secret}, body:JSON.stringify(body)
  });
  const envelope = await response.json();
  if (!response.ok || envelope.code !== 200) throw new Error(`${method}: ${envelope.error ?? response.status}`);
  return envelope.data;
}
const versions = await admin('GetCloudScriptVersions', {});
const version = versions.Versions?.findLast(value => value.PublishedRevision > 0);
if (!version) throw new Error('No published CloudScript revision found.');
const live = await admin('GetCloudScriptRevision', {Version:version.Version,Revision:version.PublishedRevision});
if (!live.IsPublished) throw new Error('Published revision lookup did not return a published script.');
const definitions = await admin('GetPlayerStatisticDefinitions', {});
const visibility = await admin('GetTitleInternalData', {Keys:['civilcraft.leaderboard.visibility.v1']});
const folder = path.resolve(process.argv[2] ?? 'LocalBackups/LeaderboardLiveAudit');
await fs.mkdir(folder,{recursive:true});
await fs.writeFile(path.join(folder,'cloudscript-live.json'),JSON.stringify(live,null,2));
await fs.writeFile(path.join(folder,'statistics-before.json'),JSON.stringify(definitions,null,2));
await fs.writeFile(path.join(folder,'visibility-before.json'),JSON.stringify(visibility,null,2));
const source = live.Files.map(file=>file.FileContents).join('\n');
const handlers = [...source.matchAll(/handlers\.(\w+)\s*=/g)].map(match=>match[1]).sort();
console.log(JSON.stringify({title,version:live.Version,revision:live.Revision,latestRevision:version.LatestRevision,
  files:live.Files.map(file=>({name:file.Filename,bytes:Buffer.byteLength(file.FileContents),sha256:crypto.createHash('sha256').update(file.FileContents).digest('hex')})),
  handlers,bridgeStatisticCount:definitions.Statistics.filter(value=>/^CC_[ES]_/.test(value.StatisticName)).length,
  visibility:visibility.Data?.['civilcraft.leaderboard.visibility.v1'] ?? null,backup:folder},null,2));
