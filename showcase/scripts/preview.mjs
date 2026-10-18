import http from 'node:http';
import {readFile,stat} from 'node:fs/promises';
import {resolve,extname,sep} from 'node:path';
const root=resolve('dist');
const types={'.html':'text/html; charset=utf-8','.js':'text/javascript','.css':'text/css','.webp':'image/webp','.json':'application/json','.txt':'text/plain','.xml':'application/xml'};
const security={'X-Content-Type-Options':'nosniff','X-Frame-Options':'DENY','Referrer-Policy':'strict-origin-when-cross-origin','Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'"};
http.createServer(async(req,res)=>{
 try{
 const pathname=decodeURIComponent(new URL(req.url,'http://localhost').pathname);
 let file=resolve(root,'.'+pathname);
 if(file!==root&&!file.startsWith(root+sep)){res.writeHead(403,security).end();return;}
 const info=await stat(file).catch(()=>null);
 if(info?.isDirectory()){
  if(!pathname.endsWith('/')){res.writeHead(308,{...security,Location:pathname+'/'}).end();return;}
  file=resolve(file,'index.html');
 }
 let body;let status=200;
 try{body=await readFile(file);}catch{status=404;file=resolve(root,'404.html');body=await readFile(file);}
 res.writeHead(status,{...security,'Content-Type':types[extname(file)]||'application/octet-stream','Cache-Control':pathname.startsWith('/assets/')?'public, max-age=31536000, immutable':'no-cache'});res.end(body);
 }catch{res.writeHead(400,security).end();}
}).listen(3400,'127.0.0.1',()=>console.log('Static showcase preview http://127.0.0.1:3400'));
