using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Globalization;
using Finances.App.Shared;

namespace Finances.App.Client.Services;

public sealed class LocalApiMessageHandler : HttpMessageHandler
{
    private readonly LocalFinanceStore _store;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public LocalApiMessageHandler(LocalFinanceStore store)
    {
        _store = store;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await RouteAsync(request, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            return JsonResponse(new { message = ex.Message }, HttpStatusCode.BadRequest);
        }
        catch (KeyNotFoundException)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private async Task<HttpResponseMessage> RouteAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = GetApiPath(request.RequestUri);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 2 || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        return segments[1].ToLowerInvariant() switch
        {
            "workers" => await HandleWorkersAsync(request, segments, cancellationToken),
            "services" => await HandleServicesAsync(request, segments, cancellationToken),
            "products" => await HandleProductsAsync(request, segments, cancellationToken),
            "servicerecords" => await HandleServiceRecordsAsync(request, segments, cancellationToken),
            "productsales" => await HandleProductSalesAsync(request, segments, cancellationToken),
            "analytics" => await HandleAnalyticsAsync(request, segments),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private async Task<HttpResponseMessage> HandleWorkersAsync(HttpRequestMessage request, string[] segments, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && segments.Length == 2)
        {
            return JsonResponse(await _store.GetWorkersAsync());
        }

        if (request.Method == HttpMethod.Get && TryParseId(segments, 2, out var id))
        {
            var worker = (await _store.GetWorkersAsync()).FirstOrDefault(item => item.Id == id);
            return worker is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : JsonResponse(worker);
        }

        if (request.Method == HttpMethod.Post && segments.Length == 2)
        {
            var worker = await ReadBodyAsync<Worker>(request, cancellationToken);
            return JsonResponse(await _store.AddWorkerAsync(worker), HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Put && TryParseId(segments, 2, out id))
        {
            var worker = await ReadBodyAsync<Worker>(request, cancellationToken);
            await _store.UpdateWorkerAsync(id, worker);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (request.Method == HttpMethod.Delete && TryParseId(segments, 2, out id))
        {
            var result = await _store.DeleteWorkerAsync(id);
            return result.Success
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : JsonResponse(new { message = result.ErrorMessage }, HttpStatusCode.Conflict);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleServicesAsync(HttpRequestMessage request, string[] segments, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && segments.Length == 2)
        {
            return JsonResponse(await _store.GetServicesAsync());
        }

        if (request.Method == HttpMethod.Get && TryParseId(segments, 2, out var id))
        {
            var service = (await _store.GetServicesAsync()).FirstOrDefault(item => item.Id == id);
            return service is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : JsonResponse(service);
        }

        if (request.Method == HttpMethod.Post && segments.Length == 2)
        {
            var service = await ReadBodyAsync<Service>(request, cancellationToken);
            return JsonResponse(await _store.AddServiceAsync(service), HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Put && TryParseId(segments, 2, out id))
        {
            var service = await ReadBodyAsync<Service>(request, cancellationToken);
            await _store.UpdateServiceAsync(id, service);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (request.Method == HttpMethod.Delete && TryParseId(segments, 2, out id))
        {
            var result = await _store.DeleteServiceAsync(id);
            return result.Success
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : JsonResponse(new { message = result.ErrorMessage }, HttpStatusCode.Conflict);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleProductsAsync(HttpRequestMessage request, string[] segments, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && segments.Length == 2)
        {
            return JsonResponse(await _store.GetProductsAsync());
        }

        if (request.Method == HttpMethod.Get && TryParseId(segments, 2, out var id))
        {
            var product = (await _store.GetProductsAsync()).FirstOrDefault(item => item.Id == id);
            return product is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : JsonResponse(product);
        }

        if (request.Method == HttpMethod.Post && segments.Length == 2)
        {
            var product = await ReadBodyAsync<Product>(request, cancellationToken);
            return JsonResponse(await _store.AddProductAsync(product), HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Put && TryParseId(segments, 2, out id))
        {
            var product = await ReadBodyAsync<Product>(request, cancellationToken);
            await _store.UpdateProductAsync(id, product);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (request.Method == HttpMethod.Delete && TryParseId(segments, 2, out id))
        {
            var result = await _store.DeleteProductAsync(id);
            return result.Success
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : JsonResponse(new { message = result.ErrorMessage }, HttpStatusCode.Conflict);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleProductSalesAsync(HttpRequestMessage request, string[] segments, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && segments.Length == 2)
        {
            return JsonResponse(await _store.GetProductSalesAsync());
        }

        if (request.Method == HttpMethod.Get && TryParseId(segments, 2, out var id))
        {
            var sale = (await _store.GetProductSalesAsync()).FirstOrDefault(item => item.Id == id);
            return sale is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : JsonResponse(sale);
        }

        if (request.Method == HttpMethod.Post && segments.Length == 2)
        {
            var sale = await ReadBodyAsync<ProductSale>(request, cancellationToken);
            return JsonResponse(await _store.AddProductSaleAsync(sale), HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Put && TryParseId(segments, 2, out id))
        {
            var sale = await ReadBodyAsync<ProductSale>(request, cancellationToken);
            await _store.UpdateProductSaleAsync(id, sale);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (request.Method == HttpMethod.Delete && TryParseId(segments, 2, out id))
        {
            await _store.DeleteProductSaleAsync(id);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleServiceRecordsAsync(HttpRequestMessage request, string[] segments, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && segments.Length == 2)
        {
            return JsonResponse(await _store.GetServiceRecordsAsync());
        }

        if (request.Method == HttpMethod.Get && TryParseId(segments, 2, out var id))
        {
            var record = (await _store.GetServiceRecordsAsync()).FirstOrDefault(item => item.Id == id);
            return record is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : JsonResponse(record);
        }

        if (request.Method == HttpMethod.Post && segments.Length == 2)
        {
            var record = await ReadBodyAsync<ServiceRecord>(request, cancellationToken);
            return JsonResponse(await _store.AddServiceRecordAsync(record), HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Put && TryParseId(segments, 2, out id))
        {
            var record = await ReadBodyAsync<ServiceRecord>(request, cancellationToken);
            await _store.UpdateServiceRecordAsync(id, record);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (request.Method == HttpMethod.Delete && TryParseId(segments, 2, out id))
        {
            await _store.DeleteServiceRecordAsync(id);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleAnalyticsAsync(HttpRequestMessage request, string[] segments)
    {
        if (request.Method != HttpMethod.Get || segments.Length < 3)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var query = ParseQuery(request.RequestUri?.Query);
        var workerId = ReadNullableInt(query, "workerId");
        var from = ReadNullableDate(query, "from");
        var to = ReadNullableDate(query, "to");

        return segments[2].ToLowerInvariant() switch
        {
            "summary" => JsonResponse(await _store.GetSummaryAsync(workerId, from, to)),
            "daily-earnings" => JsonResponse(await _store.GetDailyEarningsAsync(workerId, from, to)),
            "revenue-by-worker" => JsonResponse(await _store.GetRevenueByWorkerAsync(workerId, from, to)),
            "service-popularity" => JsonResponse(await _store.GetServicePopularityAsync(workerId, from, to)),
            "forecast" => JsonResponse(await _store.GetForecastAsync(workerId, from, to, ReadInt(query, "forecastDays", 14))),
            "recent" => JsonResponse(await _store.GetRecentAsync(workerId, ReadInt(query, "count", 5))),
            "productsales-revenue" => JsonResponse(new { revenue = await _store.GetProductSalesRevenueAsync(from, to) }),
            "month-comparison" => JsonResponse(await _store.GetMonthComparisonAsync(workerId)),
            "revenue-by-dayofweek" => JsonResponse(await _store.GetRevenueByDayOfWeekAsync(workerId, from, to)),
            "top-products" => JsonResponse(await _store.GetTopProductsAsync(workerId, from, to, ReadInt(query, "count", 5))),
            "combined-timeline" => JsonResponse(await _store.GetCombinedTimelineAsync(workerId, from, to)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private async Task<T> ReadBodyAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            throw new InvalidDataException("Request body is required.");
        }

        var payload = await request.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
        return payload ?? throw new InvalidDataException("Request body is required.");
    }

    private HttpResponseMessage JsonResponse<T>(T value, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = JsonContent.Create(value, options: _jsonOptions)
        };
    }

    private static string GetApiPath(Uri? uri)
    {
        var path = uri?.AbsolutePath ?? string.Empty;
        var apiIndex = path.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
        return apiIndex >= 0 ? path[(apiIndex + 1)..] : path.TrimStart('/');
    }

    private static bool TryParseId(string[] segments, int index, out int id)
    {
        id = 0;
        return segments.Length > index && int.TryParse(segments[index], out id);
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => Uri.UnescapeDataString(part.Length > 1 ? part[1] : string.Empty),
                StringComparer.OrdinalIgnoreCase);
    }

    private static int? ReadNullableInt(IReadOnlyDictionary<string, string> query, string key)
    {
        return query.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static DateTime? ReadNullableDate(IReadOnlyDictionary<string, string> query, string key)
    {
        return query.TryGetValue(key, out var value)
            && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> query, string key, int defaultValue)
    {
        return query.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}