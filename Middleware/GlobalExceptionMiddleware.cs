using System.Net;
using System.Text.Json;

namespace MarketApp.Middleware;

public class GlobalExceptionMiddleware
{
    private const string GenericErrorMessage =
        "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.";

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("İstek istemci tarafından iptal edildi. Path: {Path}", context.Request.Path);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogError(ex, "Response başladıktan sonra beklenmeyen hata oluştu. Path: {Path}", context.Request.Path);
                throw;
            }

            _logger.LogError(
                ex,
                "Beklenmeyen hata yakalandı. TraceId: {TraceId}, Method: {Method}, Path: {Path}",
                context.TraceIdentifier,
                context.Request.Method,
                context.Request.Path);

            context.Response.Clear();
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var response = new
            {
                message = GenericErrorMessage,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
