using System.Text.Json.Serialization;
using Demo.Api.Domain;
using FluentValidation;

namespace Demo.Api.Contracts;

public sealed record CreateUserRequest(string Name, string Email, string Password, [property: JsonRequired] UserRole Role);
public sealed record UpdateUserRequest(string Name, string Email);
public sealed record UserRoleRequest([property: JsonRequired] UserRole Role);
public sealed record UserStatusRequest([property: JsonRequired] bool IsActive);
public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => new RegisterRequest(x.Name, x.Email, x.Password)).SetValidator(new RegisterValidator());
        RuleFor(x => x.Role).IsInEnum();
    }
}
public sealed class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).Must(EmailValidation.IsAddress);
    }
}
public sealed class UserRoleValidator : AbstractValidator<UserRoleRequest>
{ public UserRoleValidator() => RuleFor(x => x.Role).IsInEnum(); }

public sealed record AuditResponse(Guid Id, Guid? UserId, string UserName, string Action, string EntityName,
    Guid EntityId, System.Text.Json.JsonElement? OldValues, System.Text.Json.JsonElement? NewValues,
    string Description, DateTimeOffset CreatedAt, string? CorrelationId);
public sealed record AuditPage(List<AuditResponse> Items, int Page, int PageSize, int TotalCount);
