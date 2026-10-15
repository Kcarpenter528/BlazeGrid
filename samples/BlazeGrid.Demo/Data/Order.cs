namespace BlazeGrid.Demo.Data;

public enum OrderStatus { New, Processing, OnHold, Shipped, Delivered, Cancelled }

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public string Region { get; set; } = "";
    public string Product { get; set; } = "";
    public OrderStatus Status { get; set; }
    public string Priority { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public DateTime? ShipDate { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public double Discount { get; set; }
    public bool Paid { get; set; }
    public string SalesRep { get; set; } = "";
    public string Notes { get; set; } = "";
}

public static class OrderGenerator
{
    private static readonly string[] Customers =
    {
        "Acme Corp", "Globex", "Initech", "Umbrella", "Hooli", "Stark Industries", "Wayne Enterprises", "Wonka Industries",
        "Cyberdyne", "Soylent", "Tyrell Corp", "Massive Dynamic", "Pied Piper", "Vandelay Industries", "Oscorp",
        "Aperture Science", "Black Mesa", "Gringotts", "Monarch Solutions", "Nakatomi Trading"
    };
    private static readonly string[] Regions = { "North America", "South America", "Europe", "Asia Pacific", "Middle East", "Africa" };
    private static readonly string[] Products =
    {
        "Widget", "Gadget", "Sprocket", "Flux Capacitor", "Hyperdrive", "Quantum Chip", "Servo Motor", "Laser Module",
        "Gear Assembly", "Control Board", "Sensor Array", "Power Cell"
    };
    private static readonly string[] Priorities = { "Low", "Normal", "High", "Critical" };
    private static readonly string[] Reps = { "Avery", "Jordan", "Riley", "Morgan", "Casey", "Quinn", "Rowan", "Sage", "Emerson", "Hayden" };
    private static readonly string[] NoteBits = { "Expedite", "Gift wrap", "Call before delivery", "Fragile", "Bulk order", "", "", "" };

    public static Order[] Create(int count, int seed = 42)
    {
        var rnd = new Random(seed);
        var start = new DateTime(2022, 1, 1);
        var list = new Order[count];
        for (int i = 0; i < count; i++)
        {
            var qty = rnd.Next(1, 250);
            var price = Math.Round((decimal)(rnd.NextDouble() * 900 + 5), 2);
            var date = start.AddDays(rnd.Next(0, 1400));
            var status = (OrderStatus)rnd.Next(0, 6);
            list[i] = new Order
            {
                Id = i + 1,
                Customer = Customers[rnd.Next(Customers.Length)],
                Region = Regions[rnd.Next(Regions.Length)],
                Product = Products[rnd.Next(Products.Length)],
                Status = status,
                Priority = Priorities[rnd.Next(Priorities.Length)],
                OrderDate = date,
                ShipDate = status is OrderStatus.Shipped or OrderStatus.Delivered ? date.AddDays(rnd.Next(1, 20)) : null,
                Quantity = qty,
                UnitPrice = price,
                Amount = qty * price,
                Discount = Math.Round(rnd.NextDouble() * 0.3, 3),
                Paid = rnd.Next(3) > 0,
                SalesRep = Reps[rnd.Next(Reps.Length)],
                Notes = NoteBits[rnd.Next(NoteBits.Length)]
            };
        }
        return list;
    }
}
