import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const source=await readFile(new URL('../src/auth/session.js',import.meta.url),'utf8');
const {createSessionGuard,currentSessionExpired,requestToken,createSerialQueue}=await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
test('new logins and restores invalidate older work even for the same token',()=>{
  const guard=createSessionGuard('A'),old=guard.snapshot();guard.replace('A');assert.equal(guard.isCurrent(old),false);const current=guard.replace('B');assert.equal(guard.isCurrent(current),true);guard.replace(null);assert.equal(guard.isCurrent(current),false);
});
test('only 401 for the current request token revokes the current session',()=>{
  const error={response:{status:401},config:{url:'/api/me',headers:{Authorization:'Bearer A'}}};assert.equal(currentSessionExpired(error,'A'),true);assert.equal(currentSessionExpired(error,'B'),false);assert.equal(currentSessionExpired(error,null),false);
  assert.equal(currentSessionExpired({...error,config:{...error.config,url:'/api/auth/login/?next=home'}},'A'),false);assert.equal(currentSessionExpired({...error,response:{status:403}},'A'),false);assert.equal(requestToken({headers:{get:()=> 'Bearer A'}}),'A');
  assert.equal(currentSessionExpired({...error,config:{...error.config,url:'/api/auth/google'}},'A'),false);
});
test('serialized native writes prevent an older logout from clearing a newer login',async()=>{
  const queue=createSerialQueue(),guard=createSessionGuard('A'),events=[];let release;const hold=new Promise(resolve=>{release=resolve;});let stored='A';
  const first=queue(async()=>{await hold;events.push('old-write');stored='A';});const logout=guard.replace(null);const clear=queue(async()=>{if(guard.isCurrent(logout)){events.push('clear');stored=null;}});const login=guard.replace('B');const save=queue(async()=>{if(guard.isCurrent(login)){events.push('new-write');stored='B';}});release();await Promise.all([first,clear,save]);assert.equal(stored,'B');assert.deepEqual(events,['old-write','new-write']);
});
test('a failed storage operation does not block the next serialized operation',async()=>{
  const queue=createSerialQueue();const first=queue(async()=>{throw new Error('Storage unavailable');});const second=queue(async()=>42);await assert.rejects(first,/Storage unavailable/);assert.equal(await second,42);
});
