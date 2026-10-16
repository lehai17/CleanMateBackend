// Workspace integration tests: exercise the actual native auth providers with
// platform storage/network fakes. Requires the neighboring web checkout's dev deps.
process.env.NODE_ENV='test';
const path=require('node:path'),fs=require('node:fs'),assert=require('node:assert/strict');
const {test}=require('node:test');
const workspace=path.resolve(__dirname,'../../..'),webModules=path.join(workspace,'web/node_modules');
const {JSDOM}=require(path.join(webModules,'jsdom'));
const dom=new JSDOM('<!doctype html><html><body></body></html>',{url:'http://localhost'});
global.window=dom.window;global.document=dom.window.document;
Object.defineProperty(global,'navigator',{value:dom.window.navigator,configurable:true});
global.IS_REACT_ACT_ENVIRONMENT=true;
const React=require(path.join(webModules,'react')),{createRoot}=require(path.join(webModules,'react-dom/client'));
const babel=require(path.join(webModules,'@babel/core'));
function load(file,dependencies){
  const transformed=babel.transformSync(fs.readFileSync(file,'utf8'),{filename:file,babelrc:false,configFile:false,presets:[[path.join(webModules,'@babel/preset-react'),{runtime:'automatic'}]],plugins:[path.join(webModules,'@babel/plugin-transform-modules-commonjs')]}).code;
  const module={exports:{}};
  const requireFake=name=>{if(name==='react')return React;if(name==='react/jsx-runtime')return require(path.join(webModules,'react/jsx-runtime'));if(Object.hasOwn(dependencies,name))return dependencies[name];throw Error('Unexpected native provider dependency: '+name);};
  new Function('require','module','exports',transformed)(requireFake,module,module.exports);return module.exports;
}
async function fixture(repo){
  const tokenKey=repo==='mobile'?'cm_access_token':'cleanmate_cleaner_access_token',userKey=repo==='mobile'?'cm_user':'cleanmate_cleaner_user';
  const user={id:1,fullName:'Account A',role:'Customer'},values=new Map([[tokenKey,'A']]);
  const state={me:async()=>user,delete:async key=>values.delete(key),auth:null,handlers:[]};
  const storage={getItemAsync:async key=>values.get(key)||null,setItemAsync:async(key,value)=>values.set(key,value),deleteItemAsync:key=>state.delete(key)};
  const api={interceptors:{response:{use:(ok,fail)=>{state.handlers.push({ok,fail});return state.handlers.length-1;},eject:()=>{}}}};
  const guard=load(path.join(workspace,repo,'src/auth/session.js'),{});
  const {AuthProvider,useAuth}=load(path.join(workspace,repo,'src/context/AuthContext.js'),{'expo-secure-store':storage,'../api/axios':{api,TOKEN_KEY:tokenKey},'../api/auth':{meApi:()=>state.me()},'../auth/session':guard});
  function Probe(){state.auth=useAuth();return React.createElement('p',null,state.auth.user?.fullName||'Guest');}
  const element=document.createElement('div'),root=createRoot(element);
  await React.act(async()=>root.render(React.createElement(AuthProvider,null,React.createElement(Probe))));
  return {state,values,user,tokenKey,userKey,close:()=>React.act(async()=>root.unmount())};
}
for(const repo of ['mobile','cleaner-mobile']){
  test(`${repo}: offline restore retains credentials and retry validates the user`,async()=>{
    const f=await fixture(repo);try{
      f.state.me=async()=>{throw Error('Offline');};await React.act(async()=>f.state.auth.restore());assert.match(f.state.auth.restoreError,/Không thể kết nối/);assert.equal(f.values.get(f.tokenKey),'A');
      f.state.me=async()=>f.user;await React.act(async()=>f.state.auth.restore());assert.equal(f.state.auth.user.id,1);assert.equal(f.state.auth.restoreError,'');
    }finally{await f.close();}
  });
  test(`${repo}: old screen callbacks and 401 cannot replace a new login`,async()=>{
    const f=await fixture(repo);try{
      const old=f.state.auth;await React.act(async()=>f.state.auth.logout());await React.act(async()=>f.state.auth.login({id:2,fullName:'Account B',role:'Customer'},'B'));
      await React.act(async()=>{await old.logout();await old.refreshUser({...f.user,fullName:'Stale profile'});await old.login(f.user,'A-late');});assert.equal(f.state.auth.user.id,2);assert.equal(f.values.get(f.tokenKey),'B');
      const error={response:{status:401},config:{url:'/api/me',headers:{Authorization:'Bearer A'}}};await React.act(async()=>assert.rejects(f.state.handlers[0].fail(error)));assert.equal(f.state.auth.user.id,2);assert.equal(f.values.get(f.tokenKey),'B');
    }finally{await f.close();}
  });
  test(`${repo}: an in-flight old logout cannot delete newer saved credentials`,async()=>{
    const f=await fixture(repo);try{
      let release;const hold=new Promise(resolve=>{release=resolve;});f.state.delete=async key=>{if(key===f.tokenKey)await hold;f.values.delete(key);};let logout,login;
      await React.act(async()=>{logout=f.state.auth.logout();await Promise.resolve();});
      await React.act(async()=>{login=f.state.auth.login({id:2,fullName:'Account B',role:'Customer'},'B');release();await Promise.all([logout,login]);});
      assert.equal(f.values.get(f.tokenKey),'B');assert.equal(JSON.parse(f.values.get(f.userKey)).id,2);assert.equal(f.state.auth.user.id,2);
    }finally{await f.close();}
  });
  test(`${repo}: an older profile restore cannot overwrite a successful new login`,async()=>{
    const f=await fixture(repo);try{
      let resolve;f.state.me=()=>new Promise(done=>{resolve=done;});let restore;await React.act(async()=>{restore=f.state.auth.restore();await Promise.resolve();});
      await React.act(async()=>f.state.auth.login({id:2,fullName:'Account B',role:'Customer'},'B'));
      await React.act(async()=>{resolve(f.user);await restore;});assert.equal(f.state.auth.user.id,2);assert.equal(f.values.get(f.tokenKey),'B');
    }finally{await f.close();}
  });
  test(`${repo}: a failed persisted logout reports failure and retries clearing instead of signing in`,async()=>{
    const f=await fixture(repo);try{
      f.state.delete=async()=>{throw Error('Secure storage unavailable');};await React.act(async()=>f.state.auth.logout());assert.equal(f.state.auth.user,null);assert.equal(f.state.auth.logoutFailed,true);assert.equal(f.values.get(f.tokenKey),'A');
      f.state.delete=async key=>f.values.delete(key);await React.act(async()=>f.state.auth.restore());assert.equal(f.state.auth.logoutFailed,false);assert.equal(f.state.auth.user,null);assert.equal(f.values.get(f.tokenKey),undefined);
    }finally{await f.close();}
  });
  test(`${repo}: current token 401 clears persisted credentials while bad Google sign-in does not`,async()=>{
    const f=await fixture(repo);try{
      const error={response:{status:401},config:{url:'/api/auth/google',headers:{Authorization:'Bearer A'}}};await React.act(async()=>assert.rejects(f.state.handlers[0].fail(error)));assert.equal(f.state.auth.user.id,1);
      await React.act(async()=>assert.rejects(f.state.handlers[0].fail({...error,config:{...error.config,url:'/api/me'}})));assert.equal(f.state.auth.user,null);assert.equal(f.values.get(f.tokenKey),undefined);
    }finally{await f.close();}
  });
}
