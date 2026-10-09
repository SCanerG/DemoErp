using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Demo.Api.Services;

public sealed class AuthService(AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens)
{
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<UserResponse?> Register(RegisterRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(x => x.Email == email, ct)) return null;
        var user = new User { Name = request.Name.Trim(), Email = email };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Users_Email" })
        {
            // The unique index remains authoritative for concurrent registrations.
            db.Entry(user).State = EntityState.Detached;
            return null;
        }
        return new UserResponse(user.Id, user.Name, user.Email, user.Role, user.IsActive, user.CreatedAt, user.UpdatedAt);
    }

    public async Task<LoginResponse?> Login(LoginRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
        if (user is null)
        {
            // Spend the same PBKDF2 work for an unknown account to reduce timing-based enumeration.
            _ = hasher.HashPassword(new User(), request.Password);
            return null;
        }
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed || !user.IsActive) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }
        return tokens.Create(user);
    }
}
