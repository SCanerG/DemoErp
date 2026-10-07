using FluentValidation;
using System.Text.Json.Serialization;
using System.Net.Mail;

namespace Demo.Api.Contracts;

public sealed record RegisterRequest(string Name, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record ProductRequest(string Name, string Description,
    [property: JsonRequired] decimal Price, [property: JsonRequired] bool IsActive, Guid CategoryId = default);
public sealed record UserResponse(Guid Id, string Name, string Email);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);
public sealed record ProductResponse(Guid Id, string Name, string Description, decimal Price,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, Guid CategoryId, string CategoryName);

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254)
            .Must(EmailValidation.IsAddress).WithMessage("Email must be a valid address without a display name.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(128);
    }
}

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254)
            .Must(EmailValidation.IsAddress).WithMessage("Email must be a valid address without a display name.");
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

internal static class EmailValidation
{
    public static bool IsAddress(string? value) => value is not null
        && MailAddress.TryCreate(value.Trim(), out var address)
        && string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed class ProductValidator : AbstractValidator<ProductRequest>
{
    public ProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Description).NotNull().MaximumLength(2000);
        RuleFor(x => x.Price).InclusiveBetween(0m, 9999999999.99m).PrecisionScale(12, 2, true);
    }
}
