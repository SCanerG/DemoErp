using Demo.Api.AI;
using Demo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Data;
public static class AiModel
{
    public static void Configure(ModelBuilder model)
    {
        Demo.Api.AI.Agents.AgentDbModel.Configure(model);
        model.Entity<AiConversation>(e => {
            e.ToTable("AiConversations", t => t.HasCheckConstraint("CK_AiConversations_Revision", "\"Revision\" >= 0 AND \"Revision\" <= 10"));
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.UserId, x.ExpiresAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AiConversationMessage>(e => {
            e.ToTable("AiConversationMessages", t => {
                t.HasCheckConstraint("CK_AiMessages_Role", "\"Role\" IN ('user','assistant')");
                t.HasCheckConstraint("CK_AiMessages_Sequence", "\"Sequence\" >= 0 AND \"Sequence\" < 20");
            });
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique();
            e.Property(x => x.Role).HasMaxLength(16); e.Property(x => x.Language).HasMaxLength(2);
            e.Property(x => x.Content).HasMaxLength(8000); e.Property(x => x.SourcesJson).HasColumnType("jsonb");
            e.HasOne<AiConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<AiExecution>(e => {
            e.ToTable("AiExecutions"); e.HasKey(x => x.Id); e.HasIndex(x => x.ExpiresAt); e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.Property(x => x.Provider).HasMaxLength(32); e.Property(x => x.Model).HasMaxLength(100); e.Property(x => x.Outcome).HasMaxLength(64);
            e.Property(x => x.CorrelationId).HasMaxLength(128);
            e.Property(x => x.EstimatedCost).HasPrecision(18, 8); e.Property(x => x.CostCurrency).HasMaxLength(3);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AiConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
        });
        model.Entity<AiToolAudit>(e => {
            e.ToTable("AiToolAudits"); e.HasKey(x => x.Id); e.HasIndex(x => x.ExecutionId);
            e.Property(x => x.ToolName).HasMaxLength(64); e.Property(x => x.Outcome).HasMaxLength(64);
            e.HasOne<AiExecution>().WithMany().HasForeignKey(x => x.ExecutionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
