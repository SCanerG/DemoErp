import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { runAgentLiveSmoke } from '../ai-agents-live-smoke.mjs';
test('agent live smoke defaults to no network', async()=>{
  const output=[];assert.deepEqual(await runAgentLiveSmoke({},s=>output.push(s)),{skipped:true});assert.match(output[0],/^SKIP:/);
});
test('agent live smoke never sends credentials to non-loopback origins',async()=>{
  for(const url of ['http://remote.invalid','https://remote.invalid','http://user:pass@localhost','http://localhost/path'])
    await assert.rejects(()=>runAgentLiveSmoke({AI_AGENTS_LIVE_SMOKE:'true',API_URL:url}),/loopback/);
});
test('agent live smoke requires local ERP credentials',async()=>{
  await assert.rejects(()=>runAgentLiveSmoke({AI_AGENTS_LIVE_SMOKE:'true',API_URL:'http://127.0.0.1'}),/credentials/);
});
for(const mock of [false,true])test(`four-scenario live verification ${mock?'rejects Mock':'validates audit and read-only draft'} using intercepted local API`,async()=>{
  const runs=new Map();let sequence=0;
  const server=http.createServer(async(req,res)=>{
    let body='';for await(const c of req)body+=c;const input=body?JSON.parse(body):null;
    res.setHeader('Content-Type','application/json');let result;
    if(req.url==='/api/auth/login')result={accessToken:'synthetic-session'};
    else if(req.url==='/api/ai/agents/status')result={provider:mock?'Mock':'Ollama',isMock:mock,health:mock?'mock':'ready',model:'qwen3:8b'};
    else if(req.url==='/api/products')result=[{id:'product',name:'Synthetic product',price:12.5,isActive:true}];
    else if(req.url==='/api/customers')result=[{id:'customer',name:'Synthetic customer',isActive:true}];
    else if(req.url==='/api/orders')result=[];
    else if(req.url==='/api/inventory')result=[{productId:'product',quantityOnHand:5,minimumStockLevel:10}];
    else if(req.url==='/api/ai/agents/runs') {const id=String(++sequence);runs.set(id,input.mode==='auto'?'collaboration':input.mode);result={id};}
    else {
      const id=req.url.split('/')[5];const intent=runs.get(id);const multi=intent==='collaboration';
      if(req.url.endsWith('/execute'))result={executionId:id,intent,isMock:false,language:'tr',answer:'Synthetic explanation',
        inventory:['inventory','collaboration'].includes(intent)?{}:null,sales:['sales','collaboration'].includes(intent)?{}:null,
        sources:[{name:'analyze_replenishment'},{name:'get_sales_summary'}],
        orderDraft:intent==='order'?{notSubmitted:true,persisted:false,requiresHumanReview:true,clarifications:[],customerId:'customer',lines:[{productId:'product',quantity:2,unitPrice:12.5}],total:25}:null};
      else result={execution:{id,provider:'Ollama',model:'qwen3:8b',providerCalls:1,outcome:'success',totalTokens:30,tools:[{name:'read_report',outcome:'success'}]},
        steps:['orchestrator',...(multi?['sales','inventory']:[intent]),'summary'].map(agent=>({agent,outcome:'success'}))};
    }
    res.end(JSON.stringify(result));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const env={AI_AGENTS_LIVE_SMOKE:'true',API_URL:`http://127.0.0.1:${server.address().port}`,AI_LIVE_EMAIL:'synthetic@example.com',AI_LIVE_PASSWORD:'synthetic-only-test-password'};
  try {
    if(mock)await assert.rejects(()=>runAgentLiveSmoke(env,()=>{}),/real Ollama/);
    else {const lines=[];const result=await runAgentLiveSmoke(env,s=>lines.push(s));assert.equal(result.liveOllamaAgentsVerified,true);assert.equal(result.scenarios.length,4);assert.ok(!lines.join('').includes(env.AI_LIVE_PASSWORD));}
  }finally{await new Promise(resolve=>server.close(resolve));}
});
