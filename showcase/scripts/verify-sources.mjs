import {execFileSync} from 'node:child_process';
import {readFile,stat,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const revision='fbada64d67859a526529368c440b80fc6a3dc32a';
const tree=execFileSync('git',['ls-tree','-r','--name-only',revision],{cwd:'..',encoding:'utf8'}).split('\n');
const model=await readFile('src/model.ts','utf8');
const paths=[...new Set([...model.matchAll(/['"]((?:backend|frontend|docs)\/[^'"]+|README(?:\.tr)?\.md|VERIFICATION\.md)['"]/g)].map(m=>m[1]))];
for(const path of paths){if(!tree.includes(path))throw Error('Missing source '+path);await stat('../'+path);}
const media=JSON.parse(await readFile('src/media.json','utf8'));
for(const m of media){
 const data=await readFile('../'+m.source);
 if(createHash('sha256').update(data).digest('hex')!==m.sha256)throw Error('Capture hash mismatch '+m.id);
 const committed=execFileSync('git',['show',revision+':'+m.source],{cwd:'..',maxBuffer:20*1024*1024});
 if(!data.equals(committed))throw Error('Capture differs from audited commit '+m.id);
}
const changed=execFileSync('git',['diff','--name-only',revision],{cwd:'..',encoding:'utf8'}).trim().split('\n').filter(Boolean);
if(changed.some(p=>!p.startsWith('showcase/')))throw Error('Original application modified');
const result={revision,verifiedSourcePaths:paths.length,verifiedGenuineCaptures:media.length,originalTrackedFilesUnchanged:true};
await mkdir('verification',{recursive:true});await writeFile('verification/source-verification.json',JSON.stringify(result,null,2));
console.log(result);
