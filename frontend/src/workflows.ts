import type { Edge, Node } from '@xyflow/react';
export type WorkflowConfig = {threshold:number;field:string;comparison:string;number:number;status:string;date:string|null;expected:boolean;dayOfWeek:number;hourUtc:number;minuteUtc:number;periodDays:number;summarize:boolean;optional:boolean;question:string;customerId:string|null;productId:string|null;quantity:number;retries:number;language:string};
export type WorkflowNode = {id:string;type:string;x:number;y:number;config:WorkflowConfig};
export type WorkflowEdge = {id:string;source:string;target:string;when:string};
export type WorkflowGraph = {schemaVersion:number;nodes:WorkflowNode[];edges:WorkflowEdge[];policy:{timeoutSeconds:number;retries:number;maxAgents:number;maxTools:number}};
export type Workflow = {id:string;name:string;description:string;revision:number;publishedVersion:number|null;enabled:boolean;archived:boolean;nextRunAt:string|null};
export type WorkflowCatalog = {enabled:boolean;nodes:{type:string;category:string;pure:boolean;agent:boolean}[];templates:{key:string;graph:WorkflowGraph}[]};
export type DesignerNode = Node<{label:string;kind:string;config:WorkflowConfig},'workflow'>;
export type WorkflowRun = {id:string;workflowId:string;state:string;mode:string;versionId:string;error:string|null;traceId:string;createdAt:string;agentCalls:number;toolCalls:number};
export type WorkflowValue = {count:number;stock:number|null;sold:number|null;sales:number|null;evidence:boolean;approved:boolean;kind:string;data:Record<string,unknown>|null;proposalId:string|null;agentExecutionId:string|null};
export type WorkflowNodeRun = {nodeId:string;type:string;state:string;error:string|null;attempts:number;durationMs:number;output:WorkflowValue};
export type WorkflowRunDetail = {run:WorkflowRun;version:number;nodes:WorkflowNodeRun[]};
export function workflowConfig(language='tr'):WorkflowConfig{return{threshold:10,field:'count',comparison:'gt',number:0,status:'Pending',date:null,expected:true,dayOfWeek:5,hourUtc:9,minuteUtc:0,periodDays:7,summarize:false,optional:true,question:'',customerId:null,productId:null,quantity:1,retries:1,language};}
export function workflowNodes(graph:WorkflowGraph,label:(type:string)=>string):DesignerNode[]{return graph.nodes.map(n=>({id:n.id,type:'workflow',position:{x:n.x,y:n.y},data:{label:label(n.type),kind:n.type,config:n.config}}));}
export function workflowEdges(graph:WorkflowGraph):Edge[]{return graph.edges.map(e=>({id:e.id,source:e.source,target:e.target,sourceHandle:e.when,label:e.when==='always'?undefined:e.when}));}
export function workflowGraph(nodes:DesignerNode[],edges:Edge[],policy:WorkflowGraph['policy']):WorkflowGraph{return{schemaVersion:1,nodes:nodes.map(n=>({id:n.id,type:n.data.kind,x:n.position.x,y:n.position.y,config:n.data.config})),edges:edges.map(e=>({id:e.id,source:e.source,target:e.target,when:e.sourceHandle??'always'})),policy};}
export function workflowConnectionAllowed(source:string|null,target:string|null,nodes:DesignerNode[],edges:Edge[]):boolean{
  if(!source||!target||source===target||edges.some(e=>e.source===source&&e.target===target))return false;
  const from=nodes.find(n=>n.id===source),to=nodes.find(n=>n.id===target);if(!from||!to||from.data.kind==='control.end'||to.data.kind.startsWith('trigger.')||(to.data.kind!=='control.end'&&edges.some(e=>e.target===target)))return false;
  const pending=[target],seen=new Set<string>();while(pending.length){const id=pending.pop()!;if(id===source)return false;if(seen.has(id))continue;seen.add(id);pending.push(...edges.filter(e=>e.source===id).map(e=>e.target));}return true;
}
