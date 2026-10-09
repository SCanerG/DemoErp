import { test, expect } from '@playwright/test';

// These tests isolate session/storage and catalog UI; analytics requests are intentionally unavailable.
test.beforeEach(async ({ page }) => {
  await page.route('**/api/dashboard/**', route => route.abort());
  await page.route('**/api/reports/**', route => route.abort());
});

test('catalog handles loading, empty results, errors, and retry', async ({ page }) => {
  await page.goto('/auth/login');
  await page.evaluate(() => sessionStorage.setItem('catalog.session', JSON.stringify({ accessToken: 'ui-state-fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: 'state-fixture', name: 'UI fixture', email: 'fixture@example.com', role: 'Admin', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null } })));
  let release: () => void = () => {};
  const ready = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/products', async route => {
    await ready;
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });
  await page.goto('/products');
  await expect(page.getByRole('status')).toContainText('Loading your catalog');
  release();
  await expect(page.getByRole('heading', { name: 'Your catalog is a clean slate' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Create your first product' })).toBeVisible();
  await page.unroute('**/api/products');
  await page.route('**/api/products', route => route.fulfill({ status: 500, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Temporary server error', status: 500 }) }));
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Could not load products' })).toBeVisible();
  await page.unroute('**/api/products');
  await page.route('**/api/products', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.getByRole('button', { name: 'Try again' }).click();
  await expect(page.getByRole('heading', { name: 'Your catalog is a clean slate' })).toBeVisible();
});

test('malformed stored session redirects safely and malformed API errors remain readable', async ({ page }) => {
  await page.goto('/auth/login');
  await page.evaluate(() => sessionStorage.setItem('catalog.session', JSON.stringify({
    accessToken: 'fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: 'fixture', name: 42 },
  })));
  await page.goto('/products');
  await expect(page).toHaveURL(/\/auth\/login/);
  await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible();
  await page.evaluate(() => sessionStorage.setItem('catalog.session', JSON.stringify({
    accessToken: 'fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(),
    user: { id: 'fixture', name: 'Tester', email: 'tester@example.com', role: 'Admin', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null },
  })));
  await page.route('**/api/products', route => route.fulfill({ status: 500, contentType: 'application/json', body: 'null' }));
  await page.goto('/products');
  await expect(page.getByRole('heading', { name: 'Could not load products' })).toBeVisible();
  await expect(page.getByText('Unable to complete the request.', { exact: true })).toBeVisible();
});

test('unavailable session storage still permits login and logout in memory', async ({ page }) => {
  await page.addInitScript(() => {
    Storage.prototype.setItem = () => { throw new DOMException('Storage unavailable', 'SecurityError'); };
    Storage.prototype.removeItem = () => { throw new DOMException('Storage unavailable', 'SecurityError'); };
  });
  await page.route('**/api/auth/login', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
    accessToken: 'fixture', expiresAt: new Date(Date.now() + 3600000).toISOString(),
    user: { id: 'fixture', name: 'Tester', email: 'tester@example.com', role: 'Admin', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null },
  }) }));
  await page.route('**/api/products', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.goto('/auth/login');
  await page.getByLabel('Email address').fill('tester@example.com');
  await page.getByLabel('Password', { exact: true }).fill('TestPassword123!');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/auth\/login/);
});

test('late unauthorized response from a previous session does not clear a newer login', async ({ page }) => {
  await page.addInitScript(() => {
    const originalFetch = window.fetch;
    let held = false;
    window.fetch = (input, options) => {
      if (!held && String(input).endsWith('/api/products')) {
        held = true;
        return new Promise<Response>(resolve => {
          // Deliberately model a response already in flight when cancellation/logout occurs.
          (window as unknown as { releaseOldResponse: () => void }).releaseOldResponse = () =>
            resolve(new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401 }));
        });
      }
      return originalFetch(input, options);
    };
  });
  await page.goto('/auth/login');
  await page.evaluate(() => sessionStorage.setItem('catalog.session', JSON.stringify({
    accessToken: 'old-session', expiresAt: new Date(Date.now() + 3600000).toISOString(),
    user: { id: 'fixture', name: 'Tester', email: 'tester@example.com', role: 'Admin', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null },
  })));
  await page.goto('/products');
  await expect(page.getByRole('status')).toContainText('Loading your catalog');
  await page.getByRole('button', { name: 'Sign out' }).click();
  await page.route('**/api/auth/login', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
    accessToken: 'new-session', expiresAt: new Date(Date.now() + 3600000).toISOString(),
    user: { id: 'fixture', name: 'Tester', email: 'tester@example.com', role: 'Admin', isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: null },
  }) }));
  await page.route('**/api/products', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.getByLabel('Email address').fill('tester@example.com');
  await page.getByLabel('Password', { exact: true }).fill('TestPassword123!');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await page.getByRole('navigation').getByRole('link', { name: 'Products', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Your catalog is a clean slate' })).toBeVisible();
  await page.evaluate(async () => {
    (window as unknown as { releaseOldResponse: () => void }).releaseOldResponse();
    await new Promise(resolve => setTimeout(resolve, 100));
  });
  await expect(page).toHaveURL(/\/products$/);
  expect(await page.evaluate(() => JSON.parse(sessionStorage.getItem('catalog.session')!).accessToken)).toBe('new-session');
});
