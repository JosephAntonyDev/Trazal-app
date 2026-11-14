import type { Database } from './database.js';
import { canonical, validateTransition, type WorkDocument } from './document.js';
import { ApiError } from './errors.js';

export class Repository {
  constructor(private db: Database) {}
  private async account<T>(uid:string,operation:(tx:Database)=>Promise<T>):Promise<T> {
    return this.db.transaction(async tx=>{
      await tx.query('INSERT INTO accounts(uid) VALUES($1) ON CONFLICT DO NOTHING',[uid]);
      // Lock also serializes the account cursor: no later change can commit ahead of an earlier cursor.
      const [account]=await tx.query<{disabled:boolean;request_count:number;age:number}>(
        'SELECT disabled,request_count,extract(epoch from now()-request_window)::float8 AS age FROM accounts WHERE uid=$1 FOR UPDATE',[uid]);
      if(!account||account.disabled)throw new ApiError(403,'account_disabled','La cuenta no tiene acceso a sincronización.');
      if(account.age<60&&account.request_count>=120)throw new ApiError(429,'rate_limit','Espera un minuto antes de sincronizar otra vez.');
      await tx.query('UPDATE accounts SET request_count=CASE WHEN now()-request_window >= interval \'60 seconds\' THEN 1 ELSE request_count+1 END, request_window=CASE WHEN now()-request_window >= interval \'60 seconds\' THEN now() ELSE request_window END WHERE uid=$1',[uid]);
      return operation(tx);
    });
  }
  me(uid:string) { return this.account(uid,async()=>({uid})); }
  changes(uid:string,cursor:string) {
    return this.account(uid,async tx=>{
      const rows=await tx.query<{id:string;revision:string}>('SELECT id,revision::text FROM works WHERE owner_uid=$1 AND revision>$2::bigint ORDER BY revision LIMIT 101',[uid,cursor]);
      const changes=rows.slice(0,100);return {changes,nextCursor:changes.at(-1)?.revision??cursor,hasMore:rows.length>100};
    });
  }
  get(uid:string,id:string) {
    return this.account(uid,async tx=>{
      const [row]=await tx.query<{revision:string;document:WorkDocument}>('SELECT revision::text,document FROM works WHERE owner_uid=$1 AND id=$2',[uid,id]);
      if(!row)throw new ApiError(404,'work_not_found','No se encontró esta obra en tu cuenta.');return row;
    });
  }
  async put(uid:string,id:string,expected:string,requestId:string,document:WorkDocument) {
    const bytes=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(id+'\n'+expected+'\n'+canonical(document)));
    const fingerprint=Array.from(new Uint8Array(bytes),x=>x.toString(16).padStart(2,'0')).join('');
    return this.account(uid,async tx=>{
      const [prior]=await tx.query<{fingerprint:string;revision:string}>('SELECT fingerprint,revision::text FROM sync_requests WHERE owner_uid=$1 AND request_id=$2',[uid,requestId]);
      if(prior) {if(prior.fingerprint!==fingerprint)throw new ApiError(409,'idempotency_mismatch','No se puede reutilizar una solicitud con otros datos.');return {revision:prior.revision};}
      const [current]=await tx.query<{revision:string;document:WorkDocument}>('SELECT revision::text,document FROM works WHERE owner_uid=$1 AND id=$2',[uid,id]);
      if((current?.revision??'0')!==expected)throw new ApiError(409,'revision_conflict','La obra cambió en otro equipo. Se conservaron tus cambios.',current?.revision??'0');
      if(current)validateTransition(current.document,document);
      else {
        const [count]=await tx.query<{total:number}>('SELECT count(*)::integer AS total FROM works WHERE owner_uid=$1',[uid]);
        if(count!.total>=100)throw new ApiError(422,'work_quota','El piloto admite hasta 100 obras sincronizadas por cuenta. Tus obras locales se conservan.');
      }
      const [clock]=await tx.query<{revision:string}>('UPDATE accounts SET clock=clock+1 WHERE uid=$1 RETURNING clock::text AS revision',[uid]);
      const revision=clock!.revision;
      await tx.query('INSERT INTO works(owner_uid,id,revision,document) VALUES($1,$2,$3::bigint,$4::jsonb) ON CONFLICT(owner_uid,id) DO UPDATE SET revision=excluded.revision,document=excluded.document,updated_at=now()',[uid,id,revision,JSON.stringify(document)]);
      await tx.query('INSERT INTO sync_requests(owner_uid,request_id,fingerprint,revision) VALUES($1,$2,$3,$4::bigint)',[uid,requestId,fingerprint,revision]);
      return {revision};
    });
  }
}
