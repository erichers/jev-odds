import { expect, test } from '@playwright/test';

const odds = {
  ticker: 'SPY',
  name: 'SPDR S&P 500',
  direction: 'up',
  percent: 5,
  spot: 500,
  asOf: '2026-10-07',
  targetDate: '2027-01-15',
  effectiveDate: '2027-01-15',
  tradingDays: 60,
  years: 60 / 252,
  sigma: 0.16,
  volSource: '60',
  volSampleDays: 60,
  vol20: 0.12,
  vol60: 0.16,
  vol252: 0.18,
  volBlend: 0.15,
  drift: 'zero',
  mu: 0,
  nu: -0.01,
  upper: 525,
  lower: null,
  analyticClose: 0.42,
  analyticTouch: 0.7,
  monteCarloClose: 0.41,
  monteCarloTouch: 0.69,
  monteCarloPaths: 20000,
  monteCarloSeed: 184208,
  expectedLow: 470,
  expectedHigh: 530,
  empiricalClose: null,
  empiricalTouch: null,
  empiricalSamples: 0,
  origin: 'cached',
  provider: 'seed',
  density: [],
  paths: [],
  formula: 'test',
};

test.beforeEach(async ({ page }) => {
  await page.route('**/api/**', async (route) => {
    const url = route.request().url();
    if (route.request().method() === 'POST' && url.includes('/api/odds')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(odds) });
      return;
    }
    if (url.includes('/api/market/')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          ticker: 'SPY',
          name: 'SPDR S&P 500',
          lastClose: 500,
          asOf: '2026-10-07',
          origin: 'cached',
          provider: 'seed',
          bars: 1000,
          vol20: 0.12,
          vol60: 0.16,
          vol252: 0.18,
          volBlend: 0.15,
          logDriftAnnual: 0.08,
        }),
      });
      return;
    }
    if (url.includes('/api/calendar')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ from: '2026-10-07', to: '2027-01-15', tradingDays: 60, effectiveDate: '2027-01-15' }),
      });
      return;
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });
});

test('default form submits a 5 percent move', async ({ page }) => {
  await page.goto('/odds');
  const percent = page.locator('#percent');
  await expect(percent).toHaveValue('5');
  await expect(percent).toHaveAttribute('step', 'any');
  await expect(page.locator('form')).toHaveAttribute('novalidate', '');
  const mismatch = await percent.evaluate((el: HTMLInputElement) => el.validity.stepMismatch);
  expect(mismatch).toBe(false);

  const posted = page.waitForRequest((req) => req.method() === 'POST' && req.url().includes('/api/odds'));
  await page.getByRole('button', { name: 'Calculate odds' }).click();
  const request = await posted;
  expect(request.postDataJSON()).toMatchObject({
    ticker: 'SPY',
    percent: 5,
    direction: 'up',
    targetDate: '2027-01-15',
    volWindow: '60',
    drift: 'zero',
    volOverridePercent: null,
  });
  await expect(page.getByRole('heading', { name: 'SPY up 5%' })).toBeVisible();
});

test('an out of range move shows an inline message', async ({ page }) => {
  await page.goto('/odds');
  let posted = false;
  page.on('request', (req) => {
    if (req.method() === 'POST' && req.url().includes('/api/odds')) {
      posted = true;
    }
  });
  await page.locator('#percent').fill('0');
  await page.getByRole('button', { name: 'Calculate odds' }).click();
  await expect(page.locator('#form-error')).toHaveText('Enter a move of at least 0.1 percent.');
  expect(posted).toBe(false);
});
