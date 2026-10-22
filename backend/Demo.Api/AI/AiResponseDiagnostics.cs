namespace Demo.Api.AI;
// Request-scoped metadata only. Never retain prompts, report rows, arguments, thinking or model text.
public sealed class AiResponseDiagnostics
{
    public Guid? ExecutionId { get; set; }
    public int? OutputTokens { get; private set; }
    public int ActualProviderCalls { get; private set; }
    public int KnownTotalTokens { get; private set; }
    public string? LastValidationFailure { get; private set; }
    public void Attempt() => ActualProviderCalls++;
    public void Usage(int? total,int? output) { KnownTotalTokens+=total??0;if(output.HasValue)OutputTokens=(OutputTokens??0)+output; }
    public void Failure(string type) => LastValidationFailure=type;
}
