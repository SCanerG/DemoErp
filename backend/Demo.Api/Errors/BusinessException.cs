namespace Demo.Api.Errors;
public class BusinessException(string code, int status = 400) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
