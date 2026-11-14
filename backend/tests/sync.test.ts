import {after, before, test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {PGlite} from '@electric-sql/pglite';
import type {Database,Row} from '../src/database.js';
import {createHandler} from '../src/worker.js';
import {ApiError} from '../src/errors.js';
import {canonical,validateDocument,MAX_BODY,type WorkDocument} from '../src/document.js';

const pg=new PGlite();
function adapt(client:Pick<PGlite,'query'>):Database {
  return {query:async<T extends Row>(sql:string,params:unknown[]=[]) => (await client.query<T>(sql,params)).rows,
    transaction:operation=>pg.transaction(tx=>operation(adapt(tx)))};
}
const handler=createHandler({auth:async request=>{
  const auth=request.headers.get('authorization');if(auth!=='Bearer alice'&&auth!=='Bearer bob')throw new ApiError(401,'invalid_token','Inicia sesión.');return auth.slice(7);
},database:()=>({database:adapt(pg),close:async()=>{}})});
const env={FIREBASE_PROJECT_ID:'test-project'};const ctx={waitUntil:(_:Promise<unknown>)=>{}};
let serial=1;const guid=()=> (serial++).toString(16).padStart(32,'0');
export function fixture(workId=guid()):WorkDocument {
  const conceptId=guid();return {schemaVersion:1,work:{id:workId,title:'Banqueta',type:'Caminos y carreteras',scope:'100 m²',vat:'16',createdAt:'2026-01-01T00:00:00Z'},frozen:true,
    concepts:[{id:conceptId,code:'0001',description:'Construcción de banqueta',unit:'m²',quantity:'100',unitPrice:'25.5'}],
    plan:[{conceptId,date:'2026-01-02',quantity:'100'}],progress:[],expenses:[]};
}
async function request(path:string,method='GET',document?:unknown,revision='0',key=guid(),owner='alice') {
  const headers=new Headers({authorization:'Bearer '+owner});let body:string|undefined;
  if(document!==undefined){body=JSON.stringify(document);headers.set('content-type','application/json');headers.set('if-match','"'+revision+'"');headers.set('idempotency-key',key);}
  return handler(new Request('https://api.test'+path,{method,headers,...(body?{body}:{})}),env,ctx);
}
before(async()=>{await pg.exec(await readFile(new URL('../migrations/001_sync.sql',import.meta.url),'utf8'));});
after(async()=>pg.close());
test('health público; datos privados necesitan sesión; falta configuración falla cerrada',async()=>{
  assert.equal((await handler(new Request('https://api.test/health'),{},ctx)).status,200);
  assert.equal((await handler(new Request('https://api.test/v1/me'),env,ctx)).status,401);
  assert.equal((await handler(new Request('https://api.test/v1/me'),{},ctx)).status,503);
});
test('valida cantidades con enteros exactos, fechas y campos desconocidos',()=>{
  const d=fixture();assert.equal(validateDocument(d,d.work.id),d);
  for(const mutate of [(x:WorkDocument)=>x.plan[0]!.quantity='100.0001',(x:WorkDocument)=>x.plan[0]!.date='2026-02-30',(x:WorkDocument)=>x.concepts[0]!.unitPrice='1e4']){
    const copy=structuredClone(d);mutate(copy);assert.throws(()=>validateDocument(copy,copy.work.id));
  }
  assert.throws(()=>validateDocument({...d,ownerUid:'bob'},d.work.id));
});
test('subir, descargar y reintentar conserva una sola revisión',async()=>{
  const d=fixture();const path='/v1/works/'+d.work.id;const key=guid();
  const one=await request(path,'PUT',d,'0',key);assert.equal(one.status,200);const result=await one.json() as {revision:string};
  assert.deepEqual(await (await request(path,'PUT',d,'0',key)).json(),result);
  const current=await (await request(path)).json() as {revision:string;document:WorkDocument};assert.equal(canonical(current.document),canonical(d));
  assert.equal(current.revision,result.revision);assert.equal((await request(path,'PUT',{...d,work:{...d.work,title:'Otro'}},'0',key)).status,409);
});
test('un usuario no lee ni sobrescribe datos de otro',async()=>{
  const d=fixture();const path='/v1/works/'+d.work.id;await request(path,'PUT',d);
  assert.equal((await request(path,'GET',undefined,'0',guid(),'bob')).status,404);
  const own={...d,work:{...d.work,title:'Obra de Bob'}};
  assert.equal((await request(path,'PUT',own,'0',guid(),'bob')).status,200);
  assert.equal((await (await request(path)).json() as {document:WorkDocument}).document.work.title,'Banqueta');
});
test('dos equipos con la misma revisión: solo uno gana y el otro recibe conflicto',async()=>{
  const d=fixture();const path='/v1/works/'+d.work.id;const first=await (await request(path,'PUT',d)).json() as {revision:string};
  const updates=['Equipo A','Equipo B'].map(title=>request(path,'PUT',{...d,work:{...d.work,title}},first.revision));
  const results=await Promise.all(updates);assert.deepEqual(results.map(r=>r.status).sort(),[200,409]);
});
test('JSONB cambia orden de claves sin romper la línea base; catálogo fijo sigue protegido',async()=>{
  const d=fixture();const path='/v1/works/'+d.work.id;let result=await (await request(path,'PUT',d)).json() as {revision:string};
  d.work.title='Título actualizado';const ok=await request(path,'PUT',d,result.revision);assert.equal(ok.status,200);result=await ok.json() as {revision:string};
  d.concepts[0]!.unitPrice='28';assert.equal((await request(path,'PUT',d,result.revision)).status,409);
});
test('avance no supera 100%, no disminuye y conserva cortes ya sincronizados',async()=>{
  const d=fixture();const path='/v1/works/'+d.work.id;const key=d.concepts[0]!.id;
  d.progress=[{id:guid(),conceptId:key,date:'2026-01-03',quantity:'101',note:''}];assert.equal((await request(path,'PUT',d)).status,400);
  d.progress[0]!.quantity='50';const first=await (await request(path,'PUT',d)).json() as {revision:string};
  d.progress[0]!.quantity='60';assert.equal((await request(path,'PUT',d,first.revision)).status,409);
  d.progress[0]!.quantity='50';d.progress.push({id:guid(),conceptId:key,date:'2026-01-04',quantity:'40',note:''});assert.equal((await request(path,'PUT',d,first.revision)).status,400);
});
test('cursor devuelve cambios ordenados y nunca datos de otra cuenta',async()=>{
  const response=await request('/v1/sync/changes');const feed=await response.json() as {changes:{id:string;revision:string}[];nextCursor:string};
  assert(feed.changes.length>0);assert(feed.changes.every((r,i)=>i===0||BigInt(r.revision)>BigInt(feed.changes[i-1]!.revision)));
  const empty=await (await request('/v1/sync/changes?cursor='+feed.nextCursor)).json() as {changes:unknown[]};assert.equal(empty.changes.length,0);
  assert.equal((await request('/v1/sync/changes?cursor=1e2')).status,400);
});
test('rechaza carga grande, falta de revisión y CORS abierto',async()=>{
  const d=fixture();d.work.scope='x'.repeat(MAX_BODY);assert.equal((await request('/v1/works/'+d.work.id,'PUT',d)).status,413);
  const missing=new Request('https://api.test/v1/works/'+d.work.id,{method:'PUT',headers:{authorization:'Bearer alice','content-type':'application/json'},body:'{}'});
  assert.equal((await handler(missing,env,ctx)).status,428);
  assert.equal((await handler(new Request('https://api.test/health',{headers:{origin:'https://untrusted.test'}}),env,ctx)).status,403);
});
test('cuenta deshabilitada y límite de frecuencia',async()=>{
  await request('/v1/me','GET',undefined,'0',guid(),'bob');await pg.query('UPDATE accounts SET disabled=true WHERE uid=$1',['bob']);
  assert.equal((await request('/v1/me','GET',undefined,'0',guid(),'bob')).status,403);
  await pg.query('UPDATE accounts SET request_count=120,request_window=now() WHERE uid=$1',['alice']);
  const limited=await request('/v1/me');assert.equal(limited.status,429);assert.equal(limited.headers.get('retry-after'),'60');
});
