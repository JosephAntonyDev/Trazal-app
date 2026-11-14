import postgres from 'postgres';
export type Row = Record<string,unknown>;
export interface Database {
  query<T extends Row = Row>(text: string,parameters?: unknown[]): Promise<T[]>;
  transaction<T>(operation: (tx:Database)=>Promise<T>): Promise<T>;
}
export function connect(connectionString: string) {
  const sql=postgres(connectionString,{max:1,fetch_types:false,prepare:true,connect_timeout:5,idle_timeout:5});
  const adapt=(client: typeof sql | postgres.TransactionSql):Database=>({
    query: async <T extends Row>(text:string,parameters:unknown[]=[])=>Array.from(await client.unsafe(text,parameters as postgres.ParameterOrJSON<never>[],{prepare:parameters.length>0})) as unknown as T[],
    transaction: async <T>(operation:(tx:Database)=>Promise<T>)=> await sql.begin(tx=>operation(adapt(tx))) as T
  });
  return {database:adapt(sql),close:()=>sql.end({timeout:2})};
}
