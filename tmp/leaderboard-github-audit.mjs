import {spawnSync} from 'node:child_process';
const base='https://api.github.com/repos/Astra1127/CivilCraft';
const credential=spawnSync('git',['credential','fill'],{input:'protocol=https\nhost=github.com\n\n',encoding:'utf8',env:{...process.env,GIT_TERMINAL_PROMPT:'0',GCM_INTERACTIVE:'Never'}});
const fields=Object.fromEntries(credential.stdout.split(/\r?\n/).filter(line=>line.includes('=')).map(line=>[line.slice(0,line.indexOf('=')),line.slice(line.indexOf('=')+1)]));
const headers={'Accept':'application/vnd.github+json','User-Agent':'CivilCraft-Leaderboard-Verification','X-GitHub-Api-Version':'2022-11-28'};
if(fields.password)headers.Authorization='Basic '+Buffer.from(`${fields.username}:${fields.password}`).toString('base64');
async function get(suffix){const response=await fetch(base+suffix,{headers});return {status:response.status,data:await response.json()};}
const [repo,status,deployments]=await Promise.all([get(''),get('/commits/main/status'),get('/deployments?environment=Production&per_page=2')]);
console.log(JSON.stringify({credentialAvailable:!!fields.password,repositoryStatus:repo.status,push:repo.data.permissions?.push??false,defaultBranch:repo.data.default_branch,
  status:status.status,sha:status.data.sha,checks:status.data.statuses?.map(item=>({state:item.state,context:item.context,target:item.target_url})),
  deploymentsStatus:deployments.status,deployments:Array.isArray(deployments.data)?deployments.data.map(item=>({id:item.id,sha:item.sha,environment:item.environment})):[]},null,2));
