using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace Middlewares;

public sealed class HttpBodyLoggingMiddleware
{
    private const int DefaultLimitBytes = 8 * 1024;
    private readonly RequestDelegate _next;
    private readonly ILogger<HttpBodyLoggingMiddleware> _logger;

    public HttpBodyLoggingMiddleware(RequestDelegate next, ILogger<HttpBodyLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/Authentication", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var contentType = context.Request.ContentType ?? string.Empty;
        var isJson = contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);

        string? requestBody = null;
        if (isJson && context.Request.ContentLength is > 0)
        {
            context.Request.EnableBuffering();
            requestBody = await ReadBodyAsync(context.Request.Body, DefaultLimitBytes);
            context.Request.Body.Position = 0;
        }

        var originalResponseBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            buffer.Position = 0;
            var responseContentType = context.Response.ContentType ?? string.Empty;
            var responseIsJson = responseContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
            var responseBody = responseIsJson ? await ReadBodyAsync(buffer, DefaultLimitBytes) : null;

            buffer.Position = 0;
            await buffer.CopyToAsync(originalResponseBody);

            _logger.LogInformation(
                "HTTP {Method} {Path} => {StatusCode} traceId={TraceId} spanId={SpanId} requestId={RequestId} requestBody={RequestBody} responseBody={ResponseBody}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                context.TraceIdentifier,
                requestBody,
                responseBody);
        }
        finally
        {
            context.Response.Body = originalResponseBody;
        }
    }

    private static async Task<string> ReadBodyAsync(Stream stream, int limitBytes)
    {
        stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

        var sb = new StringBuilder();
        var buffer = new char[1024];
        var remaining = limitBytes;

        while (remaining > 0)
        {
            var read = await reader.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read <= 0) break;
            sb.Append(buffer, 0, read);
            remaining -= read;
        }

        if (reader.Peek() >= 0)
            sb.Append("…(truncated)");

        return sb.ToString();
    }
}
