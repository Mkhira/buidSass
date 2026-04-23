using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace BackendApi.Modules.Catalog.Primitives;

public interface IImageVariantGenerator
{
    Task<IReadOnlyList<ImageVariantResult>> GenerateAsync(Stream originalStream, CancellationToken cancellationToken);
}

public sealed class ImageSharpVariantGenerator : IImageVariantGenerator
{
    private static readonly (string Name, int Width, int Height) ThumbSpec = ("thumb", 96, 96);
    private static readonly (string Name, int Width, int Height) CardSpec = ("card", 320, 320);
    private static readonly (string Name, int Width, int Height) DetailSpec = ("detail", 960, 0);
    private static readonly (string Name, int Width, int Height) HeroSpec = ("hero", 1600, 0);

    public async Task<IReadOnlyList<ImageVariantResult>> GenerateAsync(Stream originalStream, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync(originalStream, cancellationToken);
        var results = new List<ImageVariantResult>();

        foreach (var spec in new[] { ThumbSpec, CardSpec, DetailSpec, HeroSpec })
        {
            foreach (var format in new[] { "jpeg", "webp" })
            {
                using var clone = image.Clone(ctx => Resize(ctx, spec.Width, spec.Height));
                await using var buffer = new MemoryStream();
                if (format == "jpeg")
                {
                    await clone.SaveAsync(buffer, new JpegEncoder { Quality = 85 }, cancellationToken);
                }
                else
                {
                    await clone.SaveAsync(buffer, new WebpEncoder { Quality = 82 }, cancellationToken);
                }

                results.Add(new ImageVariantResult(
                    VariantName: spec.Name,
                    Format: format,
                    Width: clone.Width,
                    Height: clone.Height,
                    Bytes: buffer.ToArray()));
            }
        }

        return results;
    }

    private static void Resize(IImageProcessingContext ctx, int width, int height)
    {
        var size = height == 0 ? new Size(width, 0) : new Size(width, height);
        ctx.Resize(new ResizeOptions
        {
            Mode = height == 0 ? ResizeMode.Max : ResizeMode.Crop,
            Size = size,
        });
    }
}

public sealed record ImageVariantResult(string VariantName, string Format, int Width, int Height, byte[] Bytes);
