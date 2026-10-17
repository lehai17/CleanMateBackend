const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const u = require('./native-ui.cjs'), fixture = require(path.join(u.root, 'fixtures.json'));
const app = 'com.cleanmate.cleaner.validation';
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(predicate) { const until = Date.now() + 90000; do { try { const n = u.nodes(); if (predicate(n)) return n; } catch {} await pause(500); } while (Date.now() < until); throw new Error('Cleaner native UI timeout'); }
function text(n, label) { return n.find(x => [x['content-desc'], x.text].some(value => value?.toLowerCase() === label.toLowerCase()) && x.clickable === 'true') || n.find(x => x.text?.toLowerCase() === label.toLowerCase()); }
function fields(n) { return n.filter(x => x.class === 'android.widget.EditText' && x.package === app); }
function keyboardBack() { if (/mInputShown=true|mIsInputViewShown=true/.test(u.adb('shell','dumpsys','input_method').toString())) u.adb('shell','input','keyevent','KEYCODE_BACK'); }
async function press(label) { for (let i=0;i<5;i++) { const n=u.nodes(), target=text(n,label); if(target && !/\[(\d+),(\d+)\]\[\1,\2\]/.test(target.bounds)) { try {u.tap(target);return;} catch{} } u.adb('shell','input','swipe','620','1280','620','650','500'); } throw new Error('Missing button: '+label); }
async function tab(label) { const n=await waitFor(n=>n.some(x=>x.clickable==='true'&&x['content-desc']?.endsWith(', '+label)));u.tap(n.find(x=>x.clickable==='true'&&x['content-desc']?.endsWith(', '+label))); }
(async()=>{
  await u.verifyHost();
  u.adb('shell','am','force-stop',app);u.adb('shell','pm','clear',app);u.adb('shell','am','start','-n',app+'/.MainActivity');
  let n=await waitFor(n=>fields(n).length===2);
  u.tap(fields(n)[0]);u.adb('shell','input','text',fixture.cleaner.email);keyboardBack();
  n=u.nodes();u.tap(n.find(x=>x['content-desc']==='Language: EN'));
  n=u.nodes();u.tap(n.find(x=>x['content-desc']==='Appearance: Dark'));
  n=u.nodes();assert.equal(fields(n)[0].text,fixture.cleaner.email);
  u.tap(fields(n)[1]);u.adb('shell','input','text',fixture.password);keyboardBack();await press('Sign in');
  await tab('Find work');
  await waitFor(n=>n.some(x=>x.text?.includes('CM'+fixture.bookingId)));
  u.screenshot('cleaner-available-job');await press('Accept job');
  await waitFor(n=>n.some(x=>x.text==='Job accepted.'));await press('OK');
  await tab('Jobs');await waitFor(n=>Boolean(text(n,'Start job')));await press('Start job');
  await waitFor(n=>Boolean(text(n,'Complete job')));u.screenshot('cleaner-in-progress');await press('Complete job');
  await waitFor(n=>Boolean(text(n,'Confirm cash received')));await press('Confirm cash received');
  await waitFor(n=>Boolean(text(n,'Received in full')));await press('Received in full');
  await waitFor(n=>n.some(x=>x.text?.includes('Completed'))&&!text(n,'Confirm cash received'));
  u.screenshot('cleaner-completed-cash');
  const login=await fetch('http://127.0.0.1:15238/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email:fixture.cleaner.email,password:fixture.password})}).then(r=>r.json());
  const jobs=await fetch('http://127.0.0.1:15238/api/cleaner/jobs/mine',{headers:{Authorization:'Bearer '+login.token}}).then(r=>r.json());
  const job=jobs.find(x=>x.id===fixture.bookingId);
  assert.equal(job.status,'Completed');assert.equal(job.paymentStatus,'Paid');assert.equal(job.paymentMethod,'cash');assert.equal(job.price,140000);assert.equal(job.customerConfirmedAt,null);
  u.adb('shell','am','force-stop',app);u.adb('shell','am','start','-n',app+'/.MainActivity');
  n=await waitFor(n=>n.some(x=>x['content-desc']?.endsWith(', Jobs')));
  assert.equal(n.find(x=>x['content-desc']==='Language: EN')?.selected,'true');assert.equal(n.find(x=>x['content-desc']==='Appearance: Dark')?.selected,'true');
  fs.writeFileSync(path.join(u.root,'cleaner-smoke-results.json'),JSON.stringify({nativeLogin:true,languageRetainsEmail:true,englishDark:true,jobLifecycle:true,cashAfterCompletion:true,customerConfirmationUnchanged:true,secureStoreColdRestore:true,checkedAt:new Date().toISOString()}));
  console.log('Cleaner native login, EN/dark, accept/start/complete, cash declaration and cold restore passed.');
})().catch(error=>{console.error(error.stack);process.exitCode=1});
