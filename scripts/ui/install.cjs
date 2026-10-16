// Maintainer tool: synchronize the authored catalog and install UI adapters.
// Source adapters are versioned independently in each client repository.
const fs=require('fs'),path=require('path');
const root=path.resolve(__dirname,'../../..');
const catalog=Object.fromEntries(fs.readFileSync(path.join(__dirname,'catalog.txt'),'utf8').split(/\r?\n/).filter(Boolean).map(line=>{const index=line.indexOf('|');if(index<1)throw Error(line);return [line.slice(0,index),line.slice(index+1)];}));
const clients=['web','mobile','admin','cleaner-web','cleaner-mobile'];
for(const client of clients){
  const folder=path.join(root,client,'src/ui'); fs.mkdirSync(folder,{recursive:true});
  fs.writeFileSync(path.join(folder,'catalog.js'),`// Authored interface translations. Keep user content outside translation adapters.\nexport const english = ${JSON.stringify(catalog,null,2)};\n`);
  let core=fs.readFileSync(path.join(__dirname,'core.js'),'utf8');
  const portal=client==='admin'||client==='cleaner-web',native=client.includes('mobile');
  if(portal)core=core.replace("'./catalog'","'./catalog.js'");
  fs.writeFileSync(path.join(folder,'core.js'),core);
  if(portal){
    fs.writeFileSync(path.join(folder,'preferences.js'),fs.readFileSync(path.join(__dirname,'preferences.portal.js')));
    fs.writeFileSync(path.join(folder,'preferences.css'),fs.readFileSync(path.join(__dirname,'preferences.css')));
  }else fs.writeFileSync(path.join(folder,'Preferences.jsx'),fs.readFileSync(path.join(__dirname,native?'Preferences.native.jsx':'Preferences.web.jsx')));
  if(!portal&&!native) fs.writeFileSync(path.join(folder,'preferences.css'),fs.readFileSync(path.join(__dirname,'preferences.css')));
}
console.log(`Synchronized UI adapters and ${Object.keys(catalog).length} authored translations for five clients.`);
