using System.Collections.Concurrent;
using System.Text.Json;
using NJsonSchema;

namespace BackendApi.Modules.Catalog.Primitives;

public sealed class AttributeSchemaValidator
{
    private readonly ConcurrentDictionary<(Guid categoryId, int version), JsonSchema> _cache = new();

    public async Task<AttributeSchemaValidationResult> ValidateAsync(
        Guid categoryId,
        int schemaVersion,
        string schemaJson,
        JsonElement attributes,
        CancellationToken cancellationToken)
    {
        var schema = await GetOrParseAsync(categoryId, schemaVersion, schemaJson);
        var errors = schema.Validate(attributes.GetRawText());
        if (errors.Count == 0)
        {
            return AttributeSchemaValidationResult.Valid();
        }

        return AttributeSchemaValidationResult.Invalid(errors
            .Select(e => new AttributeSchemaError(e.Path ?? string.Empty, e.Kind.ToString()))
            .ToArray());
    }

    private async Task<JsonSchema> GetOrParseAsync(Guid categoryId, int version, string schemaJson)
    {
        if (_cache.TryGetValue((categoryId, version), out var cached))
        {
            return cached;
        }

        var schema = await JsonSchema.FromJsonAsync(schemaJson);
        _cache[(categoryId, version)] = schema;
        return schema;
    }
}

public sealed record AttributeSchemaValidationResult(bool IsValid, IReadOnlyCollection<AttributeSchemaError> Errors)
{
    public static AttributeSchemaValidationResult Valid() => new(true, Array.Empty<AttributeSchemaError>());
    public static AttributeSchemaValidationResult Invalid(IReadOnlyCollection<AttributeSchemaError> errors) => new(false, errors);
}

public sealed record AttributeSchemaError(string Path, string Kind);
