import sharp from 'sharp';
import {readFile,writeFile,mkdir,copyFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const ids=['dashboard-en','dashboard-tr','inventory-en','warehouses-en','purchase-orders-en','manufacturing-en','production-detail-en','production-detail-tr-mobile','orders-en','products-tr'];
await mkdir('public/media',{recursive:true});
const manifest=[];
for(const id of ids){
 const path='../docs/screenshots/'+id+'.png';
 const data=await readFile(path);
 const meta=await sharp(data).metadata();
 const outputs=[];
 for(const size of [640,960,1440]){
   const name=id+'-'+size+'.webp';
   const result=await sharp(data).resize({width:size,withoutEnlargement:true}).webp({quality:82}).toFile('public/media/'+name);
   outputs.push({name,width:result.width,height:result.height,bytes:result.size});
 }
 manifest.push({id,source:path.replace('../',''),revision:'fbada64d67859a526529368c440b80fc6a3dc32a',sha256:createHash('sha256').update(data).digest('hex'),width:meta.width,height:meta.height,outputs});
}
await writeFile('src/media.json',JSON.stringify(manifest,null,2)+'\n');
console.log('Optimized genuine captures:',manifest.length);
