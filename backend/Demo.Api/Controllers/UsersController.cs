using Demo.Api.Contracts;
using Demo.Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

[ApiController, Authorize(Policy = AccessPolicies.UserManage), Route("api/users")]
public sealed class UsersController(UserService users, IValidator<CreateUserRequest> createValidator,
    IValidator<UpdateUserRequest> updateValidator, IValidator<UserRoleRequest> roleValidator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserResponse>>> List(CancellationToken ct) => Ok(await users.List(ct));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken ct) => Result(await users.Get(id, ct));
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    { await createValidator.ValidateAndThrowAsync(request, ct); var value = await users.Create(request, ct); return CreatedAtAction(nameof(Get), new { id = value.Id }, value); }
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    { await updateValidator.ValidateAndThrowAsync(request, ct); return Result(await users.Update(id, request, null, null, ct)); }
    [HttpPut("{id:guid}/role")]
    public async Task<ActionResult<UserResponse>> Role(Guid id, UserRoleRequest request, CancellationToken ct)
    { await roleValidator.ValidateAndThrowAsync(request, ct); return Result(await users.Update(id, null, request.Role, null, ct)); }
    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<UserResponse>> Status(Guid id, UserStatusRequest request, CancellationToken ct) => Result(await users.Update(id, null, null, request.IsActive, ct));
    private ActionResult<UserResponse> Result(UserResponse? value) => value is null ? Problem(statusCode: 404, title: "Record not found") : Ok(value);
}
