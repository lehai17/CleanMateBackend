import {test,expect} from '@playwright/test';
import {randomUUID} from 'node:crypto';
const API='http://127.0.0.1:15238',password='CleanMate-admin-test-123!',kind='__PORTAL_KIND__',key='__SESSION_KEY__';
async function account(request){
  const email=`session-${randomUUID()}@example.test`;
  if(kind==='admin'){
    const login=await request.post(`${API}/api/auth/login`,{data:{email:'admin@system.test',password}});expect(login.status()).toBe(200);const headers={Authorization:`Bearer ${(await login.json()).token}`};expect((await request.post(`${API}/api/management/staff`,{headers,data:{email,password,fullName:'Restore Account'}})).status()).toBe(200);const response=await request.post(`${API}/api/auth/login`,{data:{email,password}});expect(response.status()).toBe(200);return response.json();
  }
  const response=await request.post(`${API}/api/auth/register`,{data:{email,password,fullName:'Restore Account',role:'Cleaner'}});expect(response.status()).toBe(200);return response.json();
}
async function prepare(page,token){await page.goto('/');await page.getByLabel('Language / Ngôn ngữ').selectOption('en');await page.getByLabel('Appearance / Giao diện').selectOption('dark');await page.evaluate(({key,token})=>sessionStorage.setItem(key,token),{key,token});}
test('portal keeps a valid token through a connection outage and lets the user retry or choose another account',async({page,request})=>{
  const saved=await account(request);await prepare(page,saved.token);await page.route('**/api/me',route=>route.abort('failed'));await page.reload();await expect(page.getByRole('alert')).toContainText('Unable to connect');expect(await page.evaluate(key=>sessionStorage.getItem(key),key)).toBe(saved.token);
  await page.unroute('**/api/me');await page.getByRole('button',{name:'Retry connection',exact:true}).click();await expect(page.locator('.account strong')).toHaveText('Restore Account');await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
  await page.route('**/api/me',route=>route.abort('failed'));await page.reload();await page.getByRole('button',{name:'Use another account',exact:true}).click();await expect(page.getByRole('heading',{name:'Sign in',exact:true})).toBeVisible();expect(await page.evaluate(key=>sessionStorage.getItem(key),key)).toBeNull();await expect(page.getByLabel('Language / Ngôn ngữ')).toHaveValue('en');
});
test('portal removes an invalid session after a real unauthorized response and keeps display preferences',async({page})=>{
  await prepare(page,'invalid-system-test-token');await page.reload();await expect(page.getByRole('heading',{name:'Sign in',exact:true})).toBeVisible();expect(await page.evaluate(key=>sessionStorage.getItem(key),key)).toBeNull();await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
});
