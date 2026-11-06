import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const ids = ['MarshID','FordMainContract','BrooksMainContract','RonanMainContract','ClarkMainContract','ClarkOffsetRoad','MillsMainContract','MillsWorkersCrossing','MillsReconnectingArcadia'];
async function walk(folder){return (await Promise.all((await fs.readdir(folder,{withFileTypes:true})).map(item=>item.isDirectory()?walk(path.join(folder,item.name)):[path.join(folder,item.name)]))).flat();}
const files=(await walk('Assets/BridgeBuilder/Data/Contract')).filter(file=>file.endsWith('.asset'));
assert.equal(files.length,19);
const backup='LocalBackups/LeaderboardRegionalOffline-'+new Date().toISOString().replace(/[:.]/g,'-');
let changed=0;
for(const file of files){
  const before=await fs.readFile(file,'utf8');
  const id=before.match(/^  contractID: (.*)$/m)?.[1].trim();
  if(!ids.includes(id))continue;
  assert.match(before,/^  hideFromLeaderboard: [01]\r?$/m);
  const after=before.replace(/^  hideFromLeaderboard: 1(?=\r?$)/m,'  hideFromLeaderboard: 0');
  if(before!==after){
    const copy=path.join(backup,file);await fs.mkdir(path.dirname(copy),{recursive:true});await fs.writeFile(copy,before);
    assert.equal(before.replace(/^  hideFromLeaderboard: [01]/m,''),after.replace(/^  hideFromLeaderboard: [01]/m,''));
    await fs.writeFile(file,after);changed++;
  }
}
for(const id of ids){
  const found=await Promise.all(files.map(async file=>({file,text:await fs.readFile(file,'utf8')})));
  const matches=found.filter(row=>row.text.match(/^  contractID: (.*)$/m)?.[1].trim()===id);
  assert.equal(matches.length,1);assert.match(matches[0].text,/^  hideFromLeaderboard: 0\r?$/m);
}
console.log(`PASS: All nine regional contracts locally eligible; ${changed} flags changed, all other serialized content preserved. Backup: ${backup}`);
