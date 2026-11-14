import { ApiError, invalid } from './errors.js';

export interface WorkDocument {
  schemaVersion: 1;
  work: { id: string; title: string; type: string; scope: string; vat: string | null; createdAt: string };
  frozen: boolean;
  concepts: { id: string; code: string; description: string; unit: string; quantity: string; unitPrice: string }[];
  plan: { conceptId: string; date: string; quantity: string }[];
  progress: { id: string; conceptId: string; date: string; quantity: string; note: string }[];
  expenses: { id: string; date: string; category: string; description: string; amount: string; vat: string }[];
}
export const MAX_BODY = 512 * 1024;
const types = ['Caminos y carreteras','Agua potable y drenaje','Escuelas','Electrificación','Edificación','Otra obra civil'];
const categories = ['Materiales','Mano de obra','Maquinaria','Subcontratos','Servicios','Gastos de oficina','Gastos de oficina de campo','Depreciación, mantenimiento y rentas','Indirectos','Otros gastos'];
export function id(value: unknown): string { if(typeof value !== 'string' || value.length!==32 || !/^[0-9a-f]{32}$/.test(value)) invalid('Identificador inválido.'); return value as string; }
function object(v: unknown, keys: string[]): Record<string, unknown> {
  if(!v || typeof v !== 'object' || Array.isArray(v)) invalid('Se esperaba un objeto.');
  const o=v as Record<string,unknown>;
  if(Object.keys(o).length !== keys.length || keys.some(k=>!Object.hasOwn(o,k))) invalid('Campos desconocidos o incompletos.');
  return o;
}
function str(v: unknown, max: number, required=false): string { if(typeof v!=='string' || v.length>max || (required&&!v.trim()) || /[\u0000-\u0008\u000b\u000c\u000e-\u001f]/.test(v)) invalid('Texto inválido.'); return v as string; }
function list(v: unknown, max: number): unknown[] { if(!Array.isArray(v)||v.length>max) invalid('Demasiados registros o lista inválida.');return v as unknown[]; }
function date(v: unknown): string {
  const s=str(v,10,true);if(!/^\d{4}-\d{2}-\d{2}$/.test(s) || s<'1900-01-01' || s>'2100-12-31' || !Number.isFinite(Date.parse(s)) || new Date(s).toISOString().slice(0,10)!==s) invalid('Fecha inválida.');return s;
}
// Scaled integers, never floating point comparisons for contracted quantities or monetary limits.
function decimal(v: unknown, scale: number, max: bigint, positive=false): bigint {
  const s=str(v,40,true);const re=new RegExp('^(0|[1-9][0-9]*)(\\.[0-9]{1,'+scale+'})?$');
  if(!re.test(s)) invalid('Usa un decimal no negativo en texto, sin exponentes ni separadores.');
  const [a,b='']=s.split('.');const n=BigInt(a!)*10n**BigInt(scale)+BigInt(b.padEnd(scale,'0'));
  if(n>max*10n**BigInt(scale)||(positive&&n===0n)) invalid('Importe o cantidad fuera de rango.');return n;
}
function unique(set: Set<string>, key: string) { if(set.has(key)) invalid('Registro duplicado.');set.add(key); }
export function validateDocument(input: unknown, workId: string): WorkDocument {
  const d=object(input,['schemaVersion','work','frozen','concepts','plan','progress','expenses']);
  if(d.schemaVersion!==1 || typeof d.frozen!=='boolean') invalid('Versión de documento incompatible.');
  const w=object(d.work,['id','title','type','scope','vat','createdAt']);if(id(w.id)!==workId) invalid('La obra no coincide con la ruta.');
  str(w.title,180,true);if(!types.includes(str(w.type,80)))invalid('Tipo de obra desconocido.');str(w.scope,240);
  if(w.vat!==null)decimal(w.vat,10,100n);
  const created=str(w.createdAt,40,true);if(!/^\d{4}-\d\d-\d\dT.*(?:Z|\+00:00)$/.test(created)||!Number.isFinite(Date.parse(created)))invalid('Fecha de creación inválida.');
  const quantities=new Map<string,bigint>();const codes=new Set<string>();
  for(const item of list(d.concepts,2000)) {
    const c=object(item,['id','code','description','unit','quantity','unitPrice']);const key=id(c.id);
    if(quantities.has(key))invalid('Concepto duplicado.');unique(codes,str(c.code,40,true).trim().toUpperCase());str(c.description,500,true);str(c.unit,30,true);
    quantities.set(key,decimal(c.quantity,4,100000000n,true)*1000000n);decimal(c.unitPrice,4,100000000n);
  }
  const planned=new Map<string,bigint>();const dates=new Set<string>();
  for(const item of list(d.plan,10000)) {
    const p=object(item,['conceptId','date','quantity']);const key=id(p.conceptId);if(!quantities.has(key))invalid('Programa sin concepto.');
    unique(dates,key+date(p.date));const sum=(planned.get(key)??0n)+decimal(p.quantity,4,100000000n,true)*1000000n;
    if(sum>quantities.get(key)!)invalid('El programa excede la cantidad contratada.');planned.set(key,sum);
  }
  if(d.frozen) {
    if(!quantities.size)invalid('No se puede fijar una obra vacía.');
    for(const [key,quantity] of quantities)if(planned.get(key)!==quantity)invalid('Línea base incompleta.');
  }
  const ids=new Set<string>();const progressDates=new Set<string>();const series=new Map<string,{date:string;quantity:bigint}[]>();
  const today=new Date().toISOString().slice(0,10);
  for(const item of list(d.progress,5000)) {
    const p=object(item,['id','conceptId','date','quantity','note']);unique(ids,id(p.id));const key=id(p.conceptId);
    if(!d.frozen || !quantities.has(key))invalid('Avance sin línea base o concepto.');
    const day=date(p.date);if(day>today)invalid('Avance futuro.');unique(progressDates,key+day);str(p.note,500);
    const quantity=decimal(p.quantity,10,100000000n,true);if(quantity>quantities.get(key)!)invalid('El avance supera el 100 %.');
    const group=series.get(key)??[];group.push({date:day,quantity});series.set(key,group);
  }
  for(const group of series.values()) {group.sort((a,b)=>a.date.localeCompare(b.date));let last=0n;for(const p of group){if(p.quantity<last)invalid('El avance acumulado disminuye.');last=p.quantity;}}
  ids.clear();
  for(const item of list(d.expenses,5000)) {
    const e=object(item,['id','date','category','description','amount','vat']);unique(ids,id(e.id));date(e.date);if(!categories.includes(str(e.category,80)))invalid('Categoría inválida.');str(e.description,500,true);decimal(e.amount,2,1000000000000n,true);decimal(e.vat,2,100n);
  }
  return input as WorkDocument;
}
export function canonical(value: unknown): string {
  if(Array.isArray(value)) return '['+value.map(canonical).join(',')+']';
  if(value!==null && typeof value==='object') return '{'+Object.entries(value).sort(([a],[b])=>a.localeCompare(b)).map(([k,v])=>JSON.stringify(k)+':'+canonical(v)).join(',')+'}';
  return JSON.stringify(value);
}
function keyed<T>(items:T[],key:(item:T)=>string) { return new Map(items.map(item=>[key(item),canonical(item)])); }
function unchanged<T>(old:T[],next:T[],key:(item:T)=>string,exact:boolean) {
  const right=keyed(next,key);if(exact&&old.length!==next.length)return false;
  return [...keyed(old,key)].every(([k,v])=>right.get(k)===v);
}
export function validateTransition(old: WorkDocument, next: WorkDocument) {
  if(old.work.createdAt!==next.work.createdAt) throw new ApiError(409,'immutable_creation','La fecha de creación no se modifica.');
  if(old.frozen && (!next.frozen || !unchanged(old.concepts,next.concepts,c=>c.id,true) || !unchanged(old.plan,next.plan,p=>p.conceptId+p.date,true)))
    throw new ApiError(409,'immutable_baseline','La línea base fijada no puede reemplazarse.');
  if(!unchanged(old.progress,next.progress,p=>p.id,false))throw new ApiError(409,'immutable_progress','Los cortes existentes requieren un flujo de corrección con historial.');
  const expenses=new Set(next.expenses.map(e=>e.id));if(old.expenses.some(e=>!expenses.has(e.id)))throw new ApiError(409,'expense_deletion','La eliminación de gastos no está habilitada.');
}
