import {test} from 'node:test';
import assert from 'node:assert/strict';
import {api,session} from '../src/api.js';
test('an older portal response cannot expire or replace a new login',async()=>{
  const values=new Map(),originalFetch=globalThis.fetch,originalStorage=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:key=>values.get(key)||null,setItem:(key,value)=>values.set(key,value),removeItem:key=>values.delete(key)};
  try{
    session.set('A');let release;globalThis.fetch=async(url,options)=>{assert.equal(options.headers.Authorization,'Bearer A');return await new Promise(resolve=>{release=resolve;});};
    const pending=api('/me');session.set('B');release(new Response(JSON.stringify({message:'Expired'}),{status:401,headers:{'Content-Type':'application/json'}}));await assert.rejects(pending,error=>error.staleSession===true&&error.status===0);assert.equal(session.get(),'B');
    globalThis.fetch=async()=>new Response(JSON.stringify({id:2}),{status:200,headers:{'Content-Type':'application/json'}});assert.deepEqual(await api('/me'),{id:2});
  }finally{globalThis.fetch=originalFetch;if(originalStorage===undefined)delete globalThis.sessionStorage;else globalThis.sessionStorage=originalStorage;}
});
