import {readFile} from 'node:fs/promises';
import {connect} from '../src/database.js';
const url=process.env.DATABASE_URL;
if(!url)throw new Error('Define DATABASE_URL en el entorno; no escribas credenciales en el repositorio.');
const connection=connect(url);
try {
  const sql=await readFile(new URL('../migrations/001_sync.sql',import.meta.url),'utf8');
  await connection.database.transaction(async tx=>{
    await tx.query('SELECT pg_advisory_xact_lock(7472617)');
    await tx.query('CREATE TABLE IF NOT EXISTS schema_migrations(version integer PRIMARY KEY,applied_at timestamptz NOT NULL DEFAULT now())');
    const applied=await tx.query('SELECT version FROM schema_migrations WHERE version=1');
    if(!applied.length)await tx.query(sql);
  });
  console.log('Migración 001 aplicada o ya presente.');
}finally{await connection.close();}
