using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Demo.Api.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problems)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        if (exception is BusinessException business)
        {
            context.Response.StatusCode = business.Status;
            problem = new ProblemDetails { Status = business.Status, Title = "Business rule violation", Detail = business.Code };
            problem.Extensions["code"] = business.Code;
        }
        else if (exception is ValidationException validation)
        {
            context.Response.StatusCode = 400;
            problem = new ValidationProblemDetails(validation.Errors.GroupBy(x => x.PropertyName)
                .ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).Distinct().ToArray()))
            { Status = 400, Title = "Validation failed" };
        }
        else if ((exception is DbUpdateException ? exception.InnerException : exception) is PostgresException pg
            && pg.SqlState is PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.SerializationFailure)
        {
            context.Response.StatusCode = 409;
            var code = pg.SqlState == PostgresErrorCodes.SerializationFailure ? "orderChanged" : "recordReferenced";
            problem = new ProblemDetails { Status = 409, Title = "Business rule violation", Detail = code };
            problem.Extensions["code"] = code;
        }
        else
        {
            logger.LogError(exception, "Unexpected failure on {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = 500;
            problem = new ProblemDetails { Status = 500, Title = "An unexpected error occurred",
                Detail = "Please try again later." };
        }
        problem.Extensions["traceId"] = context.TraceIdentifier;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
        return true;
    }
}
