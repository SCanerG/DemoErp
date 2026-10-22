using System.Security.Claims;
using Demo.Api.AI.Agents;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.AI.Workflows;

public sealed class WorkflowService(AppDbContext db, AiToolExecutor access, WorkflowOptions options, IServiceScopeFactory scopes)
{
    public static Guid Owner(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);
    public async Task Check(ClaimsPrincipal user, bool write, CancellationToken ct) => await access.Recheck(user, write ? AccessPolicies.BusinessWrite : AccessPolicies.BusinessRead, ct);
    public async Task<WorkflowDefinition> Own(Guid id, ClaimsPrincipal user, bool locked, CancellationToken ct)
    {
        await Check(user,locked,ct); var owner=Owner(user);
        return locked ? await db.Set<WorkflowDefinition>().FromSqlInterpolated($"SELECT * FROM \"WorkflowDefinitions\" WHERE \"Id\"={id} AND \"OwnerId\"={owner} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing()
            : await db.Set<WorkflowDefinition>().AsNoTracking().SingleOrDefaultAsync(w=>w.Id==id&&w.OwnerId==owner,ct) ?? throw Missing();
    }
    public static BusinessException Missing() => new("workflowNotFound",404);
    public static void Revision(WorkflowDefinition row,int revision) { if(row.Revision!=revision)throw new BusinessException("workflowConflict",409);if(row.Archived)throw new BusinessException("workflowArchived",409); }
    public static void Draft(WorkflowSave input)
    {
        if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>100||input.Description is null||input.Description.Length>500||input.Graph is null||input.Graph.SchemaVersion!=1||input.Graph.Policy is null||input.Graph.Nodes is null||input.Graph.Edges is null||input.Graph.Nodes.Count>25||input.Graph.Edges.Count>40
            ||input.Graph.Nodes.Any(n=>n is null||n.Config is null||!WorkflowRegistry.All.Any(t=>t.Type==n.Type))||WorkflowModel.Write(input.Graph).Length>60000)throw new BusinessException("workflowInvalidGraph");
    }
    public async Task<WorkflowDefinition> Save(Guid? id, WorkflowSave input, ClaimsPrincipal user, CancellationToken ct)
    {
        await Check(user,true,ct);Draft(input);var owner=Owner(user);await using var tx=await db.Database.BeginTransactionAsync(ct);
        WorkflowDefinition row;
        if(id is null){var key="workflow-owner:"+owner;await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))",ct);
            if(await db.Set<WorkflowDefinition>().CountAsync(w=>w.OwnerId==owner&&!w.Archived,ct)>=50)throw new BusinessException("workflowLimit",409);
            row=new(){OwnerId=owner};db.Add(row);
        }else{row=await Own(id.Value,user,true,ct);Revision(row,input.Revision);row.Revision++;}
        row.Name=input.Name.Trim();row.Description=input.Description.Trim();row.Draft=WorkflowModel.Write(input.Graph);row.UpdatedAt=DateTimeOffset.UtcNow;
        await Check(user,true,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return row;
    }
    public async Task<WorkflowDefinition> Publish(Guid id,WorkflowCommand input,ClaimsPrincipal user,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);var row=await Own(id,user,true,ct);Revision(row,input.Revision);
        var graph=WorkflowModel.Read<WorkflowGraph>(row.Draft);WorkflowRegistry.Validate(graph);var actor=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==row.OwnerId,ct);
        var version=new WorkflowVersion{WorkflowId=id,Number=(row.PublishedVersion??0)+1,Graph=row.Draft,OwnerRole=actor.Role.ToString(),SecurityVersion=actor.SecurityVersion};db.Add(version);
        row.PublishedVersion=version.Number;row.Enabled=false;row.Revision++;row.UpdatedAt=DateTimeOffset.UtcNow;row.LastSignal=null;row.NextRunAt=null;
        await Check(user,true,ct);db.Add(Audit(row.OwnerId,id,"WorkflowPublish"));await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return row;
    }
    public async Task<ClaimsPrincipal> Identity(WorkflowDefinition row,WorkflowVersion version,CancellationToken ct)
    {
        var actor=await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==row.OwnerId,ct);
        if(actor is null||!actor.IsActive||actor.SecurityVersion!=version.SecurityVersion||actor.Role.ToString()!=version.OwnerRole||actor.Role==UserRole.Viewer)throw new BusinessException("workflowAuthorizationChanged",403);
        var identity=new ClaimsIdentity([new Claim("sub",actor.Id.ToString()),new Claim("role",actor.Role.ToString()),new Claim("sv",actor.SecurityVersion.ToString())],"workflow-authorized-owner","sub","role");
        var user=new ClaimsPrincipal(identity);await Check(user,true,ct);return user;
    }
    public async Task<WorkflowDefinition> Change(Guid id,WorkflowCommand input,ClaimsPrincipal user,bool archive,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);var row=await Own(id,user,true,ct);Revision(row,input.Revision);
        if(input.Enabled&&!archive){if(!options.Enabled)throw new BusinessException("workflowDisabled",503);var version=await Published(row,ct);await Identity(row,version,ct);
            var graph=WorkflowModel.Read<WorkflowGraph>(version.Graph);var start=WorkflowRegistry.Validate(graph)[0];row.NextRunAt=start.Type=="trigger.schedule"?WorkflowRegistry.NextWeekly(start.Config,DateTimeOffset.UtcNow):DateTimeOffset.UtcNow;}
        row.Enabled=!archive&&input.Enabled;row.Archived=archive;row.Revision++;row.LastSignal=null;row.UpdatedAt=DateTimeOffset.UtcNow;
        if(archive)await db.Set<WorkflowRun>().Where(r=>r.WorkflowId==id&&(r.State=="queued"||r.State=="running"||r.State=="waitingApproval")).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.State,"cancelled").SetProperty(r=>r.EndedAt,DateTimeOffset.UtcNow),ct);
        db.Add(Audit(row.OwnerId,id,archive?"WorkflowArchive":row.Enabled?"WorkflowEnable":"WorkflowDisable"));await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return row;
    }
    public async Task<WorkflowVersion> Published(WorkflowDefinition row,CancellationToken ct) => await db.Set<WorkflowVersion>().AsNoTracking().SingleOrDefaultAsync(v=>v.WorkflowId==row.Id&&v.Number==row.PublishedVersion,ct)??throw new BusinessException("workflowNotPublished",409);
    public async Task<WorkflowRun> Enqueue(Guid id,WorkflowRunRequest input,ClaimsPrincipal user,CancellationToken ct,string mode="manual")
    {
        if(!options.Enabled)throw new BusinessException("workflowDisabled",503);if(input.IdempotencyKey==Guid.Empty)throw new BusinessException("workflowInvalidGraph");
        await using var tx=await db.Database.BeginTransactionAsync(ct);var row=await Own(id,user,true,ct);if(row.Archived)throw new BusinessException("workflowArchived",409);
        var existing=await db.Set<WorkflowRun>().AsNoTracking().SingleOrDefaultAsync(r=>r.WorkflowId==id&&r.RequestKey==input.IdempotencyKey,ct);
        var requestedMode=input.Simulation?"simulation":mode;
        if(existing is not null){if(existing.Mode!=requestedMode)throw new BusinessException("workflowConflict",409);await tx.CommitAsync(ct);return existing;}
        if(await db.Set<WorkflowRun>().CountAsync(r=>r.OwnerId==row.OwnerId&&(r.State=="queued"||r.State=="running"||r.State=="waitingApproval"),ct)>=20)throw new BusinessException("workflowLimit",429);
        WorkflowVersion version;
        if(input.Simulation){WorkflowRegistry.Validate(WorkflowModel.Read<WorkflowGraph>(row.Draft));version=await db.Set<WorkflowVersion>().SingleOrDefaultAsync(v=>v.WorkflowId==id&&v.Number==-row.Revision,ct)??new(){WorkflowId=id,Number=-row.Revision,Graph=row.Draft,OwnerRole=user.FindFirstValue("role")!,SecurityVersion=int.Parse(user.FindFirstValue("sv")!)};if(db.Entry(version).State==EntityState.Detached)db.Add(version);}
        else{version=await Published(row,ct);await Identity(row,version,ct);}
        var run=new WorkflowRun{WorkflowId=id,VersionId=version.Id,OwnerId=row.OwnerId,RequestKey=input.IdempotencyKey,Mode=requestedMode};db.Add(run);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return run;
    }
    public async Task<WorkflowRun> OwnRun(Guid id,ClaimsPrincipal user,bool locked,CancellationToken ct)
    {
        await Check(user,locked,ct);var owner=Owner(user);
        return locked?await db.Set<WorkflowRun>().FromSqlInterpolated($"SELECT * FROM \"WorkflowRuns\" WHERE \"Id\"={id} AND \"OwnerId\"={owner} FOR UPDATE").SingleOrDefaultAsync(ct)??throw Missing()
            :await db.Set<WorkflowRun>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id&&r.OwnerId==owner,ct)??throw Missing();
    }
    public async Task Cancel(Guid id,ClaimsPrincipal user,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);var run=await OwnRun(id,user,true,ct);
        if(run.State is "success" or "failed" or "cancelled")throw new BusinessException("workflowConflict",409);
        var outputs=await db.Set<WorkflowNodeRun>().AsNoTracking().Where(n=>n.RunId==id).Select(n=>n.Output).ToListAsync(ct);
        var ids=outputs.Select(json=>WorkflowModel.Read<WorkflowValue>(json).ProposalId).Where(p=>p.HasValue).Select(p=>p!.Value).Distinct().ToArray();
        if(ids.Length>0){await using var scope=scopes.CreateAsyncScope();var approvals=scope.ServiceProvider.GetRequiredService<WorkflowProposals>();foreach(var proposal in await db.Set<AiOrderProposal>().AsNoTracking().Where(p=>ids.Contains(p.Id)&&p.UserId==run.OwnerId&&p.State=="pending").Select(p=>p.Id).ToArrayAsync(ct))await approvals.Cancel(proposal,user,ct);}
        run.State="cancelled";run.EndedAt=DateTimeOffset.UtcNow;run.Error="workflowCancelled";db.Add(Audit(run.OwnerId,run.Id,"WorkflowCancel"));await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<ProposalView> Approve(Guid id,ProposalApproval input,ClaimsPrincipal user,CancellationToken ct)
    {
        // Hold the workflow row against concurrent cancellation; the existing business approval uses its own context/transaction.
        await using var tx=await db.Database.BeginTransactionAsync(ct);var run=await OwnRun(id,user,true,ct);
        var node=await db.Set<WorkflowNodeRun>().SingleOrDefaultAsync(n=>n.RunId==id&&n.Type=="action.requestApproval"&&(n.State=="waitingApproval"||n.State=="success"),ct)??throw new BusinessException("workflowApprovalRequired",409);
        if(run.Mode=="simulation"||run.State is not ("waitingApproval" or "queued" or "success"))throw new BusinessException("workflowConflict",409);
        var row=await Own(run.WorkflowId,user,false,ct);if(row.Archived)throw new BusinessException("workflowArchived",409);var version=await db.Set<WorkflowVersion>().SingleAsync(v=>v.Id==run.VersionId,ct);await Identity(row,version,ct);
        var value=WorkflowModel.Read<WorkflowValue>(node.Output);if(value.ProposalId is null)throw new BusinessException("workflowApprovalRequired",409);
        await using var scope=scopes.CreateAsyncScope();var result=await scope.ServiceProvider.GetRequiredService<WorkflowProposals>().Approve(value.ProposalId.Value,input,user,ct);
        if(!result.ResultVerified||result.State!="approved")throw new BusinessException("workflowApprovalRequired",409);
        node.Output=WorkflowModel.Write(value with{Approved=true,Data=System.Text.Json.JsonSerializer.SerializeToElement(result,WorkflowModel.Json)});node.State="success";node.EndedAt=DateTimeOffset.UtcNow;
        if(run.State=="waitingApproval")run.State="queued";await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return result;
    }
    private static AuditLog Audit(Guid owner,Guid id,string action)=>new(){UserId=owner,UserName="Authorized workflow owner",Action=action,EntityName="WorkflowDefinition",EntityId=id,Description="Explicit workflow lifecycle action"};
}
