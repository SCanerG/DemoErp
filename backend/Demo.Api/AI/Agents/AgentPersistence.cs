using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace Demo.Api.AI.Agents;

public sealed class AiAgentRun { public Guid ExecutionId {get;set;} public string Mode {get;set;}="auto"; public bool CancelRequested {get;set;} public DateTimeOffset? StartedAt {get;set;} public DateTimeOffset? CompletedAt {get;set;} public Guid? RetryOf {get;set;} }
public sealed class AiAgentStep {
    public Guid Id {get;set;}=Guid.NewGuid(); public Guid ExecutionId {get;set;} public int Sequence {get;set;}
    public string Agent {get;set;}=""; public string Outcome {get;set;}="running";
    public long DurationMs {get;set;} public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public static class AgentDbModel {
    public static void Configure(ModelBuilder model) {
        model.Entity<AiAgentRun>(e=> { e.ToTable("AiAgentRuns");e.HasKey(x=>x.ExecutionId);e.Property(x=>x.Mode).HasMaxLength(24);
            e.HasOne<AiExecution>().WithOne().HasForeignKey<AiAgentRun>(x=>x.ExecutionId).OnDelete(DeleteBehavior.Cascade); });
        model.Entity<AiAgentStep>(e=> { e.ToTable("AiAgentSteps",t=>t.HasCheckConstraint("CK_AiAgentSteps_Sequence","\"Sequence\" >= 0 AND \"Sequence\" < 5"));
            e.HasKey(x=>x.Id); e.HasIndex(x=>new {x.ExecutionId,x.Sequence}).IsUnique(); e.Property(x=>x.Agent).HasMaxLength(24);e.Property(x=>x.Outcome).HasMaxLength(64);
            e.HasOne<AiAgentRun>().WithMany().HasForeignKey(x=>x.ExecutionId).OnDelete(DeleteBehavior.Cascade); });
    }
}
public sealed class AgentRunStore(IServiceScopeFactory scopes, AiExecutionJournal journal, AiToolExecutor access)
{
    public async Task Attach(Guid id,string mode,CancellationToken ct) {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        db.Add(new AiAgentRun { ExecutionId=id,Mode=mode });await db.SaveChangesAsync(ct);
        await db.Set<AiExecution>().Where(r=>r.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.Outcome,"queued"),ct);await tx.CommitAsync(ct);
    }
    public async Task<AgentRunStatus> Read(Guid id,ClaimsPrincipal user,CancellationToken ct) {
        var execution=await journal.Read(id,user,ct);
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mode=await db.Set<AiAgentRun>().Where(r=>r.ExecutionId==id).Select(r=>r.Mode).SingleOrDefaultAsync(ct)??throw new BusinessException("aiExecutionNotFound",404);
        var steps=await db.Set<AiAgentStep>().AsNoTracking().Where(s=>s.ExecutionId==id).OrderBy(s=>s.Sequence).Take(5)
            .Select(s=>new AgentStepStatus(s.Sequence,s.Agent,s.Outcome,s.DurationMs,s.CreatedAt)).ToListAsync(ct);
        await access.Recheck(user,AccessPolicies.BusinessRead,ct);var metadata=await db.Set<AiAgentRun>().AsNoTracking().SingleAsync(r=>r.ExecutionId==id,ct);
        var owner=Guid.Parse(user.FindFirstValue("sub")!);
        return new(mode,execution,steps,owner,metadata.CompletedAt,metadata.RetryOf,metadata.StartedAt);
    }
    public async Task<List<AgentRunSummary>> List(ClaimsPrincipal user,CancellationToken ct) {
        await access.Recheck(user,AccessPolicies.BusinessRead,ct);var owner=Guid.Parse(user.FindFirstValue("sub")!);
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await (from r in db.Set<AiAgentRun>() join e in db.Set<AiExecution>() on r.ExecutionId equals e.Id
            where e.UserId==owner && e.ExpiresAt>DateTimeOffset.UtcNow orderby e.CreatedAt descending
            select new AgentRunSummary(e.Id,r.Mode,e.Outcome,e.Provider,e.Model,e.CreatedAt)).Take(20).ToListAsync(ct);
    }
    public async Task<string> Claim(Guid id,ClaimsPrincipal user,CancellationToken ct) {
        var run=await Read(id,user,ct);
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var owner=Guid.Parse(user.FindFirstValue("sub")!);
        if(await db.Set<AiExecution>().Where(r=>r.Id==id&&r.UserId==owner&&r.ExpiresAt>DateTimeOffset.UtcNow&&r.Outcome=="queued")
            .ExecuteUpdateAsync(s=>s.SetProperty(r=>r.Outcome,"running"),ct)!=1) throw new BusinessException("aiAgentAlreadyStarted",409);
        await db.Set<AiAgentRun>().Where(r=>r.ExecutionId==id).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.StartedAt,DateTimeOffset.UtcNow),ct);
        return run.Mode;
    }
    public async Task<Guid> Step(Guid run,int sequence,string agent,CancellationToken ct) {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var step=new AiAgentStep {ExecutionId=run,Sequence=sequence,Agent=agent};db.Add(step);await db.SaveChangesAsync(ct);return step.Id;
    }
    public async Task StepEnd(Guid id,string outcome,long duration) {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await db.Set<AiAgentStep>().Where(s=>s.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Outcome,outcome).SetProperty(x=>x.DurationMs,duration),deadline.Token);
    }
    public async Task Cancel(Guid id,ClaimsPrincipal user,CancellationToken ct) {
        var status=await Read(id,user,ct);
        if(status.Execution.Outcome is not ("queued" or "running"))throw new BusinessException("aiAgentAlreadyStarted",409);
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<AiAgentRun>().Where(r=>r.ExecutionId==id).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.CancelRequested,true),ct);
        if(await db.Set<AiExecution>().Where(r=>r.Id==id&&r.Outcome=="queued").ExecuteUpdateAsync(s=>s.SetProperty(r=>r.Outcome,"cancelled"),ct)==1)await Finish(id);
    }
    public async Task<bool> Cancelled(Guid id,CancellationToken ct) {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<AiAgentRun>().Where(r=>r.ExecutionId==id).Select(r=>r.CancelRequested).SingleAsync(ct);
    }
    public async Task Watch(Guid id,CancellationTokenSource execution,CancellationToken stop) {
        try { while(!stop.IsCancellationRequested){await Task.Delay(500,stop);if(await Cancelled(id,stop)){execution.Cancel();return;}} }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception) { execution.Cancel(); }
    }
    public async Task Finish(Guid id) {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await db.Set<AiAgentRun>().Where(r=>r.ExecutionId==id).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.CompletedAt,DateTimeOffset.UtcNow),deadline.Token);
    }
    public async Task Recover(Guid id,ClaimsPrincipal user,int timeoutSeconds,CancellationToken ct) {
        var run=await Read(id,user,ct);
        if(run.Execution.Outcome!="running"||(run.StartedAt is null||run.StartedAt.Value.AddSeconds(timeoutSeconds+30)>DateTimeOffset.UtcNow))throw new BusinessException("aiWorkflowNotRecoverable",409);
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await using var tx=await db.Database.BeginTransactionAsync(ct);
        if(await db.Set<AiExecution>().Where(r=>r.Id==id&&r.Outcome=="running").ExecuteUpdateAsync(s=>s.SetProperty(r=>r.Outcome,"interrupted"),ct)!=1)throw new BusinessException("aiWorkflowNotRecoverable",409);
        await db.Set<AiAgentRun>().Where(r=>r.ExecutionId==id).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.CancelRequested,true).SetProperty(r=>r.CompletedAt,DateTimeOffset.UtcNow),ct);
        await db.Set<AiAgentStep>().Where(r=>r.ExecutionId==id&&r.Outcome=="running").ExecuteUpdateAsync(s=>s.SetProperty(r=>r.Outcome,"interrupted"),ct);await tx.CommitAsync(ct);
    }
    public async Task LinkRetry(Guid id,Guid parent,CancellationToken ct) {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<AiAgentRun>().Where(r=>r.ExecutionId==id).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.RetryOf,parent),ct);
    }
}
