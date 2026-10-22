using System.Text;
using System.Threading.RateLimiting;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Demo.Api.Services;
using Demo.Api.Swagger;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false))).ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors
                .Select(error => error.Exception is not null || entry.Key.StartsWith('$')
                    ? "The request body contains a missing or invalid value."
                    : error.ErrorMessage).ToArray());
        var problem = new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(errors)
        { Status = 400, Title = "Validation failed" };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(problem);
    };
});
builder.Services.AddValidatorsFromAssemblyContaining<RegisterValidator>();
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings__Default must be configured.")));
builder.Services.AddOptions<JwtOptions>().BindConfiguration("Jwt")
    .Validate(jwt => !string.IsNullOrEmpty(jwt.Secret) && Encoding.UTF8.GetByteCount(jwt.Secret) >= 32
        && !string.IsNullOrWhiteSpace(jwt.Issuer) && !string.IsNullOrWhiteSpace(jwt.Audience)
        && jwt.LifetimeMinutes is >= 1 and <= 1440,
        "Configure Jwt Secret (at least 32 bytes), Issuer, Audience and LifetimeMinutes (1-1440).")
    .Validate(jwt => builder.Environment.IsDevelopment()
        || !jwt.Secret.StartsWith("development-only", StringComparison.Ordinal),
        "Replace the development JWT placeholder outside Development.")
    .ValidateOnStart();
builder.Services.AddSingleton<TokenService>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddSingleton(sp => Demo.Api.AI.AiAssistantOptions.Read(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));
builder.Services.AddSingleton<Demo.Api.AI.AiRequestQuota>();
builder.Services.AddScoped<Demo.Api.AI.AiResponseDiagnostics>();
builder.Services.AddHttpClient<Demo.Api.AI.OpenAiModelClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddHttpClient<Demo.Api.AI.OllamaModelClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
builder.Services.AddScoped<Demo.Api.AI.IAiModelClient>(sp => new Demo.Api.AI.Quality.ObservedModelClient(sp.GetRequiredService<Demo.Api.AI.AiAssistantOptions>().Provider switch {
    "Mock" => new Demo.Api.AI.DevelopmentAiModelClient(),
    "Ollama" => sp.GetRequiredService<Demo.Api.AI.OllamaModelClient>(),
    _ => sp.GetRequiredService<Demo.Api.AI.OpenAiModelClient>() }, sp.GetRequiredService<Demo.Api.AI.Quality.QualityTelemetry>(), sp.GetRequiredService<Demo.Api.AI.AiAssistantOptions>()));
builder.Services.AddScoped<Demo.Api.AI.OrderDraftAgent>();
builder.Services.AddSingleton<Demo.Api.AI.AiExecutionPolicy>();
builder.Services.AddScoped<Demo.Api.AI.AiConversationStore>();
builder.Services.AddScoped<Demo.Api.AI.AiExecutionJournal>();
builder.Services.AddHostedService<Demo.Api.AI.AiRetentionWorker>();
builder.Services.AddScoped<Demo.Api.AI.AiToolExecutor>();
builder.Services.AddScoped<Demo.Api.AI.AiAssistantService>();
builder.Services.AddScoped<Demo.Api.AI.Agents.AgentRunStore>();
builder.Services.AddScoped<Demo.Api.AI.Agents.InventoryAnalysisAgent>();
builder.Services.AddScoped<Demo.Api.AI.Agents.SalesAnalysisAgent>();
builder.Services.AddScoped<Demo.Api.AI.Agents.IOrderDraftReader, Demo.Api.AI.Agents.OrderDraftReader>();
builder.Services.AddScoped<Demo.Api.AI.Agents.IntelligentOrderDraftAgent>();
builder.Services.AddScoped<Demo.Api.AI.Agents.AgentOrchestrator>();
builder.Services.AddScoped<Demo.Api.AI.Agents.WorkflowProposals>();
builder.Services.AddSingleton(sp=>Demo.Api.AI.Knowledge.KnowledgeOptions.Read(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient<Demo.Api.AI.Knowledge.OllamaEmbeddings>(client=>client.Timeout=Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(()=>new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false});
builder.Services.AddScoped<Demo.Api.AI.Knowledge.IKnowledgeEmbeddingProvider>(sp=>sp.GetRequiredService<Demo.Api.AI.Knowledge.OllamaEmbeddings>());
builder.Services.AddScoped<Demo.Api.AI.IEmbeddingClient>(sp=>sp.GetRequiredService<Demo.Api.AI.Knowledge.OllamaEmbeddings>());
builder.Services.AddScoped<Demo.Api.AI.Knowledge.IDocumentAuthorization,Demo.Api.AI.Knowledge.DocumentAuthorization>();
builder.Services.AddScoped<Demo.Api.AI.Knowledge.IDocumentExtractor,Demo.Api.AI.Knowledge.DocumentExtractor>();
builder.Services.AddScoped<Demo.Api.AI.Knowledge.IKnowledgeChunker,Demo.Api.AI.Knowledge.KnowledgeChunker>();
builder.Services.AddScoped<Demo.Api.AI.Knowledge.IKnowledgeVectorStore,Demo.Api.AI.Knowledge.PgKnowledgeVectorStore>();
builder.Services.AddScoped<Demo.Api.AI.Knowledge.KnowledgeDocuments>();
builder.Services.AddScoped<Demo.Api.AI.Knowledge.IKnowledgeIngestion>(sp=>sp.GetRequiredService<Demo.Api.AI.Knowledge.KnowledgeDocuments>());
builder.Services.AddScoped<Demo.Api.AI.Knowledge.KnowledgeIndexer>();
builder.Services.AddScoped<Demo.Api.AI.Knowledge.KnowledgeRetriever>();
builder.Services.AddScoped<Demo.Api.AI.Knowledge.IKnowledgeRetriever>(sp=>sp.GetRequiredService<Demo.Api.AI.Knowledge.KnowledgeRetriever>());
builder.Services.AddScoped<Demo.Api.AI.IRagRetriever>(sp=>sp.GetRequiredService<Demo.Api.AI.Knowledge.KnowledgeRetriever>());
builder.Services.AddScoped<Demo.Api.AI.Knowledge.IKnowledgeContextBuilder,Demo.Api.AI.Knowledge.KnowledgeContextBuilder>();
builder.Services.AddScoped<Demo.Api.AI.Knowledge.IKnowledgeGrounding,Demo.Api.AI.Knowledge.KnowledgeGrounding>();
builder.Services.AddScoped<Demo.Api.AI.Knowledge.KnowledgeAgent>();
builder.Services.AddHostedService<Demo.Api.AI.Knowledge.KnowledgeWorker>();
builder.Services.AddSingleton(new Demo.Api.AI.Knowledge.LocalModelGate(builder.Configuration.GetValue("LocalInference:Concurrency", 1), builder.Configuration.GetValue("LocalInference:QueueLimit", 8)));
builder.Services.AddSingleton(sp => Demo.Api.AI.Monitoring.MonitoringOptions.Read(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<Demo.Api.AI.Monitoring.IMonitoringAnalysis, Demo.Api.AI.Monitoring.MonitoringAnalysisService>();
builder.Services.AddScoped<Demo.Api.AI.Monitoring.OperationalAlerts>();
builder.Services.AddScoped<Demo.Api.AI.Monitoring.IOperationalNotifications>(sp => sp.GetRequiredService<Demo.Api.AI.Monitoring.OperationalAlerts>());
builder.Services.AddScoped<Demo.Api.AI.Monitoring.MonitoringEngine>();
builder.Services.AddHostedService<Demo.Api.AI.Monitoring.MonitoringWorker>();
builder.Services.AddSingleton(sp => Demo.Api.AI.Quality.QualityOptions.Read(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<Demo.Api.AI.Quality.QualityTelemetry>();
builder.Services.AddScoped<Demo.Api.AI.Quality.QualityRequestFilter>();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(mvc => mvc.Filters.AddService<Demo.Api.AI.Quality.QualityRequestFilter>());
builder.Services.AddHostedService<Demo.Api.AI.Quality.QualityTelemetryWorker>();
builder.Services.AddSingleton<Demo.Api.AI.Quality.EvaluationCatalog>();
builder.Services.AddScoped<Demo.Api.AI.Quality.EvaluationEngine>();
builder.Services.AddSingleton(sp => Demo.Api.AI.Workflows.WorkflowOptions.Read(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<Demo.Api.AI.Workflows.WorkflowService>();
builder.Services.AddScoped<Demo.Api.AI.Workflows.WorkflowHandlers>();
builder.Services.AddScoped<Demo.Api.AI.Workflows.IWorkflowHandlers>(sp => sp.GetRequiredService<Demo.Api.AI.Workflows.WorkflowHandlers>());
builder.Services.AddScoped<Demo.Api.AI.Workflows.WorkflowEngine>();
builder.Services.AddScoped<Demo.Api.AI.Workflows.WorkflowDrafting>();
builder.Services.AddHostedService<Demo.Api.AI.Workflows.WorkflowWorker>();

builder.Services.AddHttpClient("OllamaHealth",client=>client.Timeout=Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(()=>new SocketsHttpHandler {AllowAutoRedirect=false,UseProxy=false});
builder.Services.AddSingleton(sp=>new Demo.Api.AI.Agents.AgentHealth(sp.GetRequiredService<Demo.Api.AI.AiAssistantOptions>(),
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("OllamaHealth")));

builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<ProcurementService>();
builder.Services.AddScoped<WarehouseService>();
builder.Services.AddScoped<TransferService>();
builder.Services.AddScoped<ManufacturingService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AdminBootstrap>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, configured) =>
{
    var jwt = configured.Value;
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateLifetime = true, RequireExpirationTime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero,
        RoleClaimType = "role", NameClaimType = "name"
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var id)
                || !int.TryParse(context.Principal?.FindFirst("sv")?.Value, out var version))
            { context.Fail("Invalid session"); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().Where(u => u.Id == id)
                .Select(u => new { u.IsActive, u.SecurityVersion, u.Role, u.Name }).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (user is null || !user.IsActive || user.SecurityVersion != version
                || context.Principal?.FindFirst("role")?.Value != user.Role.ToString())
            { context.Fail("Revoked session"); return; }
            ((System.Security.Claims.ClaimsIdentity)context.Principal!.Identity!).AddClaim(new("name", user.Name));
        }
    };
});
builder.Services.AddAuthorization(AccessPolicies.Register);
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("knowledge-ingestion", _ => RateLimitPartition.GetConcurrencyLimiter("knowledge-ingestion", _ => new ConcurrencyLimiterOptions { PermitLimit = 2, QueueLimit = 0 }));
    options.AddPolicy("monitoring-actions", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("monitoring-reviews", _ => RateLimitPartition.GetConcurrencyLimiter("monitoring-reviews", _ => new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 }));
    options.AddPolicy("quality-evaluation", _ => RateLimitPartition.GetConcurrencyLimiter("quality-evaluation", _ => new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
        await Results.Problem(statusCode: 429, title: context.HttpContext.Request.Path.StartsWithSegments("/api/knowledge") ? "Knowledge ingestion busy" : context.HttpContext.Request.Path.StartsWithSegments("/api/operations") ? "Monitoring capacity busy" : context.HttpContext.Request.Path.StartsWithSegments("/api/quality") ? "AI evaluation capacity busy" : "Too many authentication attempts",
            detail: "Wait a minute and try again.").ExecuteAsync(context.HttpContext);
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Demo catalog API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Paste the access token returned by login." });
    options.OperationFilter<BearerOperationFilter>();
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    await Results.Problem(statusCode: response.StatusCode).ExecuteAsync(context.HttpContext);
});
app.UseCors();
app.UseMiddleware<Demo.Api.AI.Quality.QualityHttpMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).ExcludeFromDescription();
// Preserve the legacy health contract while bounding dependency checks.
app.MapGet("/health", Readiness).ExcludeFromDescription();
app.MapGet("/health/ready", Readiness).ExcludeFromDescription();

static async Task<IResult> Readiness(AppDbContext db, CancellationToken ct)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(2));
    try
    {
        if (await db.Database.CanConnectAsync(timeout.Token)) return Results.Ok(new { status = "healthy" });
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    return Results.Problem(statusCode: 503, title: "Database unavailable");
}

try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var attempt = 1; ; attempt++)
        {
            try { await db.Database.MigrateAsync(); break; }
            catch (Exception ex) when (attempt < 10)
            {
                app.Logger.LogWarning(ex, "Database migration attempt {Attempt} failed; retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
        await scope.ServiceProvider.GetRequiredService<AdminBootstrap>().Run(CancellationToken.None);
        app.Logger.LogInformation("Database migrations applied; starting API");
    }
    await app.RunAsync();
}

catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Application startup or database migration failed");
    throw;
}

public partial class Program { }
