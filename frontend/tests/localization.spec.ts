import { test, expect } from '@playwright/test';

test('auth validation retranslates immediately and Turkish server errors contain no English copy', async ({ page }) => {
  await page.goto('/auth/login');
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await page.getByLabel('E-posta adresi').fill('bad');
  await page.getByRole('button', { name: 'Giriş yap', exact: true }).click();
  await expect(page.getByText('Geçerli bir e-posta adresi girin.', { exact: true })).toBeVisible();
  await expect(page.getByText('Parolanızı girin.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'EN', exact: true }).click();
  await expect(page.getByText('Enter a valid email address.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'TR', exact: true }).click();
  await page.route('**/api/auth/login', route => route.fulfill({ status: 400, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Validation failed', errors: { Email: ['Email is invalid'] } }) }));
  await page.getByLabel('E-posta adresi').fill('tester@example.com'); await page.getByLabel('Parola', { exact: true }).fill('TestPassword123!'); await page.getByRole('button', { name: 'Giriş yap', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText('Gönderilen alanları kontrol edin.');
  await page.getByRole('link', { name: 'Hesap oluştur', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Hesabınızı oluşturun', exact: true })).toBeVisible();
  await expect(page.getByLabel('Parola tekrarı')).toBeVisible();
  if (process.env.CAPTURE_SCREENSHOTS) await page.screenshot({ path: '../.tools/auth-register-tr.png', fullPage: true });
});

test('all business empty/loading/error screens and navigation localize in Turkish on mobile', async ({ page }) => {
  await page.goto('/auth/login');
  await page.evaluate(() => {
    localStorage.setItem('catalog.language', 'tr');
    sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'locale-fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: 'fixture', name: 'Reviewer', email: 'reviewer@example.com', role: 'Admin', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } }));
  });
  await page.setViewportSize({ width: 390, height: 844 });
  for (const [route, heading] of [['categories', 'Kategoriler'], ['customers', 'Müşteriler'], ['orders', 'Siparişler']]) {
    await page.route(`**/api/${route}`, request => request.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
    await page.goto(`/${route}`); await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible(); await expect(page.getByRole('heading', { name: 'Henüz kayıt yok', exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await expect(page.getByRole('navigation').getByRole('link', { name: heading, exact: true })).toHaveAttribute('aria-current', 'page');
    await page.unroute(`**/api/${route}`);
    await page.route(`**/api/${route}`, request => request.fulfill({ status: 500, contentType: 'application/problem+json', body: JSON.stringify({ title: 'An unexpected error occurred', detail: 'Please try again later.' }) }));
    await page.reload(); await expect(page.getByRole('heading', { name: 'Kayıtlar yüklenemedi', exact: true })).toBeVisible(); await expect(page.getByText('Lütfen daha sonra tekrar deneyin.', { exact: true })).toBeVisible();
  }
  if (process.env.CAPTURE_SCREENSHOTS) await page.screenshot({ path: '../.tools/order-error-tr.png', fullPage: true });
});
