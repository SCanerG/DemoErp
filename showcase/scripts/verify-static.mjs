import {readdir,readFile,stat,writeFile,mkdir} from 'node:fs/promises';
import {strict as assert} from 'node:assert';
async function walk(path){return (await Promise.all((await readdir(path,{withFileTypes:true})).map(e=>e.isDirectory()?walk(path+'/'+e.name):path+'/'+e.name))).flat();}
const files=await walk('dist');
assert(files.length<20000);
let total=0;let largest=0;
for(const file of files){const size=(await stat(file)).size;assert(size<25*1024*1024,file+' exceeds Pages limit');total+=size;largest=Math.max(largest,size);}
for(const lang of ['tr','en']){
 const html=await readFile('dist/'+lang+'/index.html','utf8');
 assert(html.includes('lang="'+lang+'"'));
 assert(html.includes('https://demo.scgenco.com/'+lang+'/'));
 assert(!/noindex/.test(html));
 for(const [,path] of html.matchAll(/(?:src|href)="(\/[^"#]+)"/g)){
  if(path.startsWith('/assets/')||path.startsWith('/media/'))await stat('dist'+path);
 }
}
const media=JSON.parse(await readFile('src/media.json','utf8'));
for(const item of media)for(const output of item.outputs)await stat('dist/media/'+output.name);
const pkg=JSON.parse(await readFile('package.json','utf8'));
assert.deepEqual(Object.keys(pkg.dependencies).sort(),['react','react-dom']);
const js=(await Promise.all(files.filter(f=>f.endsWith('.js')).map(f=>readFile(f,'utf8')))).join('\n');
for(const pattern of [/sk-[A-Za-z0-9]{24,}/,/ghp_[A-Za-z0-9]{20,}/,/-----BEGIN .*PRIVATE KEY-----/,/development-only-change-me/,/ConnectionStrings__Default/,/fetch\s*\(/,/XMLHttpRequest/,/WebSocket\s*\(/,/localhost:5080\/api/])assert(!pattern.test(js),'Bundle scan: '+pattern);
assert(!files.some(f=>/\.env|_worker\.js|\.map$/.test(f)));
const headers=await readFile('dist/_headers','utf8');
assert(headers.includes("connect-src 'none'"));
const summary={files:files.length,totalBytes:total,largestAssetBytes:largest,captures:media.length,backendDependencies:0};
await mkdir('verification',{recursive:true});
await writeFile('verification/static-summary.json',JSON.stringify(summary,null,2));
console.log('Static bundle verification PASS',summary);
