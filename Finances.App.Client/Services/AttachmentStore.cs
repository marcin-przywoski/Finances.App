using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Finances.App.Client.Services;

public sealed record AttachmentBlob(
    string Id,
    string Mime,
    string? OriginalFileName,
    long SizeBytes,
    DateTime CreatedUtc,
    string? EntityType,
    int? EntityId,
    byte[] Content);

public sealed record AttachmentSummary(
    string Id,
    string Mime,
    string? OriginalFileName,
    long SizeBytes,
    DateTime CreatedUtc,
    string? EntityType,
    int? EntityId);

public sealed record AttachmentPutResult(string Id, long SizeBytes, string Mime, DateTime CreatedUtc);

/// <summary>
/// Thin wrapper around the JS financeStore module that persists attachment blobs
/// to IndexedDB. The Blazor side always works in terms of byte arrays; the JS
/// layer handles Blob creation and base64 transfer.
/// </summary>
public sealed class AttachmentStore
{
    private readonly IJSRuntime _js;

    public AttachmentStore(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<AttachmentPutResult> PutAsync(string id, string mime, byte[] content, string? originalFileName = null, string? entityType = null, int? entityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(content);

        var base64 = Convert.ToBase64String(content);
        var dto = await _js.InvokeAsync<JsAttachmentPutDto>(
            "financeStore.putAttachment",
            id, mime, base64, originalFileName, entityType, entityId);

        return new AttachmentPutResult(
            dto.Id ?? id,
            dto.SizeBytes,
            dto.Mime ?? mime,
            ParseDate(dto.CreatedUtc));
    }

    public async Task<AttachmentBlob?> GetAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var dto = await _js.InvokeAsync<JsAttachmentGetDto?>("financeStore.getAttachment", id);
        if (dto is null) return null;

        var bytes = string.IsNullOrEmpty(dto.Base64) ? Array.Empty<byte>() : Convert.FromBase64String(dto.Base64);
        return new AttachmentBlob(
            dto.Id ?? id,
            dto.Mime ?? "application/octet-stream",
            dto.OriginalFileName,
            dto.SizeBytes,
            ParseDate(dto.CreatedUtc),
            dto.EntityType,
            dto.EntityId,
            bytes);
    }

    public async Task<string?> GetBlobUrlAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return await _js.InvokeAsync<string?>("financeStore.getAttachmentBlobUrl", id);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        return await _js.InvokeAsync<bool>("financeStore.deleteAttachment", id);
    }

    public async Task<IReadOnlyList<string>> ListIdsAsync()
    {
        var ids = await _js.InvokeAsync<string[]?>("financeStore.listAttachmentIds");
        return ids ?? Array.Empty<string>();
    }

    public async Task<IReadOnlyList<AttachmentSummary>> ListSummariesAsync()
    {
        var items = await _js.InvokeAsync<JsAttachmentGetDto[]?>("financeStore.getAttachmentsSummary");
        if (items is null || items.Length == 0) return Array.Empty<AttachmentSummary>();
        return items
            .Select(item => new AttachmentSummary(
                item.Id ?? string.Empty,
                item.Mime ?? "application/octet-stream",
                item.OriginalFileName,
                item.SizeBytes,
                ParseDate(item.CreatedUtc),
                item.EntityType,
                item.EntityId))
            .ToList();
    }

    public async Task<StorageEstimate?> EstimateUsageAsync()
    {
        try
        {
            return await _js.InvokeAsync<StorageEstimate?>("financeStore.estimateUsage");
        }
        catch
        {
            return null;
        }
    }

    private static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateTime.UtcNow;
        return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTime.UtcNow;
    }

    private sealed class JsAttachmentPutDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("mime")] public string? Mime { get; set; }
        [JsonPropertyName("createdUtc")] public string? CreatedUtc { get; set; }
    }

    private sealed class JsAttachmentGetDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("mime")] public string? Mime { get; set; }
        [JsonPropertyName("originalFileName")] public string? OriginalFileName { get; set; }
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("createdUtc")] public string? CreatedUtc { get; set; }
        [JsonPropertyName("entityType")] public string? EntityType { get; set; }
        [JsonPropertyName("entityId")] public int? EntityId { get; set; }
        [JsonPropertyName("base64")] public string? Base64 { get; set; }
    }
}

public sealed class StorageEstimate
{
    [JsonPropertyName("quota")] public long? Quota { get; set; }
    [JsonPropertyName("usage")] public long? Usage { get; set; }
}
