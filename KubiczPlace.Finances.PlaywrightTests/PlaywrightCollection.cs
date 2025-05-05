using Xunit;

namespace KubiczPlace.Finances.PlaywrightTests;

[CollectionDefinition("playwright")]
public class PlaywrightCollection : ICollectionFixture<PlaywrightFixture> { }
