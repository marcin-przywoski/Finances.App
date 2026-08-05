using System.Text.Json;
using Finances.App.Client.Models;
using Finances.App.Client.Services;
using Finances.App.Shared;

namespace Finances.App.Client.Tests.TestDoubles;

/// <summary>
/// The 3-worker / 5-service catalog the app used to seed on first run. The
/// app now starts empty, so tests that assume workers 1-3 and services 1-5
/// preload this fixture into storage instead.
/// </summary>
public static class TestData
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static FinanceSnapshot SeedSnapshot() => new()
    {
        Workers =
        [
            new Worker { Id = 1, Name = "Jan Kowalski", DefaultCommissionPercentage = 50 },
            new Worker { Id = 2, Name = "Anna Nowak", DefaultCommissionPercentage = 45 },
            new Worker { Id = 3, Name = "Piotr Wisniewski", DefaultCommissionPercentage = 55 }
        ],
        Services =
        [
            new Service { Id = 1, Name = "Strzyzenie meskie", BasePrice = 50 },
            new Service { Id = 2, Name = "Strzyzenie damskie", BasePrice = 80 },
            new Service { Id = 3, Name = "Broda", BasePrice = 30 },
            new Service { Id = 4, Name = "Koloryzacja", BasePrice = 150 },
            new Service { Id = 5, Name = "Strzyzenie + Broda", BasePrice = 70 }
        ]
    };

    public static InMemoryKeyValueStorage CreateSeededStorage()
    {
        var storage = new InMemoryKeyValueStorage();
        storage.Items[LocalFinanceStore.StorageKey] = JsonSerializer.Serialize(SeedSnapshot(), WebJson);
        return storage;
    }
}
