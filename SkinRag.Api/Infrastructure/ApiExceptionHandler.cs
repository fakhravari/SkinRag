using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Application.Validation;

namespace SkinRag.Api.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var (status, title) = exception switch
        {
            IndexNotReadyException e => (503, e.Message),
            ArgumentException e => (400, e.Message),
            HttpRequestException => (503, "سرویس مدل در دسترس نیست یا درخواست مدل ناموفق بود."),
            SqlException => (503, "اتصال دیتابیس ناموفق است؛ گزارش سرور را بررسی کنید."),
            OperationCanceledException => (504, "زمان انتظار برای سرویس به پایان رسید."),
            _ => (500, "خطای داخلی رخ داد؛ گزارش سرور را بررسی کنید.")
        };
        logger.LogError(
            exception,
            "API request failed with status {Status}; trace {Trace}",
            status,
            context.TraceIdentifier);
        context.Response.StatusCode = status;
        if (status == 503)
        {
            context.Response.Headers.RetryAfter = "15";
        }

        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Instance = context.Request.Path,
                Extensions =
                {
                    ["traceId"] = context.TraceIdentifier,
                    ["code"] = exception is InputRejectedException input ? input.Code : null
                }
            },
            cancellationToken: ct);
        return true;
    }
}
