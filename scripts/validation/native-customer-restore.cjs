const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const u = require('./native-ui.cjs');
const app = 'com.cleanmate.customer.validation';
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(predicate) { const until = Date.now() + 60000; do { try { const n = u.nodes(); if (predicate(n)) return n; } catch {} await pause(500); } while (Date.now() < until); throw new Error('Native restore UI timeout'); }
function selected(n, label) { assert.equal(n.find(x => x['content-desc'] === label)?.selected, 'true'); }
(async () => {
  await u.verifyHost();
  u.adb('shell', 'am', 'force-stop', app);
  u.adb('shell', 'am', 'start', '-n', app + '/.MainActivity');
  let n = await waitFor(n => n.some(x => x['content-desc']?.endsWith(', Profile')));
  selected(n, 'Language: EN'); selected(n, 'Appearance: Dark');
  u.tap(n.find(x => x.clickable === 'true' && x['content-desc']?.endsWith(', Profile')));
  n = await waitFor(n => n.some(x => x.text === '\u0110\u0103ng nh\u1eadp'));
  assert.ok(n.some(x => x['content-desc'] === 'Sign out'));
  u.screenshot('customer-restored-session');
  u.tap(n.find(x => x['content-desc'] === 'Appearance: Light'));
  n = u.nodes(); selected(n, 'Appearance: Light');
  u.tap(n.find(x => x['content-desc'] === 'Language: VI'));
  n = u.nodes(); selected(n, 'Language: VI');
  u.tap(n.find(x => x.clickable === 'true' && x['content-desc'] === '\u0110\u0103ng xu\u1ea5t'));
  n = await waitFor(n => n.some(x => x.text === 'B\u1ea1n ch\u01b0a \u0111\u0103ng nh\u1eadp'));
  selected(n, 'Language: VI'); selected(n, 'Appearance: S\u00e1ng');
  u.adb('shell', 'am', 'force-stop', app);
  u.adb('shell', 'am', 'start', '-n', app + '/.MainActivity');
  n = await waitFor(n => n.some(x => x['content-desc']?.endsWith(', H\u1ed3 s\u01a1')));
  selected(n, 'Language: VI'); selected(n, 'Appearance: S\u00e1ng');
  u.tap(n.find(x => x.clickable === 'true' && x['content-desc']?.endsWith(', H\u1ed3 s\u01a1')));
  await waitFor(n => n.some(x => x.text === 'B\u1ea1n ch\u01b0a \u0111\u0103ng nh\u1eadp'));
  u.screenshot('customer-logged-out-restart');
  fs.writeFileSync(path.join(u.root, 'customer-restore-results.json'), JSON.stringify({ secureStoreColdRestore: true, preferenceColdRestore: true, vietnameseLight: true, logoutSurvivesRestart: true, checkedAt: new Date().toISOString() }));
  console.log('Customer native cold restore, VI/light, logout and preference persistence passed.');
})().catch(error => { console.error(error.stack); process.exitCode = 1; });
