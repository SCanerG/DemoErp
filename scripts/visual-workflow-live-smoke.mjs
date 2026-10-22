import {pathToFileURL} from 'node:url';
export async function runVisualWorkflowLiveSmoke(env=process.env,write=console.log){
  if(env.VISUAL_WORKFLOW_LIVE_SMOKE!=='true'){write('SKIP: VISUAL_WORKFLOW_LIVE_SMOKE=true required. No network request made.');return{skipped:true};}
  const base=new URL(env.API_URL??'http://127.0.0.1:5120');
  if(base.protocol!=='http:'||!['127.0.0.1','localhost','[::1]'].includes(base.hostname)||base.username||base.password||base.hash||base.search||!['','/'].includes(base.pathname))throw new Error('Only a loopback API origin is supported.');
  if(!env.AI_LIVE_EMAIL||!env.AI_LIVE_PASSWORD)throw new Error('Existing isolated administrator credentials required.');
  let token;
  async function request(path,data){const r=await fetch(new URL('/api'+path,base),{method:data?'POST':'GET',redirect:'error',signal:AbortSignal.timeout(150000),headers:{...(token?{Authorization:'Bearer '+token}:{}),...(data?{'Content-Type':'application/json'}:{})},...(data?{body:JSON.stringify(data)}:{})});if(!r.ok)throw new Error('Workflow live verification HTTP '+r.status);return r.json();}
  token=(await request('/auth/login',{email:env.AI_LIVE_EMAIL,password:env.AI_LIVE_PASSWORD})).accessToken;
  const provider=await request('/ai/agents/status');if(provider.provider!=='Ollama'||provider.health!=='ready')throw new Error('Actual local Ollama provider is required.');
  const before=await request('/orders');const start=performance.now();
  const result=await request('/workflows/ai-draft',{message:'Every Friday at 09:00 UTC, prepare a weekly sales report.',language:'en'});
  const latencyMs=Math.round(performance.now()-start);
  if(result.provider!=='Ollama'||result.origin!=='model'||!(result.reportedTokens>0)||result.workflow.enabled||result.workflow.publishedVersion!==null||!result.reviewRequired)throw new Error('A real validated inactive Ollama draft was not verified.');
  const saved=await request('/workflows/'+result.workflow.id);const trigger=saved.graph.nodes.find(n=>n.type==='trigger.schedule');
  if(!trigger||trigger.config.dayOfWeek!==5||trigger.config.hourUtc!==9||trigger.config.minuteUtc!==0)throw new Error('Draft schedule mismatch.');
  const runs=await request('/workflows/'+result.workflow.id+'/runs');const after=await request('/orders');
  if(runs.length||JSON.stringify(before.map(o=>o.id).sort())!==JSON.stringify(after.map(o=>o.id).sort()))throw new Error('Unexpected workflow execution or business write.');
  const output={liveWorkflowDraftVerified:true,workflowId:result.workflow.id,provider:result.provider,model:result.model,totalTokens:result.reportedTokens,latencyMs,validated:true,published:false,activated:false,ordersUnchanged:true};write(JSON.stringify(output));return output;
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href)runVisualWorkflowLiveSmoke().catch(()=>{console.error('Local workflow live verification failed. No automatic approval or activation was requested.');process.exitCode=1;});
