using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace OrderProcessing.Models;

public enum OrderStatus
{
	Pending,
	Processed,
	Failed
}

public class Order
{
	[BsonId]
	public Guid Id { get; set; }

	public string CustomerId { get; set; } = string.Empty;
	public List<OrderItem> Items { get; set; } = new();
	public decimal TotalAmount { get; set; }
	public OrderStatus Status { get; set; } = OrderStatus.Pending;
	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
	public DateTime? ProcessedAtUtc { get; set; }

	// Enriched fields (for demo)
	public decimal AppliedDiscount { get; set; }
	public string? Notes { get; set; }
}


