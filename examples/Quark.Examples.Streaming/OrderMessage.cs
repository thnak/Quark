namespace Quark.Examples.Streaming;

public class OrderMessage
{
    public string OrderId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public decimal TotalAmount { get; set; }
}