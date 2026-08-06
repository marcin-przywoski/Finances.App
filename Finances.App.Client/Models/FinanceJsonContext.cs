using System.Text.Json;
using System.Text.Json.Serialization;

namespace Finances.App.Client.Models;

/// <summary>
/// Source-generated serialization contract for the persisted snapshot.
/// Keeps import/export working under IL trimming and avoids reflection
/// on the hot save path. Property names stay camelCase for compatibility
/// with backups produced by earlier builds.
/// </summary>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(FinanceSnapshot))]
internal sealed partial class FinanceJsonContext : JsonSerializerContext;
