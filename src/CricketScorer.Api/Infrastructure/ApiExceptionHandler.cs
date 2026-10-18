using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CricketScorer.Api.Infrastructure;

public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    // Turns an ApiException into a ProblemDetails response; other exceptions fall through to the default 500.
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        if (exception is not ApiException api) return false;
        context.Response.StatusCode = api.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = api.StatusCode, Title = api.Message },
            Exception = exception
        });
    }
}
