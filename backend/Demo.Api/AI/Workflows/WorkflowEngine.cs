using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Demo.Api.Data;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Demo.Api.AI.Workflows;

public sealed class WorkflowEngine(AppDbContext db,WorkflowService workflows,IWorkflowHandlers handlers,WorkflowOptions options,
    Quality.QualityTelemetry telemetry,ILogger<WorkflowEngine> logger)
{
    private const long WorkerLock=11820261021;
    public async Task Schedule(CancellationToken ct)
    {
        if(!options.Enabled)return;
        var ids=await db.Set<WorkflowDefinition>().AsNoTracking().Where(w=>w.Enabled&&!w.Archived&&w.NextRunAt<=DateTimeOffset.UtcNow).OrderBy(w=>w.NextRunAt).Take(100).Select(w=>w.Id).ToArrayAsync(ct);
        foreach(var id in ids)
        {
            db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(ct);
            var row=await db.Set<WorkflowDefinition>().FromSqlInterpolated($"SELECT * FROM \"WorkflowDefinitions\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
            if(!row.Enabled||row.Archived||row.NextRunAt>DateTimeOffset.UtcNow)continue;
            try
            {
                var version=await workflows.Published(row,ct);var user=await workflows.Identity(row,version,ct);var graph=WorkflowModel.Read<WorkflowGraph>(version.Graph);var start=WorkflowRegistry.Validate(graph)[0];var now=DateTimeOffset.UtcNow;
                if(start.Type=="trigger.manual"){row.NextRunAt=null;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);continue;}
                var key=start.Type=="trigger.schedule"?"weekly:"+row.NextRunAt!.Value.ToUnixTimeSeconds():"";
                if(key=="")
                {
                    var signal=await handlers.Trigger(start,user,ct);
                    if(signal.Evidence)key=WorkflowModel.Write(signal with{DueAt=null});else row.LastSignal=null;
                }
                row.NextRunAt=start.Type=="trigger.schedule"?WorkflowRegistry.NextWeekly(start.Config,now):now.AddMinutes(1);
                if(key!="")
                {
                    var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version.Id+"|"+key)));
                    if(hash!=row.LastSignal&&await db.Set<WorkflowRun>().CountAsync(r=>r.OwnerId==row.OwnerId&&(r.State=="queued"||r.State=="running"||r.State=="waitingApproval"),ct)<20)
                    {
                        row.SignalOccurrence++;
                        var request=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(hash+"|"+row.SignalOccurrence)).AsSpan(0,16));
                        if(!await db.Set<WorkflowRun>().AnyAsync(r=>r.WorkflowId==id&&r.RequestKey==request,ct))db.Add(new WorkflowRun{WorkflowId=id,VersionId=version.Id,OwnerId=row.OwnerId,RequestKey=request,Mode=start.Type=="trigger.schedule"?"scheduled":"event"});
                        row.LastSignal=hash;
                    }
                }
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            }
            catch(BusinessException e)when(e.Code=="workflowAuthorizationChanged")
            {
                row.Enabled=false;row.NextRunAt=null;row.Revision++;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                logger.LogWarning("Workflow {WorkflowId} disabled after authorization change",id);
            }
        }
    }
    public async Task<bool> ProcessNext(CancellationToken ct)
    {
        if(!options.Enabled)return false;
        await db.Database.OpenConnectionAsync(ct);var acquired=false;
        try
        {
            // One execution across API instances; connection-scoped lock releases on crash/disconnect.
            acquired=await db.Database.SqlQueryRaw<bool>($"SELECT pg_try_advisory_lock({WorkerLock}) AS \"Value\"").SingleAsync(ct);if(!acquired)return false;
            db.ChangeTracker.Clear();WorkflowRun? run;var now=DateTimeOffset.UtcNow;
            await using(var claim=await db.Database.BeginTransactionAsync(ct))
            {
                run=await db.Set<WorkflowRun>().FromSqlInterpolated($"SELECT * FROM \"WorkflowRuns\" WHERE \"State\"='queued' OR (\"State\"='running' AND \"LeaseUntil\"<{now}) ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct);
                if(run is null)return false;
                run.State="running";run.Lease=Guid.NewGuid().ToString("N");run.LeaseUntil=now.AddMinutes(3);run.StartedAt??=now;run.Attempts++;await db.SaveChangesAsync(ct);await claim.CommitAsync(ct);
            }
            var id=run.Id;var lease=run.Lease;using var root=telemetry.Begin("workflow","automation",id,run.OwnerId);run.TraceId=Activity.Current?.TraceId.ToHexString()??"";await db.SaveChangesAsync(ct);
            try
            {
                if(run.Attempts>3)throw new BusinessException("workflowRecoveryLimit",409);
                var definition=await db.Set<WorkflowDefinition>().AsNoTracking().SingleAsync(w=>w.Id==run.WorkflowId,ct);
                if(definition.Archived)throw new BusinessException("workflowArchived",409);
                var version=await db.Set<WorkflowVersion>().AsNoTracking().SingleAsync(v=>v.Id==run.VersionId,ct);var user=await workflows.Identity(definition,version,ct);
                var graph=WorkflowModel.Read<WorkflowGraph>(version.Graph);var ordered=WorkflowRegistry.Validate(graph);var existing=await db.Set<WorkflowNodeRun>().Where(n=>n.RunId==id).ToDictionaryAsync(n=>n.NodeId,ct);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(graph.Policy.TimeoutSeconds));var token=timeout.Token;
                foreach(var node in ordered)
                {
                    token.ThrowIfCancellationRequested();await Active(id,lease,user,token);
                    if(existing.TryGetValue(node.Id,out var completed)&&completed.State is "success" or "skipped")continue;
                    var incoming=graph.Edges.Where(e=>e.Target==node.Id).ToArray();WorkflowValue input=new();var active=incoming.Length==0;
                    foreach(var edge in incoming)
                    {
                        var parent=existing[edge.Source];var value=WorkflowModel.Read<WorkflowValue>(parent.Output);
                        var source=graph.Nodes.Single(n=>n.Id==edge.Source);
                        var signal=source.Type is not ("trigger.inventory" or "trigger.orderStatus" or "trigger.overduePurchase")||value.Evidence;
                        if(parent.State=="success"&&signal&&(edge.When=="always"||edge.When==(value.Decision?"true":"false"))){input=value;active=true;}
                    }
                    var history=completed??new WorkflowNodeRun{RunId=id,NodeId=node.Id,Type=node.Type};if(completed is null){db.Add(history);existing.Add(node.Id,history);}
                    if(!active){history.State="skipped";history.Output=WorkflowModel.Write(input);history.EndedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(token);continue;}
                    if(completed is not null&&completed.State=="running"&&!WorkflowRegistry.Pure(node.Type))throw new BusinessException("workflowInterruptedReview",409);
                    var retries=graph.Policy.Retries;
                    if(incoming.Length==1&&graph.Nodes.Single(n=>n.Id==incoming[0].Source) is {Type:"control.retry"} retry)retries=Math.Min(retries,retry.Config.Retries);
                    if(!WorkflowRegistry.Pure(node.Type))retries=0;
                    WorkflowValue output=input;var watch=Stopwatch.StartNew();using var span=telemetry.Begin("workflowNode",node.Type,id,run.OwnerId);
                    while(true)
                    {
                        if(history.Attempts>=retries+1)throw new BusinessException("workflowRecoveryLimit",409);
                        history.Attempts++;history.State="running";history.Error=null;await db.SaveChangesAsync(token);
                        try
                        {
                            await Active(id,lease,user,token);
                            if(node.Type.StartsWith("agent.",StringComparison.Ordinal))
                            {
                                var charged=await db.Set<WorkflowRun>().Where(r=>r.Id==id&&r.Lease==lease&&r.State=="running"&&r.AgentCalls<graph.Policy.MaxAgents).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.AgentCalls,r=>r.AgentCalls+1),token);
                                if(charged!=1)throw new BusinessException("workflowLimit",409);
                            }
                            if(WorkflowRegistry.Branch(node.Type))output=WorkflowHandlers.Condition(node,input);
                            else if(run.Mode=="simulation")output=WorkflowHandlers.Simulate(node,input);
                            else if(node.Type.StartsWith("trigger.",StringComparison.Ordinal))output=await handlers.Trigger(node,user,token);
                            else if(node.Type is not ("action.propose" or "action.notify"))output=await handlers.Execute(node,input,run,user,token);
                            break;
                        }
                        catch(Exception e)when(WorkflowRegistry.Pure(node.Type)&&history.Attempts<=retries&&Transient(e)&&!token.IsCancellationRequested)
                        {history.Error=Category(e);span?.Retry(history.Attempts);await db.SaveChangesAsync(token);await Task.Delay(TimeSpan.FromMilliseconds(250*history.Attempts),token);}
                    }
                    await using var checkpoint=await db.Database.BeginTransactionAsync(token);var current=await Locked(id,token);
                    if(current.State!="running"||current.Lease!=lease)throw new BusinessException("workflowCancelled",409);
                    await workflows.Identity(definition,version,token);
                    if(run.Mode!="simulation"&&node.Type=="action.propose")output=await handlers.Execute(node,input,run,user,token);
                    if(run.Mode!="simulation"&&node.Type=="action.notify"&&!await db.Set<WorkflowNotification>().AnyAsync(n=>n.RunId==id&&n.NodeId==node.Id,token))
                        db.Add(new WorkflowNotification{RunId=id,OwnerId=run.OwnerId,NodeId=node.Id,Title=definition.Name,Facts=WorkflowModel.Write(output)});
                    var payload=WorkflowModel.Write(output);if(payload.Length>40000)throw new BusinessException("workflowOutputLimit",422);
                    history.Output=payload;history.State=node.Type=="action.requestApproval"&&run.Mode!="simulation"?"waitingApproval":"success";history.EndedAt=DateTimeOffset.UtcNow;history.DurationMs+=watch.ElapsedMilliseconds;
                    if(history.State=="waitingApproval")current.State="waitingApproval";
                    await db.SaveChangesAsync(token);await checkpoint.CommitAsync(token);
                    if(current.State=="waitingApproval")return true;
                }
                await using var done=await db.Database.BeginTransactionAsync(token);var final=await Locked(id,token);
                if(final.State=="running"&&final.Lease==lease){final.State="success";final.EndedAt=DateTimeOffset.UtcNow;final.Error=null;await db.SaveChangesAsync(token);}await done.CommitAsync(token);
            }
            catch(Exception error)
            {
                root?.Fail(error);db.ChangeTracker.Clear();using var final=new CancellationTokenSource(TimeSpan.FromSeconds(5));var category=Category(error);
                await db.Set<WorkflowRun>().Where(r=>r.Id==id&&r.State=="running"&&r.Lease==lease).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.State,ct.IsCancellationRequested?"queued":"failed").SetProperty(r=>r.Error,category).SetProperty(r=>r.EndedAt,ct.IsCancellationRequested?(DateTimeOffset?)null:DateTimeOffset.UtcNow),final.Token);
                await db.Set<WorkflowNodeRun>().Where(n=>n.RunId==id&&n.State=="running").ExecuteUpdateAsync(s=>s.SetProperty(n=>n.Error,category).SetProperty(n=>n.State,ct.IsCancellationRequested?"running":"failed"),final.Token);
                logger.LogWarning("Workflow execution {ExecutionId} version-bound outcome error category {Category}",id,category);
            }
            return true;
        }
        finally
        {
            if(acquired)try{await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({WorkerLock})",CancellationToken.None);}catch(Exception){logger.LogWarning("Workflow worker lock connection interrupted");}
            await db.Database.CloseConnectionAsync();
        }
    }
    private async Task<WorkflowRun> Locked(Guid id,CancellationToken ct)
    {
        var row=await db.Set<WorkflowRun>().FromSqlInterpolated($"SELECT * FROM \"WorkflowRuns\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
        // EF identity resolution must not reuse stale running state after another context cancelled the run.
        await db.Entry(row).ReloadAsync(ct);return row;
    }
    private async Task Active(Guid id,string? lease,System.Security.Claims.ClaimsPrincipal user,CancellationToken ct)
    {
        await workflows.Check(user,true,ct);if(!await db.Set<WorkflowRun>().AsNoTracking().AnyAsync(r=>r.Id==id&&r.State=="running"&&r.Lease==lease,ct))throw new BusinessException("workflowCancelled",409);
    }
    private static bool Transient(Exception error)=>error is NpgsqlException{IsTransient:true} or TimeoutException || error is BusinessException{Status:503};
    private static string Category(Exception error)=>error is BusinessException b?Quality.QualityTelemetry.Safe(b.Code):error is OperationCanceledException?"workflowTimeout":"workflowExecutionFailed";
}
public sealed class WorkflowWorker(IServiceScopeFactory scopes,WorkflowOptions options,ILogger<WorkflowWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Enabled)return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await using var scope=scopes.CreateAsyncScope();var engine=scope.ServiceProvider.GetRequiredService<WorkflowEngine>();await engine.Schedule(stoppingToken);await engine.ProcessNext(stoppingToken);}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){return;}
            catch(Exception){logger.LogWarning("Workflow worker temporarily unavailable");}
            try{await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){return;}
        }
    }
}
