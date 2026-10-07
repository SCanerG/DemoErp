using Demo.Api.Domain;
using FluentValidation;
using System.Text.Json.Serialization;

namespace Demo.Api.Contracts;
public sealed record CategoryRequest(string Name, string Description, [property: JsonRequired] bool IsActive);
public sealed record CategoryResponse(Guid Id, string Name, string Description, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, int ProductCount);
public sealed record CustomerRequest(string Name, string Email, string Phone, string Address, [property: JsonRequired] bool IsActive);
public sealed record CustomerResponse(Guid Id, string Name, string Email, string Phone, string Address, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, int OrderCount);
public sealed record OrderItemRequest(Guid ProductId, int Quantity);
public sealed record OrderRequest(Guid CustomerId, List<OrderItemRequest> Items);
public sealed record OrderStatusRequest([property: JsonRequired] OrderStatus Status);
public sealed record OrderSummary(Guid Id, string OrderNumber, Guid CustomerId, string CustomerName, OrderStatus Status, DateTimeOffset OrderDate, decimal TotalAmount, int ItemCount);
public sealed record OrderItemResponse(Guid Id, Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record OrderResponse(Guid Id, string OrderNumber, Guid CustomerId, string CustomerName, OrderStatus Status, DateTimeOffset OrderDate, decimal TotalAmount, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, List<OrderItemResponse> Items);
public sealed class CategoryValidator : AbstractValidator<CategoryRequest>
{
    public CategoryValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(150); RuleFor(x => x.Description).NotNull().MaximumLength(2000); }
}
public sealed class CustomerValidator : AbstractValidator<CustomerRequest>
{
    public CustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotNull().MaximumLength(254).Must(value => string.IsNullOrEmpty(value) || EmailValidation.IsAddress(value)).WithMessage("Email must be a valid address.");
        RuleFor(x => x.Phone).NotNull().MaximumLength(40);
        RuleFor(x => x.Address).NotNull().MaximumLength(1000);
    }
}
public sealed class OrderValidator : AbstractValidator<OrderRequest>
{
    public OrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().Must(items => items is null || items.Count <= 100).WithMessage("Use no more than 100 order lines.");
        RuleForEach(x => x.Items).NotNull().ChildRules(item => { item.RuleFor(x => x.ProductId).NotEmpty(); item.RuleFor(x => x.Quantity).InclusiveBetween(1, 100000); });
        RuleFor(x => x.Items).Must(items => items is null || items.Where(x => x is not null).Select(x => x.ProductId).Distinct().Count() == items.Count).WithMessage("Select each product only once.");
    }
}
public sealed class OrderStatusValidator : AbstractValidator<OrderStatusRequest>
{
    public OrderStatusValidator() { RuleFor(x => x.Status).IsInEnum(); }
}
