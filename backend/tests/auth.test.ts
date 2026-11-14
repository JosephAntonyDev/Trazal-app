import {test} from 'node:test';
import assert from 'node:assert/strict';
import {generateKeyPair,SignJWT} from 'jose';
import {verifyToken} from '../src/auth.js';
const pair=await generateKeyPair('RS256');
const now=Math.floor(Date.now()/1000);
const claims={sub:'firebase-user',iat:now,exp:now+3600,auth_time:now-10,aud:'test-project',iss:'https://securetoken.google.com/test-project'};
const token=(changes:Record<string,unknown>={})=>new SignJWT({...claims,...changes}).setProtectedHeader({alg:'RS256',kid:'test-key'}).sign(pair.privateKey);
test('acepta firma y claims Firebase correctos',async()=>assert.equal(await verifyToken(await token(),'test-project',async()=>pair.publicKey),'firebase-user'));
test('rechaza expiración, proyecto, emisor, sujeto y autenticación futura',async()=>{
  for(const changes of [{exp:now-1},{aud:'other-project'},{iss:'https://other.test'},{sub:''},{iat:now+1000},{auth_time:now+1000}])
    await assert.rejects(verifyToken(await token(changes),'test-project',async()=>pair.publicKey));
});
test('rechaza otra firma y JWT sin firmar',async()=>{
  const other=await generateKeyPair('RS256');await assert.rejects(verifyToken(await token(),'test-project',async()=>other.publicKey));
  await assert.rejects(verifyToken('eyJhbGciOiJub25lIn0.e30.','test-project',async()=>pair.publicKey));
});
