using Demo.Api.Domain;
using Demo.Api.Errors;
using FluentValidation;

namespace Demo.Api.Contracts;
public sealed record WarehouseRequest(string Code, string Name, string Description, string Location);
public sealed record WarehouseResponse(Guid Id, string Code, string Name, string Description, string Location, bool IsActive, bool IsDefault,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, int ProductCount, long TotalStockQuantity, int LowStockProducts, int OutOfStockProducts);
public sealed record WarehouseStock(Guid Id, Guid WarehouseId, string WarehouseName, Guid ProductId, string ProductName, string CategoryName,
    bool IsActive, int QuantityOnHand, int MinimumStockLevel, string StockStatus, DateTimeOffset? UpdatedAt);
public sealed record TransferItemRequest(Guid ProductId, int Quantity);
public sealed record TransferRequest(Guid SourceWarehouseId, Guid DestinationWarehouseId, List<TransferItemRequest> Items, string Notes);
public sealed record TransferLine(Guid Id, Guid ProductId, string ProductName, int Quantity, int SourceStock);
public sealed record TransferSummary(Guid Id, string TransferNumber, Guid SourceWarehouseId, string SourceWarehouseName,
    Guid DestinationWarehouseId, string DestinationWarehouseName, TransferStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, long TotalItemQuantity);
public sealed record TransferResponse(Guid Id, string TransferNumber, Guid SourceWarehouseId, string SourceWarehouseName,
    Guid DestinationWarehouseId, string DestinationWarehouseName, TransferStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    DateTimeOffset? CompletedAt, Guid CreatedByUserId, string CreatedBy, Guid? CompletedByUserId, string? CompletedBy, string Notes, List<TransferLine> Items);
public sealed record WarehouseReport(int TotalWarehouses, int ActiveWarehouses, long TotalUnits, int LowStockCombinations,
    long OutOfStockCombinations, int CompletedTransfers, ReportPage<WarehouseResponse> Distribution);
public sealed class WarehouseFilter
{
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsDefault { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? CategoryId { get; set; }
    public string? StockStatus { get; set; }
    public void Validate()
    {
        if (Page is < 1 or > 1000000 || PageSize is < 1 or > 100 || Search?.Length > 150 || StockStatus is not (null or "Low" or "Out" or "Healthy")) throw new BusinessException("invalidWarehouseFilters");
    }
}
public sealed class TransferFilter
{
    public string? Search { get; set; }
    public Guid? SourceWarehouseId { get; set; }
    public Guid? DestinationWarehouseId { get; set; }
    public TransferStatus? Status { get; set; }
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public void Validate()
    {
        Start = Start?.ToUniversalTime(); End = End?.ToUniversalTime();
        if (Page is < 1 or > 1000000 || PageSize is < 1 or > 100 || Search?.Length > 150 || Status.HasValue && !Enum.IsDefined(Status.Value)
            || Start.HasValue != End.HasValue || Start >= End || End - Start > TimeSpan.FromDays(366)) throw new BusinessException("invalidWarehouseFilters");
    }
}
public sealed class WarehouseValidator : AbstractValidator<WarehouseRequest>
{
    public WarehouseValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40).Matches(@"^\s*[A-Za-z0-9][A-Za-z0-9_-]*\s*$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150); RuleFor(x => x.Description).NotNull().MaximumLength(2000); RuleFor(x => x.Location).NotNull().MaximumLength(250);
    }
}
public sealed class TransferValidator : AbstractValidator<TransferRequest>
{
    public TransferValidator()
    {
        RuleFor(x => x.SourceWarehouseId).NotEmpty(); RuleFor(x => x.DestinationWarehouseId).NotEmpty().NotEqual(x => x.SourceWarehouseId);
        RuleFor(x => x.Notes).NotNull().MaximumLength(2000);
        RuleFor(x => x.Items).NotEmpty().Must(items => items is not null && items.Count <= 100 && items.All(i => i is not null) && items.Select(i => i.ProductId).Distinct().Count() == items.Count);
        RuleForEach(x => x.Items).NotNull().ChildRules(line => { line.RuleFor(x => x.ProductId).NotEmpty(); line.RuleFor(x => x.Quantity).InclusiveBetween(1, 100000); });
    }
}
