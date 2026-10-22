import { test, expect } from '@playwright/test';
test.use({ trace:'off', screenshot:'off' });
test('Live Quality Dashboard runs isolated deterministic evaluation and reads actual persisted traces',async({page})=>{
  test.skip(process.env.AI_QUALITY_UI_LIVE_SMOKE!=='true','Explicit isolated quality UI opt-in required.');test.setTimeout(120000);
  const email=process.env.AI_LIVE_EMAIL,password=process.env.AI_LIVE_PASSWORD;if(!email||!password)throw new Error('Existing isolated administrator credentials required.');
  await page.goto('/auth/login');await page.evaluate(()=>localStorage.setItem('catalog.language','en'));await page.reload();
  await page.getByLabel('Email address').fill(email);await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(/\/dashboard$/);
  await page.getByRole('link',{name:'AI Quality Dashboard',exact:true}).click();await expect(page.getByRole('heading',{name:'AI Quality Dashboard',exact:true})).toBeVisible();
  const pending=page.waitForResponse(r=>r.url().endsWith('/api/quality/evaluations')&&r.request().method()==='POST');await page.getByRole('button',{name:'Run deterministic evaluation',exact:true}).click();
  const response=await pending;expect(response.status()).toBe(202);const run=await response.json();await expect(page.getByText('Passed: 33 · Failed: 0 · Critical failures: 0')).toBeVisible();
  const select=page.getByLabel('Select a trace');const ids=await select.locator('option').evaluateAll(options=>options.map(option=>(option as HTMLOptionElement).value).filter(Boolean));expect(ids.length).toBeGreaterThan(0);
  const timelineResponse=page.waitForResponse(r=>r.url().includes('/api/quality/traces/'+ids[0]));await select.selectOption(ids[0]);const timeline=await timelineResponse;expect(timeline.status()).toBe(200);const spans=await timeline.json();expect(spans.length).toBeGreaterThan(0);expect(spans.every((span:{traceId:string})=>span.traceId===ids[0])).toBe(true);
  await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  console.log(JSON.stringify({liveQualityUiVerified:true,evaluationId:run.id,mode:'deterministic-fixtures',scenarios:33,passed:33,traceId:ids[0],traceObservations:spans.length,realModelInThisUiTest:false}));
});
