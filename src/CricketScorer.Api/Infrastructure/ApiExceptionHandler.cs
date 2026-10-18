using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CricketScorer.Api.Infrastructure;

public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    // Turns an ApiException (or two saves racing on the same row) into a ProblemDetails response; anything else stays a 500.
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        var api = exception switch
        {
            ApiException e => e,
            DbUpdateConcurrencyException => new ApiException(StatusCodes.Status409Conflict,
                "Someone else changed this at the same time. Try again.", "retry"),
            _ => null
        };
        if (api is null) return false;

        var problem = new ProblemDetails { Status = api.StatusCode, Title = api.Message };
        if (api.Code is not null) problem.Extensions["code"] = api.Code;
        context.Response.StatusCode = api.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
