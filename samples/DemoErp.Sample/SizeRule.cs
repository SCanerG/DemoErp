namespace DemoErp.Sample;

// Adapted from the private domain's size definition; no persistence or ERP dependencies.
public sealed record SizeRequest(decimal WidthCm, decimal LengthCm, string Shape);
public sealed record SizeResult(decimal GrossAreaM2);

public static class SizeRule
{
    public static SizeResult Evaluate(SizeRequest request)
    {
        if (request.WidthCm <= 0 || request.LengthCm <= 0 ||
            request.WidthCm > 10000 || request.LengthCm > 10000)
            throw new ArgumentException("Dimensions must be positive and at most 10000 cm.");

        if (request.Shape is not ("Rectangle" or "Square" or "Round" or "Oval" or "Custom"))
            throw new ArgumentException("Unknown shape.");

        if ((request.Shape is "Square" or "Round") && request.WidthCm != request.LengthCm)
            throw new ArgumentException("Square and round dimensions must be equal.");

        return new(request.WidthCm * request.LengthCm / 10000m);
    }
}
