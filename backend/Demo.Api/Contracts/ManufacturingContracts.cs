using Demo.Api.Domain;
using Demo.Api.Errors;
using FluentValidation;
namespace Demo.Api.Contracts;
public sealed record BomItemRequest(Guid MaterialProductId, int QuantityRequired);
public sealed record BomRequest(Guid ProductId, string Version, string Name, int BaseQuantity, List<BomItemRequest> Items);
public sealed record BomLine(Guid MaterialProductId, string MaterialName, int QuantityRequired);
public sealed record BomResponse(Guid Id, Guid ProductId, string ProductName, string Version, string Name, int BaseQuantity,
    bool IsActive, DateTimeOffset? ActivatedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, Guid CreatedByUserId, string CreatedBy, List<BomLine> Items);
public sealed record ProductionRequest(Guid ProductId, Guid BillOfMaterialsId, int PlannedQuantity, Guid SourceWarehouseId,
    Guid DestinationWarehouseId, DateTimeOffset? PlannedStartDate, DateTimeOffset? PlannedEndDate, string Notes);
public sealed record MaterialAvailability(Guid MaterialProductId, string MaterialName, int QuantityRequired, int QuantityConsumed,
    Guid SourceWarehouseId, string SourceWarehouseName, int AvailableQuantity, int ShortageQuantity, string AvailabilityStatus);
public sealed record ProductionResponse(Guid Id, string ProductionOrderNumber, Guid ProductId, string ProductName, Guid BillOfMaterialsId,
    string BomVersion, int PlannedQuantity, int ProducedQuantity, Guid SourceWarehouseId, string SourceWarehouseName,
    Guid DestinationWarehouseId, string DestinationWarehouseName, ProductionStatus Status, DateTimeOffset? PlannedStartDate,
    DateTimeOffset? PlannedEndDate, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string Notes, DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt, Guid CreatedByUserId, string CreatedBy, Guid? StartedByUserId, string? StartedBy,
    Guid? CompletedByUserId, string? CompletedBy, List<MaterialAvailability> Materials);
public sealed record ManufacturingKpis(int Draft, int Released, int InProgress, int Completed, long TotalCompletedQuantity);
public sealed record MaterialRequirementRow(Guid ProductionOrderId, string ProductionOrderNumber, string ProductName,
    Guid MaterialProductId, string MaterialName, int QuantityRequired, int AvailableQuantity, int ShortageQuantity, Guid SourceWarehouseId, string SourceWarehouseName);
public sealed class ManufacturingFilter
{
    public string? Search { get; set; }
    public Guid? ProductId { get; set; }
    public string? Version { get; set; }
    public string? BomStatus { get; set; }
    public ProductionStatus? Status { get; set; }
    public Guid? SourceWarehouseId { get; set; }
    public Guid? DestinationWarehouseId { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string Sort { get; set; } = "newest";
    public void Validate()
    {
        Start = Start?.ToUniversalTime(); End = End?.ToUniversalTime();
        if (Page is < 1 or > 1000000 || PageSize is < 1 or > 100 || Search?.Length > 150 || Version?.Length > 40
            || BomStatus is not (null or "Draft" or "Active" or "Inactive") || Sort is not ("newest" or "oldest" or "version")
            || Status.HasValue && !Enum.IsDefined(Status.Value) || Start.HasValue != End.HasValue || Start > End || End - Start > TimeSpan.FromDays(366))
            throw new BusinessException("invalidManufacturingFilters");
    }
}
public sealed class BomValidator : AbstractValidator<BomRequest>
{
    public BomValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty(); RuleFor(x => x.Version).NotEmpty().MaximumLength(40); RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BaseQuantity).GreaterThan(0);
        RuleFor(x => x.Items).NotEmpty().Must(items => items is not null && items.Count <= 100 && items.All(i => i is not null) && items.Select(i => i.MaterialProductId).Distinct().Count() == items.Count);
        RuleForEach(x => x.Items).NotNull().ChildRules(i => { i.RuleFor(x => x.MaterialProductId).NotEmpty(); i.RuleFor(x => x.QuantityRequired).GreaterThan(0); });
        RuleFor(x => x).Must(x => x.Items is not null && x.Items.All(i => i is not null && i.MaterialProductId != x.ProductId)).WithMessage("A finished product cannot consume itself.");
    }
}
public sealed class ProductionValidator : AbstractValidator<ProductionRequest>
{
    public ProductionValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty(); RuleFor(x => x.BillOfMaterialsId).NotEmpty(); RuleFor(x => x.PlannedQuantity).GreaterThan(0);
        RuleFor(x => x.SourceWarehouseId).NotEmpty(); RuleFor(x => x.DestinationWarehouseId).NotEmpty(); RuleFor(x => x.Notes).NotNull().MaximumLength(2000);
        RuleFor(x => x).Must(x => x.PlannedStartDate is null || x.PlannedEndDate is null || x.PlannedEndDate >= x.PlannedStartDate).WithMessage("Planned end must not precede planned start.");
    }
}
