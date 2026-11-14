// Local integration harness only. Production worker always uses Firebase verification.
import {createServer} from 'node:http';
import {readFile,mkdtemp} from 'node:fs/promises';
import {resolve,join} from 'node:path';
import {tmpdir} from 'node:os';
import {spawn} from 'node:child_process';
import {PGlite} from '@electric-sql/pglite';
import type {Database,Row} from '../src/database.js';
import {createHandler} from '../src/worker.js';
import {ApiError} from '../src/errors.js';
const pg=new PGlite();await pg.exec(await readFile(new URL('../migrations/001_sync.sql',import.meta.url),'utf8'));
function adapt(client:Pick<PGlite,'query'>):Database{return {query:async<T extends Row>(sql:string,params:unknown[]=[]) => (await client.query<T>(sql,params)).rows,transaction:fn=>pg.transaction(tx=>fn(adapt(tx))) };}
const handler=createHandler({auth:async req=>{if(req.headers.get('authorization')!=='Bearer integration-test')throw new ApiError(401,'invalid_token','Sesión inválida.');return 'integration-user';},database:()=>({database:adapt(pg),close:async()=>{}})});
const server=createServer(async(req,res)=>{
  try{
    const chunks:Buffer[]=[];for await(const chunk of req)chunks.push(Buffer.from(chunk));const body=Buffer.concat(chunks);
    const request=new Request('http://127.0.0.1'+req.url,{method:req.method!,headers:req.headers as Record<string,string>,...(body.length?{body}: {})});
    const response=await handler(request,{FIREBASE_PROJECT_ID:'test-project'},{waitUntil:()=>{}});
    res.writeHead(response.status,Object.fromEntries(response.headers));res.end(Buffer.from(await response.arrayBuffer()));
  }catch{res.writeHead(500);res.end();}
});
await new Promise<void>(done=>server.listen(0,'127.0.0.1',done));const address=server.address();if(!address||typeof address==='string')throw new Error('No port');
try{
  const folder=await mkdtemp(join(tmpdir(),'trazal-http-'));const executable=resolve('../build/output/Release/tests/Trazal.Tests.exe');
  const code=await new Promise<number|null>((done,reject)=>{const child=spawn(executable,['--sync-http',folder,'http://127.0.0.1:'+address.port],{stdio:'inherit',windowsHide:true});child.on('exit',done);child.on('error',reject);});
  if(code!==0)process.exitCode=1;
}finally{server.close();await pg.close();}
