const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const u = require('./native-ui.cjs'), fixture = require(path.join(u.root, 'fixtures.json'));
const app = 'com.cleanmate.customer.validation';
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(predicate) { const until = Date.now() + 30000; do { try { const n = u.nodes(); if (predicate(n)) return n; } catch {} await pause(300); } while (Date.now() < until); throw new Error('Native UI did not reach the expected state'); }
function keyboardBack() { const state = u.adb('shell', 'dumpsys', 'input_method').toString(); if (/mInputShown=true|mIsInputViewShown=true/.test(state)) u.adb('shell', 'input', 'keyevent', 'KEYCODE_BACK'); }
function fieldNodes(n) { return n.filter(x => x.class === 'android.widget.EditText' && x.package === app); }
(async () => {
  await u.verifyHost();
  u.adb('shell', 'am', 'force-stop', app);
  u.adb('shell', 'pm', 'clear', app);
  u.adb('shell', 'am', 'start', '-n', app + '/.MainActivity');
  let n = await waitFor(n => n.some(x => x.text === 'Hồ sơ'));
  u.tap(n.find(x => x.clickable === 'true' && x['content-desc']?.endsWith(', Hồ sơ')));
  n = await waitFor(n => n.some(x => x['content-desc'] === 'Đăng nhập' && x.clickable === 'true'));
  u.tap(n.find(x => x['content-desc'] === 'Đăng nhập' && x.clickable === 'true'));
  n = await waitFor(n => fieldNodes(n).length >= 2);
  u.tap(fieldNodes(n)[0]); u.adb('shell', 'input', 'text', fixture.customer.email); keyboardBack();
  n = u.nodes(); u.tap(n.find(x => x['content-desc'] === 'Language: EN'));
  n = u.nodes(); assert.equal(n.find(x => x['content-desc'] === 'Language: EN').selected, 'true');
  u.tap(n.find(x => x['content-desc'] === 'Appearance: Dark'));
  n = u.nodes(); assert.equal(n.find(x => x['content-desc'] === 'Appearance: Dark').selected, 'true');
  assert.equal(fieldNodes(n)[0].text, fixture.customer.email);
  u.screenshot('customer-login-english-dark');
  u.tap(fieldNodes(n)[1]); u.adb('shell', 'input', 'text', fixture.password);
  n = u.nodes(); u.screenshot('customer-login-keyboard');
  const keyboardSubmit = n.find(x => x['content-desc'] === 'Sign in' && x.clickable === 'true');
  keyboardBack(); n = u.nodes();
  u.tap(n.find(x => x['content-desc'] === 'Sign in' && x.clickable === 'true') || n.filter(x => x.text === 'Sign in').at(-1));
  n = await waitFor(n => n.some(x => x.text === 'Profile') && !n.some(x => x['content-desc'] === 'Sign in' && x.clickable === 'true'));
  if (!n.some(x => x.text === 'Đăng nhập')) u.tap(n.find(x => x.clickable === 'true' && x['content-desc']?.endsWith(', Profile')));
  n = await waitFor(n => n.some(x => x.text === 'Đăng nhập'));
  assert.ok(!n.some(x => x.text === 'Sign in'), 'A private name must not become a translated login label');
  u.screenshot('customer-profile-english-dark');
  fs.writeFileSync(path.join(u.root, 'customer-smoke-results.json'), JSON.stringify({ languagePreservesEmail: true, darkAppearance: true, nativeLogin: true, privateNamePreserved: true, submitPresentWithKeyboard: Boolean(keyboardSubmit), checkedAt: new Date().toISOString() }));
  console.log('Customer native smoke passed: language/theme, retained form value, real API login and private profile name.');
})().catch(error => { console.error(error.stack); process.exitCode = 1; });
