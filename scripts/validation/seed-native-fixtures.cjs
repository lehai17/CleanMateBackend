const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const root = path.resolve(__dirname, '../../CleanMate.Tests/obj/native-validation'), api = 'http://127.0.0.1:15238';
const password = 'CleanMateNative12345';
async function call(route, data, token, method = 'POST') {
  const response = await fetch(api + route, { method, signal: AbortSignal.timeout(15000), headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (!response.ok) throw new Error(`${method} ${route}: ${response.status}`);
  return response.json();
}
(async () => {
  const marker = fs.readFileSync(path.join(root, 'host-marker.txt'), 'utf8');
  if (!/^CleanMate native test [a-f0-9-]{36}$/.test(marker.trim())) throw new Error('Invalid isolated test-host marker');
  const site = await fetch('http://127.0.0.1:13000', { signal: AbortSignal.timeout(15000) }).then(r => r.text());
  if (!site.includes(marker)) throw new Error('This is not our isolated native test host');
  if(fs.existsSync(path.join(root, 'fixtures.json'))) throw new Error('Fixtures already exist; use a fresh isolated test-host run.');
  const customerEmail = `native-customer-${randomUUID()}@example.test`, cleanerEmail = `native-cleaner-${randomUUID()}@example.test`;
  const customer = await call('/api/auth/register', { email: customerEmail, password, fullName: 'Đăng nhập', phoneNumber: '0901234567', role: 'Customer' });
  const cleaner = await call('/api/auth/register', { email: cleanerEmail, password, fullName: 'Native Cleaner', phoneNumber: '0901234568', role: 'Cleaner' });
  const admin = await call('/api/auth/login', { email: 'admin@system.test', password: 'CleanMate-admin-test-123!' });
  await call(`/api/management/applications/${cleaner.user.cleanerProfile.id}/review`, { status: 'Approved' }, admin.token, 'PUT');
  const booking = await call('/api/bookings', { serviceKey: 'cleaning', startTime: new Date(Date.now() + 2 * 86400000).toISOString(), durationHours: 2, address: 'Native validation address', paymentMethod: 'cash', notes: 'Native UI validation' }, customer.token);
  fs.writeFileSync(path.join(root, 'fixtures.json'), JSON.stringify({ customer: { email: customerEmail, id: customer.user.id }, cleaner: { email: cleanerEmail, id: cleaner.user.id }, password, bookingId: booking.id }));
  console.log(`Seeded isolated fixtures: customer ${customer.user.id}, approved cleaner ${cleaner.user.id}, cash booking ${booking.id}`);
})().catch(error => { console.error(error.message); process.exitCode = 1; });
