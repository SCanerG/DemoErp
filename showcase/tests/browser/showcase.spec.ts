import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {screens} from '../../src/model';
test.beforeEach(async({page})=>{
 // Ignore unrelated machine security-extension injection; never change OS security.
 await page.route(/.*kaspersky-labs\.com.*/,route=>route.abort());
});
for(const locale of ['tr','en'] as const){
 for(const width of [390,1440]){
  test(locale+' '+width+' navigation, responsive layout, theme and accessibility',async({page})=>{
   await page.setViewportSize({width,height:950});
   const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
   const response=await page.goto('/'+locale+'/');expect(response?.status()).toBe(200);
   await expect(page.locator('h1')).toBeVisible();
   await expect(page.locator('html')).toHaveAttribute('lang',locale);
   expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);
   const links=page.locator('#mobile-links');
   if(width===390){await page.getByRole('button',{name:locale==='tr'?'Menü':'Menu',exact:true}).click();await expect(links).toBeVisible();}
   await links.locator('a[href="#architecture"]').click();
   await expect(page).toHaveURL(/#architecture$/);
   await expect(page.getByRole('link',{name:locale==='tr'?'Switch to English':'Türkçeye geç'})).toHaveAttribute('href','/'+(locale==='tr'?'en':'tr')+'/#architecture');
   await page.getByRole('button',{name:locale==='tr'?'Temayı değiştir':'Change theme'}).click();
   await expect(page.locator('html')).toHaveAttribute('data-theme','light');
   for(const theme of ['light','dark']){
    if(theme==='dark')await page.getByRole('button',{name:locale==='tr'?'Temayı değiştir':'Change theme'}).click();
    const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();
    expect(result.violations).toEqual([]);
    await page.screenshot({path:'verification/'+locale+'-'+width+'-'+theme+'-viewport.png'});
   }
   expect(errors).toEqual([]);
   for(const img of await page.locator('img').all()){await img.scrollIntoViewIfNeeded();await expect.poll(()=>img.evaluate((n:HTMLImageElement)=>n.complete&&n.naturalWidth>0)).toBe(true);}
   await page.evaluate(()=>{(document.activeElement as HTMLElement)?.blur();window.scrollTo({top:0,behavior:'instant'});});
   await page.screenshot({path:'verification/'+locale+'-'+width+'.png',fullPage:true});
  });
 }
 test(locale+' deterministic preview and reset',async({page})=>{
  await page.goto('/'+locale+'/');
  await expect(page.getByText(locale==='tr'?'Tarayıcı İçi Etkileşimli Önizleme':'Browser-Based Interactive Preview',{exact:true})).toBeVisible();
  const next=page.getByRole('button',{name:locale==='tr'?'Sonraki adımı uygula':'Apply next step'});
  for(let i=0;i<6;i++)await next.click();
  await expect(page.getByTestId('balance-0')).toHaveText('40');
  await expect(page.getByTestId('balance-2')).toHaveText('3');
  await expect(page.getByTestId('balance-3')).toHaveText('2');
  await expect(page.getByRole('button',{name:locale==='tr'?'Örnek akış tamamlandı':'Sample workflow complete'})).toBeDisabled();
  await page.getByLabel(locale==='tr'?'Örnek üretim partisi':'Sample production batch').selectOption('10');
  await expect(page.getByTestId('balance-0')).toHaveText('0');
  for(let i=0;i<6;i++)await next.click();
  await expect(page.getByTestId('balance-0')).toHaveText('20');
  await expect(page.getByTestId('balance-2')).toHaveText('8');
  await page.getByRole('button',{name:locale==='tr'?'Örneği sıfırla':'Reset sample'}).click();
  await expect(page.getByTestId('balance-0')).toHaveText('0');
 });
 test(locale+' real gallery filters, media, dialog keyboard and focus restoration',async({page})=>{
  await page.goto('/'+locale+'/#screens');
  await expect(page.locator('.screen-card')).toHaveCount(10);
  await page.getByLabel(locale==='tr'?'Cihaz':'Device',{exact:true}).selectOption('mobile');
  await expect(page.locator('.screen-card')).toHaveCount(1);
  const opener=page.locator('.screen-open');await opener.focus();await page.keyboard.press('Enter');
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByRole('dialog').locator('img')).toBeVisible();
  const scan=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();
  expect(scan.violations).toEqual([]);
  for(let i=0;i<6;i++){await page.keyboard.press('Tab');expect(await page.getByRole('dialog').evaluate(d=>d.contains(document.activeElement))).toBe(true);}
  await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).not.toBeVisible();await expect(opener).toBeFocused();
  await page.getByLabel(locale==='tr'?'Cihaz':'Device',{exact:true}).selectOption('all');
  await page.getByRole('button',{name:locale==='tr'?'Üretim':'Manufacturing',exact:true}).click();
  await expect(page.locator('.screen-card')).toHaveCount(3);
  await page.getByRole('button',{name:locale==='tr'?'Tümü':'All',exact:true}).click();
  for(const s of screens){
   const card=page.locator('.screen-card').filter({has:page.locator('img[src="/media/'+s.id+'-640.webp"]')});
   await card.scrollIntoViewIfNeeded();
   await expect.poll(()=>card.locator('img').evaluate((img:HTMLImageElement)=>img.complete&&img.naturalWidth>0)).toBe(true);
  }
 });
}
test('section survives actual language navigation and mobile menu Escape restores focus',async({page})=>{
 await page.setViewportSize({width:390,height:844});await page.goto('/tr/');
 const menu=page.getByRole('button',{name:'Menü',exact:true});await menu.click();await page.keyboard.press('Escape');await expect(menu).toBeFocused();
 await menu.click();await page.locator('#mobile-links a[href="#screens"]').click();
 await page.getByRole('link',{name:'Switch to English'}).click();
 await expect(page).toHaveURL('/en/#screens');await expect(page.locator('html')).toHaveAttribute('lang','en');
 await expect(page.locator('#screens h2')).toHaveText('Look beyond the interface.');
 await page.getByRole('link',{name:'Türkçeye geç'}).click();await expect(page).toHaveURL('/tr/#screens');
});
test('no app network dependencies and all internal media load',async({page})=>{
 const unexpected:string[]=[];
 page.on('request',r=>{const u=new URL(r.url());if(u.hostname!=='127.0.0.1'&&!u.hostname.endsWith('kaspersky-labs.com'))unexpected.push(r.url());});
 await page.goto('/en/');await page.getByRole('button',{name:'Apply next step'}).click();
 await expect(page.locator('h1')).toBeVisible();
 for(const img of await page.locator('img').all()){await img.scrollIntoViewIfNeeded();await expect.poll(()=>img.evaluate((n:HTMLImageElement)=>n.complete&&n.naturalWidth>0)).toBe(true);}
 expect(unexpected).toEqual([]);
 expect(await page.locator('video,iframe,form').count()).toBe(0);
});
test('root page, source anchors and reduced motion are valid',async({page})=>{
 await page.emulateMedia({reducedMotion:'reduce'});const response=await page.goto('/');expect(response?.status()).toBe(200);
 await expect(page.locator('html')).toHaveAttribute('lang','tr');
 expect(await page.evaluate(()=>getComputedStyle(document.documentElement).scrollBehavior)).toBe('auto');
 for(const href of await page.locator('a[href^="#"]').evaluateAll(nodes=>nodes.map(n=>n.getAttribute('href')!))){
  expect(await page.locator(href).count()).toBe(1);
 }
 const sourceLinks=await page.locator('a[href*="/blob/"]').evaluateAll(nodes=>nodes.map(n=>n.getAttribute('href')!));
 expect(sourceLinks.every(link=>link.includes('/fbada64d67859a526529368c440b80fc6a3dc32a/'))).toBe(true);
});

for(const width of [320,768,1024]){
 test('layout fits '+width+'px',async({page})=>{
  await page.setViewportSize({width,height:900});await page.goto('/tr/');
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);
  await expect(page.locator('h1')).toBeVisible();
 });
}
test('static HTTP status, canonical and security headers',async({request})=>{
 for(const path of ['/','/tr/','/en/','/robots.txt','/sitemap.xml']){
  const r=await request.get(path);expect(r.status()).toBe(200);
  expect(r.headers()['content-security-policy']).toContain("connect-src 'none'");
  expect(r.headers()['x-content-type-options']).toBe('nosniff');
 }
 expect((await request.get('/missing-page/')).status()).toBe(404);
 for(const lang of ['tr','en']){
  const r=await request.get('/'+lang,{maxRedirects:0});
  expect(r.status()).toBe(308);expect(r.headers().location).toBe('/'+lang+'/');
 }
});