namespace Benchmark.Models;

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CustomerDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public int Age { get; set; }
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
}

public sealed class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
}

public sealed class OrderLine
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class OrderLineDto
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public DateTime PlacedAt { get; set; }
    public Customer Customer { get; set; } = new();
    public Address ShippingAddress { get; set; } = new();
    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderDto
{
    public int Id { get; set; }
    public DateTime PlacedAt { get; set; }
    public CustomerDto? Customer { get; set; }
    public AddressDto? ShippingAddress { get; set; }
    public List<OrderLineDto>? Lines { get; set; }
}

public static class SampleData
{
    public static Customer Customer() => new()
    {
        Id = 42,
        Name = "Nguyen Van A",
        Email = "a@example.com",
        Age = 30,
        IsActive = true,
        Balance = 1234.56m,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    public static Order Order(int lineCount = 10) => new()
    {
        Id = 1001,
        PlacedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        Customer = Customer(),
        ShippingAddress = new Address { Street = "1 Trang Tien", City = "Hanoi", Country = "VN" },
        Lines =
        [
            .. Enumerable.Range(1, lineCount).Select(i => new OrderLine
            {
                ProductId = i, ProductName = $"Product {i}", Quantity = i, UnitPrice = i * 1.5m
            })
        ]
    };
}
