using System.Text.Json.Serialization;
using Demo.Api.Domain;
using Demo.Api.Errors;
using FluentValidation;

namespace Demo.Api.Contracts;

public sealed record SupplierRequest(string SupplierCode, string Name, string ContactPerson, string Email, string Phone, string Address, string TaxNumber);
public sealed record SupplierStatusRequest([property: JsonRequired] bool IsActive);
public sealed record SupplierResponse(Guid Id, string SupplierCode, string Name, string ContactPerson, string Email, string Phone, string Address, string TaxNumber, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
public sealed record PurchaseItemRequest(Guid ProductId, int QuantityOrdered, [property: JsonRequired] decimal UnitCost);
public sealed record PurchaseRequest(Guid SupplierId, DateTimeOffset? ExpectedDeliveryDate, string Notes, List<PurchaseItemRequest> Items);
public sealed record PurchaseItemResponse(Guid Id, Guid ProductId, string ProductName, int QuantityOrdered, int QuantityReceived, int QuantityRemaining, decimal UnitCost, decimal LineTotal);
public sealed record PurchaseSummary(Guid Id, string PurchaseOrderNumber, Guid SupplierId, string SupplierName, PurchaseOrderStatus Status, DateTimeOffset OrderDate,
    DateTimeOffset? ExpectedDeliveryDate, decimal TotalAmount, long OrderedQuantity, long ReceivedQuantity, long RemainingQuantity);
public sealed record PurchaseResponse(Guid Id, string PurchaseOrderNumber, Guid SupplierId, string SupplierName, PurchaseOrderStatus Status, DateTimeOffset OrderDate,
    DateTimeOffset? ExpectedDeliveryDate, decimal TotalAmount, string Notes, Guid CreatedByUserId, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, List<PurchaseItemResponse> Items);
public sealed record ReceiptItemRequest(Guid PurchaseOrderItemId, int QuantityReceived);
public sealed record ReceiptRequest(List<ReceiptItemRequest> Items, string Notes, Guid? WarehouseId = null);
public sealed record ReceiptSummary(Guid Id, string ReceiptNumber, DateTimeOffset ReceivedAt, string CreatedBy, int ItemCount, long TotalReceivedQuantity);
public sealed record ReceiptItemResponse(Guid Id, Guid PurchaseOrderItemId, Guid ProductId, string ProductName, int QuantityReceived);
public sealed record ReceiptResponse(Guid Id, string ReceiptNumber, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, string SupplierName,
    DateTimeOffset ReceivedAt, string Notes, Guid CreatedByUserId, string CreatedBy, List<ReceiptItemResponse> Items, Guid WarehouseId = default, string WarehouseName = "");
public sealed record ProcurementSummary(int OpenPurchaseOrders, int PartiallyReceivedOrders, int ReceivedOrders, int ActiveSuppliers, long OutstandingQuantity);

public sealed class SupplierFilter
{
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string Sort { get; set; } = "name";
    public bool Descending { get; set; }
    public void Validate()
    {
        if (Page is < 1 or > 1000000 || PageSize is < 1 or > 100 || Search?.Length > 150 || Sort is not ("name" or "supplierCode")) throw new BusinessException("invalidProcurementFilters");
    }
}
public sealed class PurchaseFilter
{
    public string? Search { get; set; }
    public Guid? SupplierId { get; set; }
    public PurchaseOrderStatus? Status { get; set; }
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string Sort { get; set; } = "orderDate";
    public bool Descending { get; set; } = true;
    public void Validate()
    {
        Start = Start?.ToUniversalTime(); End = End?.ToUniversalTime();
        if (Page is < 1 or > 1000000 || PageSize is < 1 or > 100 || Search?.Length > 150
            || Status.HasValue && !Enum.IsDefined(Status.Value) || Start.HasValue != End.HasValue
            || Start >= End || End - Start > TimeSpan.FromDays(366) || Sort is not ("orderDate" or "purchaseOrderNumber" or "totalAmount" or "supplier")) throw new BusinessException("invalidProcurementFilters");
    }
}
public sealed class SupplierValidator : AbstractValidator<SupplierRequest>
{
    public SupplierValidator()
    {
        RuleFor(x => x.SupplierCode).NotEmpty().MaximumLength(40).Matches(@"^\s*[A-Za-z0-9][A-Za-z0-9_\-]*\s*$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ContactPerson).NotNull().MaximumLength(150);
        RuleFor(x => x.Email).NotNull().MaximumLength(254);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).NotNull().MaximumLength(40);
        RuleFor(x => x.Address).NotNull().MaximumLength(1000);
        RuleFor(x => x.TaxNumber).NotNull().MaximumLength(40);
    }
}
public sealed class PurchaseValidator : AbstractValidator<PurchaseRequest>
{
    public PurchaseValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty(); RuleFor(x => x.Notes).NotNull().MaximumLength(2000);
        RuleFor(x => x.Items).NotEmpty().Must(items => items is not null && items.Count <= 100).WithMessage("Maximum 100 items.")
            .Must(items => items is not null && items.All(i => i is not null) && items.Select(i => i.ProductId).Distinct().Count() == items.Count).WithMessage("Choose each product only once.");
        RuleForEach(x => x.Items).ChildRules(line => {
            line.RuleFor(x => x.ProductId).NotEmpty(); line.RuleFor(x => x.QuantityOrdered).InclusiveBetween(1, 100000);
            line.RuleFor(x => x.UnitCost).InclusiveBetween(0, 9999999999.99m).PrecisionScale(12, 2, false);
        });
    }
}
public sealed class ReceiptValidator : AbstractValidator<ReceiptRequest>
{
    public ReceiptValidator()
    {
        RuleFor(x => x.Notes).NotNull().MaximumLength(1000);
        RuleFor(x => x.Items).NotEmpty().Must(items => items is not null && items.Count <= 100).WithMessage("Maximum 100 items.")
            .Must(items => items is not null && items.All(i => i is not null) && items.Select(i => i.PurchaseOrderItemId).Distinct().Count() == items.Count).WithMessage("Choose each purchase item only once.");
        RuleForEach(x => x.Items).ChildRules(line => { line.RuleFor(x => x.PurchaseOrderItemId).NotEmpty(); line.RuleFor(x => x.QuantityReceived).InclusiveBetween(1, 100000); });
    }
}
