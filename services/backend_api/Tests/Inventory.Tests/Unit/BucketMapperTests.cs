using BackendApi.Modules.Inventory.Primitives;
using FluentAssertions;
using Inventory.Tests.Infrastructure;

namespace Inventory.Tests.Unit;

[Collection("inventory-fixture")]
public sealed class BucketMapperTests
{
    [Fact]
    public void Map_WhenAtsPositive_ReturnsInStock()
    {
        var sut = new BucketMapper();

        sut.Map(5).Should().Be("in_stock");
    }

    [Fact]
    public void Map_WhenAtsZeroAndFutureSupply_ReturnsBackorder()
    {
        var sut = new BucketMapper();

        sut.Map(0, hasFutureSupply: true).Should().Be("backorder");
    }

    [Fact]
    public void Map_WhenAtsZeroWithoutFutureSupply_ReturnsOutOfStock()
    {
        var sut = new BucketMapper();

        sut.Map(0, hasFutureSupply: false).Should().Be("out_of_stock");
    }

    [Fact]
    public void Map_WhenAtsNegative_ReturnsOutOfStock()
    {
        var sut = new BucketMapper();

        sut.Map(-4).Should().Be("out_of_stock");
    }
}
