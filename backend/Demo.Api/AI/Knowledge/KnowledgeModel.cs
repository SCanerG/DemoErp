using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.AI.Knowledge;

public sealed class KnowledgeOptions
{
    public bool Enabled {get;set;}
    public string EmbeddingModel {get;set;}="nomic-embed-text";
    public int MaxFileBytes {get;set;}=5*1024*1024;
    public int MaxCharacters {get;set;}=200000;
    public int ChunkCharacters {get;set;}=1200;
    public int OverlapCharacters {get;set;}=150;
    public int MaxChunks {get;set;}=300;
    public int TopK {get;set;}=5;
    public int ContextCharacters {get;set;}=7000;
    public int TimeoutSeconds {get;set;}=90;
    public static KnowledgeOptions Read(IConfiguration config) {
        var o=new KnowledgeOptions();config.GetSection("Knowledge").Bind(o);
        if(o.MaxFileBytes is <1024 or >10485760||o.MaxCharacters is <1000 or >500000||o.ChunkCharacters is <300 or >2400
            ||o.OverlapCharacters<0||o.OverlapCharacters>=o.ChunkCharacters/2||o.MaxChunks is <1 or >500||o.TopK is <1 or >8
            ||o.ContextCharacters is <1000 or >10000||o.TimeoutSeconds is <5 or >300||o.EmbeddingModel.Length is <1 or >100)throw new InvalidOperationException("Invalid knowledge limits.");
        return o;
    }
    public void Check(){if(!Enabled)throw new BusinessException("knowledgeDisabled",503);}
}
public sealed class KnowledgeDocument {
    public Guid Id {get;set;}=Guid.NewGuid();public Guid OwnerId {get;set;}public string Scope {get;set;}="private";
    public string FileName {get;set;}="";public int Version {get;set;}=1;public string Status {get;set;}="queued";
    public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;public DateTimeOffset UpdatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public sealed class KnowledgeVersion {
    public Guid Id {get;set;}=Guid.NewGuid();public Guid DocumentId {get;set;}public int Number {get;set;}
    public byte[] Content {get;set;}=[];public string Hash {get;set;}="";public string FileName {get;set;}="";public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public sealed class KnowledgeChunk {
    public Guid Id {get;set;}=Guid.NewGuid();public Guid DocumentId {get;set;}public int Version {get;set;}public int Position {get;set;}
    public string Text {get;set;}="";public string Reference {get;set;}="";public string Model {get;set;}="";public string ModelDigest {get;set;}="";
    public int Dimensions {get;set;}public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public sealed class KnowledgeJob {
    public Guid Id {get;set;}=Guid.NewGuid();public Guid DocumentId {get;set;}public int Version {get;set;}public string State {get;set;}="queued";
    public int Attempts {get;set;}public int Progress {get;set;}public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt {get;set;}public DateTimeOffset? EndedAt {get;set;}public string? Error {get;set;}public string? Lease {get;set;}
}
public static class KnowledgeDbModel {
    public static void Configure(ModelBuilder m) {
        m.Entity<KnowledgeDocument>(e=>{e.ToTable("KnowledgeDocuments");e.HasKey(x=>x.Id);e.Property(x=>x.Scope).HasMaxLength(16);e.Property(x=>x.Status).HasMaxLength(32);e.Property(x=>x.FileName).HasMaxLength(160);e.HasIndex(x=>new{x.OwnerId,x.Scope});e.HasOne<User>().WithMany().HasForeignKey(x=>x.OwnerId).OnDelete(DeleteBehavior.Restrict);});
        m.Entity<KnowledgeVersion>(e=>{e.ToTable("KnowledgeVersions");e.HasKey(x=>x.Id);e.HasIndex(x=>new{x.DocumentId,x.Number}).IsUnique();e.Property(x=>x.Hash).HasMaxLength(64);e.Property(x=>x.FileName).HasMaxLength(160);e.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(x=>x.DocumentId).OnDelete(DeleteBehavior.Cascade);});
        m.Entity<KnowledgeChunk>(e=>{e.ToTable("KnowledgeChunks");e.HasKey(x=>x.Id);e.HasIndex(x=>new{x.DocumentId,x.Version,x.Position}).IsUnique();e.Property(x=>x.Text).HasMaxLength(2400);e.Property(x=>x.Reference).HasMaxLength(160);e.Property(x=>x.Model).HasMaxLength(100);e.Property(x=>x.ModelDigest).HasMaxLength(128);e.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(x=>x.DocumentId).OnDelete(DeleteBehavior.Cascade);});
        m.Entity<KnowledgeJob>(e=>{e.ToTable("KnowledgeJobs");e.HasKey(x=>x.Id);e.Property(x=>x.State).HasMaxLength(32);e.Property(x=>x.Error).HasMaxLength(64);e.Property(x=>x.Lease).HasMaxLength(64);e.HasIndex(x=>new{x.State,x.CreatedAt});e.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(x=>x.DocumentId).OnDelete(DeleteBehavior.Cascade);});
    }
}
public sealed record DocumentView(Guid Id,string FileName,string Scope,int Version,string Status,DateTimeOffset UpdatedAt,int Progress,string? Error,int ChunkCount,bool CanManage=false,string? EmbeddingModel=null,int? Dimensions=null);
public sealed record VersionView(int Number,string FileName,string Hash,DateTimeOffset CreatedAt);
public sealed record ExtractedSection(string Reference,string Text);
public sealed record TextChunk(int Position,string Reference,string Text);
public sealed record EmbeddingBatch(string Model,string Digest,int Dimensions,List<float[]> Vectors,long DurationMs);
public sealed record KnowledgePassage(Guid ChunkId,Guid DocumentId,int Version,string FileName,string Reference,string Text,double Score);
public sealed record KnowledgeCitation(int Number,Guid DocumentId,int Version,Guid ChunkId,string FileName,string Reference,string Quote);
public sealed record KnowledgeAnswer(string Answer,string Language,string ResponseKind,List<KnowledgeCitation> Citations,bool InsufficientEvidence,long RetrievalMs,long LlmMs,int? TotalTokens,string EmbeddingModel,int Dimensions);
public sealed record KnowledgeQuestion(string Message,string Language="tr",Guid? DocumentId=null);
