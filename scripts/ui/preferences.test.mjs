import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const catalog = await readFile(new URL('../src/ui/catalog.js',import.meta.url),'utf8');
const source = (await readFile(new URL('../src/ui/core.js',import.meta.url),'utf8')).replace(/import \{ english \} from ['"]\.\/catalog(?:\.js)?['"];?/,catalog);
const {parsePreferences,resolveTheme,translate,palettes}=await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
test('invalid or corrupt persisted preferences safely fall back to Vietnamese and system appearance',()=>{
  for(const value of [null,undefined,'broken','[]','42','{"language":"fr","theme":"contrast"}'])assert.deepEqual(parsePreferences(value),{language:'vi',theme:'system'});
  assert.deepEqual(parsePreferences('{"language":"en","theme":"dark","token":"discard"}'),{language:'en',theme:'dark'});
});
test('theme tracks the operating system only when system is selected',()=>{
  assert.equal(resolveTheme('system',true),'dark'); assert.equal(resolveTheme('system',false),'light');
  assert.equal(resolveTheme('light',true),'light'); assert.equal(resolveTheme('dark',false),'dark');
  assert.notEqual(palettes.light.text,palettes.dark.text);
});
test('translation is whole-phrase only and never rewrites unknown content or business identifiers',()=>{
  assert.equal(translate('  Đăng nhập\n','en'),'  Sign in\n');
  assert.equal(translate('Đăng nhập','vi'),'Đăng nhập');
  for(const value of ['CM123','HouseCleaning','constructor','toString','__proto__','Khách tên Đăng nhập','customer@example.test',123,null])assert.equal(translate(value,'en'),value);
  assert.equal(translate('3 thông báo chưa đọc','en'),'3 unread notifications');
});
test('both palettes keep primary and secondary text readable on their surfaces',()=>{
  const luminance=hex=>{const values=hex.slice(1).match(/../g).map(v=>parseInt(v,16)/255).map(v=>v<=0.04045?v/12.92:((v+0.055)/1.055)**2.4);return values[0]*0.2126+values[1]*0.7152+values[2]*0.0722;};
  for(const palette of Object.values(palettes))for(const foreground of ['text','muted'])for(const background of ['background','surface']){
    const values=[luminance(palette[foreground]),luminance(palette[background])].sort((a,b)=>b-a);assert.ok((values[0]+0.05)/(values[1]+0.05)>=4.5,`${foreground}/${background} contrast must reach 4.5:1`);
  }
});
