using System.Linq.Expressions;
using System.Security.Claims;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Services;

public sealed class UserService(AppDbContext db, IPasswordHasher<User> hasher, IHttpContextAccessor http)
{
    private static readonly Expression<Func<User, UserResponse>> Projection = u => new(u.Id, u.Name, u.Email, u.Role, u.IsActive, u.CreatedAt, u.UpdatedAt);
    public Task<List<UserResponse>> List(CancellationToken ct) => db.Users.AsNoTracking().OrderBy(u => u.Name).ThenBy(u => u.Id).Select(Projection).ToListAsync(ct);
    public Task<UserResponse?> Get(Guid id, CancellationToken ct) => db.Users.AsNoTracking().Where(u => u.Id == id).Select(Projection).SingleOrDefaultAsync(ct);
    internal static Task Lock(AppDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7419202604)", ct);

    private async Task<Guid> CheckActor(CancellationToken ct)
    {
        var principal = http.HttpContext?.User;
        if (!Guid.TryParse(principal?.FindFirstValue("sub"), out var id)
            || !int.TryParse(principal.FindFirstValue("sv"), out var version)
            || !await db.Users.AnyAsync(u => u.Id == id && u.IsActive && u.Role == UserRole.Admin && u.SecurityVersion == version, ct))
            throw new BusinessException("invalidUser", 401);
        return id;
    }
    private async Task EmailAvailable(string email, Guid? id, CancellationToken ct)
    { if (await db.Users.AnyAsync(u => u.Email == email && u.Id != id, ct)) throw new BusinessException("duplicateEmail", 409); }

    public async Task<UserResponse> Create(CreateUserRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, ct); await CheckActor(ct);
        var email = request.Email.Trim().ToLowerInvariant(); await EmailAvailable(email, null, ct);
        var user = new User { Name = request.Name.Trim(), Email = email, Role = request.Role };
        user.PasswordHash = hasher.HashPassword(user, request.Password); db.Users.Add(user);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return (await Get(user.Id, ct))!;
    }
    public async Task<UserResponse?> Update(Guid id, UpdateUserRequest? request, UserRole? role, bool? active, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // One database lock serializes all admin/security changes across API instances.
        await Lock(db, ct); var actor = await CheckActor(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return null;
        if (actor == id && role.HasValue) throw new BusinessException("cannotChangeOwnRole", 409);
        if (actor == id && active == false) throw new BusinessException("cannotDeactivateSelf", 409);
        if (user.IsActive && user.Role == UserRole.Admin && (active == false || role.HasValue && role != UserRole.Admin)
            && await db.Users.CountAsync(u => u.IsActive && u.Role == UserRole.Admin, ct) <= 1)
            throw new BusinessException("lastActiveAdmin", 409);
        var securityChanged = role.HasValue && role != user.Role || active.HasValue && active != user.IsActive;
        if (request is not null)
        {
            var email = request.Email.Trim().ToLowerInvariant(); await EmailAvailable(email, id, ct);
            securityChanged |= email != user.Email;
            user.Name = request.Name.Trim(); user.Email = email;
        }
        if (role.HasValue) user.Role = role.Value;
        if (active.HasValue) user.IsActive = active.Value;
        if (securityChanged) user.SecurityVersion = checked(user.SecurityVersion + 1);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Get(id, ct);
    }
}

public sealed class AdminBootstrap(AppDbContext db, IConfiguration config, IPasswordHasher<User> hasher)
{
    public async Task Run(CancellationToken ct)
    {
        if (!config.GetValue<bool>("Bootstrap:Enabled")) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await UserService.Lock(db, ct);
        if (await db.Users.AnyAsync(u => u.IsBootstrapAccount || u.Role == UserRole.Admin, ct)) return;
        var name = config["Bootstrap:Name"] ?? "";
        var email = (config["Bootstrap:Email"] ?? "").Trim().ToLowerInvariant();
        var password = config["Bootstrap:Password"] ?? "";
        var valid = await new RegisterValidator().ValidateAsync(new RegisterRequest(name, email, password), ct);
        if (!valid.IsValid) throw new InvalidOperationException("Bootstrap requires a valid name, email and password of 12-128 characters.");
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new InvalidOperationException("Bootstrap account conflicts with an existing account; no account was promoted.");
        var user = new User { Name = name.Trim(), Email = email, Role = UserRole.Admin, IsBootstrapAccount = true };
        user.PasswordHash = hasher.HashPassword(user, password); db.Users.Add(user);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
