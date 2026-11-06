import fs from 'node:fs/promises';
import path from 'node:path';
async function walk(folder) {return (await Promise.all((await fs.readdir(folder, {withFileTypes:true})).map(item => item.isDirectory() ? walk(path.join(folder,item.name)) : [path.join(folder,item.name)]))).flat();}
const rows=[];
for (const file of (await walk('Assets/BridgeBuilder/Data/Contract')).filter(file=>file.endsWith('.asset'))) {
  const source = await fs.readFile(file,'utf8'), meta=await fs.readFile(file+'.meta','utf8');
  rows.push({id:source.match(/^  contractID: (.*)$/m)?.[1].trim(), guid:meta.match(/^guid: (.*)$/m)?.[1].trim(),file});
}
console.log(JSON.stringify(rows.map(({id,guid})=>({id,guid})),null,2));
for (const scene of ['Main Menu','BHAN HOUSE','CanyonCrossing']) {
  const source=await fs.readFile('Assets/Scenes/'+scene+'.unity','utf8');
  const index=source.indexOf('guid: fbe92ff9608ed654894f6ceb2d977492');
  const end=source.indexOf('\n--- ',index), component=source.slice(index,end<0?undefined:end);
  const list=component.match(/^  allGameContracts:\r?\n((?:  - [^\r\n]*(?:\r?\n|$))*)/m)?.[1]??'';
  const guids=new Set([...list.matchAll(/guid: (\w+)/g)].map(match=>match[1]));
  console.log(JSON.stringify({scene,entries:guids.size,missing:rows.filter(row=>!guids.has(row.guid)).map(row=>({id:row.id,guid:row.guid}))}));
}
