using System.Security.Cryptography;

namespace BackendApi.Modules.Catalog.Primitives;

public sealed class ContentAddressedPaths
{
    public string OriginalKey(Guid productId, ReadOnlySpan<byte> contentSha256, string extension)
    {
        var prefix = Convert.ToHexString(contentSha256[..8]).ToLowerInvariant();
        return $"catalog/{productId:N}/{prefix}/original.{NormalizeExtension(extension)}";
    }

    public string VariantKey(Guid productId, ReadOnlySpan<byte> contentSha256, string variantName, string format)
    {
        var prefix = Convert.ToHexString(contentSha256[..8]).ToLowerInvariant();
        return $"catalog/{productId:N}/{prefix}/{variantName}.{NormalizeExtension(format)}";
    }

    public byte[] ComputeSha256(ReadOnlySpan<byte> bytes)
    {
        return SHA256.HashData(bytes);
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.TrimStart('.').ToLowerInvariant();
        return trimmed switch
        {
            "jpg" => "jpg",
            "jpeg" => "jpg",
            "png" => "png",
            "webp" => "webp",
            _ => trimmed,
        };
    }
}
