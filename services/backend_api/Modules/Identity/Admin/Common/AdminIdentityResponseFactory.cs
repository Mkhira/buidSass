using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BackendApi.Modules.Identity.Admin.Common;

public static class AdminIdentityResponseFactory
{
    public static IResult Problem(
        HttpContext context,
        int statusCode,
        string reasonCode,
        string title,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var payload = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://errors.dental-commerce/identity/{reasonCode}",
            Instance = context.Request.Path,
        };

        payload.Extensions["reasonCode"] = reasonCode;
        if (extensions is not null)
        {
            foreach (var extension in extensions)
            {
                payload.Extensions[extension.Key] = extension.Value;
            }
        }

        return Results.Json(payload, statusCode: statusCode, contentType: "application/problem+json");
    }

    public static byte[] HashString(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
    }

    public static string CreateOpaqueToken(int bytes = 32)
    {
        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));
    }

    public static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue("CorrelationId", out var value) && value is string correlationId)
        {
            return correlationId;
        }

        if (context.Request.Headers.TryGetValue("X-Correlation-Id", out var headerValue))
        {
            return headerValue.ToString();
        }

        return context.TraceIdentifier;
    }
}
