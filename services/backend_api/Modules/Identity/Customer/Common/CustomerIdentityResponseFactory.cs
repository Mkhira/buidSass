using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BackendApi.Modules.Identity.Customer.Common;

public static class CustomerIdentityResponseFactory
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

    public static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static string CreateOpaqueToken(int bytes = 32)
    {
        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));
    }

    public static string CreateNumericOtpCode(int digits)
    {
        // Rejection sampling to avoid modulo bias: accept only bytes in [0, 250) since 250 is the
        // largest multiple of 10 that fits in a byte. Bytes in [250, 256) are resampled.
        const byte acceptanceThreshold = 250;
        var chars = new char[digits];
        Span<byte> buffer = stackalloc byte[1];
        for (var i = 0; i < digits; i++)
        {
            do
            {
                RandomNumberGenerator.Fill(buffer);
            } while (buffer[0] >= acceptanceThreshold);

            chars[i] = (char)('0' + (buffer[0] % 10));
        }

        return new string(chars);
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
