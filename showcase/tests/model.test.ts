import {test} from 'node:test';
import {strict as assert} from 'node:assert';
import {advance,initialScenario,localeHref,localize,steps,layers,decisions,documents,source,revision,screens} from '../src/model.ts';
import {copy} from '../src/copy.ts';
test('deterministic workflow produces correct stock at every step',()=>{
 let s=initialScenario();
 const expected=[[0,0,0,0],[0,0,0,0],[60,0,0,0],[60,0,0,0],[40,5,0,0],[40,0,5,0],[40,0,3,2]];
 for(let i=0;i<=6;i++){assert.deepEqual([s.raw,s.work,s.finished,s.shipped],expected[i]);if(i<6)s=advance(s);}
 assert.deepEqual(advance(s),s);
});
test('each allowed batch preserves conservation and valid stock',()=>{
 for(const batch of [1,5,10]){let s=initialScenario(batch);for(let i=0;i<6;i++)s=advance(s);
 assert.equal(s.raw,60-4*batch);assert.equal(s.finished+s.shipped,batch);assert.equal(s.work,0);
 assert.deepEqual(s,Array.from({length:6}).reduce(advance,initialScenario(batch)));
 }
});
test('state transitions do not mutate previous state; reset is clean',()=>{
 const first=initialScenario();const second=advance(first);assert.equal(first.stage,0);assert.equal(second.stage,1);
 assert.deepEqual(initialScenario(),first);assert.throws(()=>initialScenario(3));
 assert.throws(()=>advance({...first,stage:3,raw:0}));
});
test('language links preserve the selected section',()=>{
 assert.equal(localeHref('en','#architecture'),'/en/#architecture');
 assert.equal(localeHref('tr','#screens'),'/tr/#screens');
 assert.equal(localize(['Türkçe','English'],'tr'),'Türkçe');
});
test('all interface keys exist in both languages',()=>{
 assert.deepEqual(Object.keys(copy.tr),Object.keys(copy.en));
 for(const locale of ['tr','en'] as const)for(const [key,value] of Object.entries(copy[locale])){
 assert(value.length>0,key);if(Array.isArray(value))assert(value.every(v=>v.length>0),key);
 }
});
test('source links are immutable revision references',()=>{
 for(const p of [...steps.map(s=>s.path),...layers.map(l=>l.path),...decisions.flatMap(d=>[d.path,d.test]),...documents.map(d=>d[0])])
 assert.equal(source(p),'https://github.com/SCanerG/DemoErp/blob/'+revision+'/'+p);
 assert.equal(screens.length,10);assert(screens.some(s=>s.mobile));
});
