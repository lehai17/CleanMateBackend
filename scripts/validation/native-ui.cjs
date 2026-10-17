const fs = require('node:fs'), path = require('node:path'), cp = require('node:child_process');
const root = path.resolve(__dirname, '../../CleanMate.Tests/obj/native-validation');
const state = JSON.parse(fs.readFileSync(path.join(root, 'emulator-state.json'), 'utf8').replace(/^\uFEFF/, ''));
const sdk = process.env.ANDROID_HOME || path.join(process.env.LOCALAPPDATA, 'Android', 'Sdk');
const executable = path.join(sdk, 'platform-tools', 'adb.exe');
function adb(...args) { return cp.execFileSync(executable, ['-s', state.serial, ...args], { maxBuffer: 12 * 1024 * 1024, timeout: 120000 }); }
const identity = adb('emu', 'avd', 'name').toString().split(/[\r\n]+/)[0].trim();
if (!/^emulator-\d+$/.test(state.serial) || identity !== state.avd || !/^CleanMateTest_[a-f0-9]{10}$/.test(identity)) throw new Error('Refusing a device outside our recorded disposable AVD');
function xml() {
  adb('shell', 'rm', '-f', '/sdcard/cleanmate-window.xml');
  const output = adb('shell', 'uiautomator', 'dump', '/sdcard/cleanmate-window.xml').toString('utf8');
  if (!output.includes('UI hierchary dumped to:') && !output.includes('UI hierarchy dumped to:')) throw new Error('Accessibility hierarchy is not ready');
  return adb('exec-out', 'cat', '/sdcard/cleanmate-window.xml').toString('utf8');
}
function nodes() {
  const source = xml(); fs.writeFileSync(path.join(root, 'current-ui.xml'), source);
  return [...source.matchAll(/<node\s+([^>]+)>/g)].map(match => Object.fromEntries([...match[1].matchAll(/([\w-]+)="([^"]*)"/g)].map(item => [item[1], item[2].replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>')])));
}
function tap(node) {
  const bounds = node?.bounds?.match(/\[(\d+),(\d+)\]\[(\d+),(\d+)\]/);
  if (!bounds || +bounds[3] <= +bounds[1] || +bounds[4] <= +bounds[2]) throw new Error('No visible target');
  const x = String(Math.floor((+bounds[1] + +bounds[3]) / 2)), y = String(Math.floor((+bounds[2] + +bounds[4]) / 2));
  adb('shell', 'input', 'swipe', x, y, x, y, '150');
}
function screenshot(name) { fs.writeFileSync(path.join(root, name + '.png'), adb('exec-out', 'screencap', '-p')); }
module.exports = { adb, nodes, tap, screenshot, state, root, verifyHost };
async function verifyHost() {
  const marker = fs.readFileSync(path.join(root, "host-marker.txt"), "utf8").trim();
  if (!/^CleanMate native test [a-f0-9-]{36}$/.test(marker)) throw new Error("Invalid isolated test-host marker");
  const response = await fetch("http://127.0.0.1:13000", { signal: AbortSignal.timeout(15000) });
  if (!response.ok || !(await response.text()).includes(marker)) throw new Error("Refusing an unrecognized test host");
}
