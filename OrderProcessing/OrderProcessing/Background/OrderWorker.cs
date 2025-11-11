using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OrderProcessing.Data;
using OrderProcessing.Models;
using OrderProcessing.Metrics;
using OrderProcessing.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderProcessing.Background;

public class OrderWorker : BackgroundService
{
	// Background worker that consumes order messages, validates/reserves inventory,
	// computes totals/discounts and updates MongoDB accordingly.
	private readonly ILogger<OrderWorker> _logger;
	private readonly IMongoContext _mongo;
	private readonly IInventoryRepository _inventory;
	private readonly RabbitSettings _rabbit;
	private IConnection? _connection;
	private IModel? _channel;

	public OrderWorker(ILogger<OrderWorker> logger, IMongoContext mongo, IInventoryRepository inventory, IOptions<RabbitSettings> rabbitOptions)
	{
		_logger = logger;
		_mongo = mongo;
		_inventory = inventory;
		_rabbit = rabbitOptions.Value;
	}

	public override Task StartAsync(CancellationToken cancellationToken)
	{
		// Initialize RabbitMQ connection/channel and declare a durable queue.
		// BasicQos(1) ensures this consumer handles one message at a time.
		var factory = new ConnectionFactory
		{
			HostName = _rabbit.HostName,
			Port = _rabbit.Port,
			UserName = _rabbit.UserName,
			Password = _rabbit.Password,
			DispatchConsumersAsync = true
		};
		_connection = factory.CreateConnection();
		_channel = _connection.CreateModel();
		_channel.QueueDeclare(_rabbit.QueueName, durable: true, exclusive: false, autoDelete: false);
		_channel.BasicQos(0, 1, false);

		return base.StartAsync(cancellationToken);
	}

	protected override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Register an async consumer that deserializes messages and processes orders.
		if (_channel == null) throw new InvalidOperationException("Channel not initialized");

		var consumer = new AsyncEventingBasicConsumer(_channel);
		consumer.Received += async (ch, ea) =>
		{
			try
			{
				var json = Encoding.UTF8.GetString(ea.Body.ToArray());
				var payload = JsonSerializer.Deserialize<CreateOrderEnvelope>(json);
				if (payload is null)
				{
					_logger.LogWarning("Received malformed message: {Json}", json);
					_channel.BasicAck(ea.DeliveryTag, false);
					return;
				}

				await ProcessOrderAsync(payload, stoppingToken);
				_channel.BasicAck(ea.DeliveryTag, false);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing message");
				// Nack and requeue to avoid losing
				_channel!.BasicNack(ea.DeliveryTag, false, true);
			}
		};

		_channel.BasicConsume(_rabbit.QueueName, autoAck: false, consumer: consumer);
		return Task.CompletedTask;
	}

	private async Task ProcessOrderAsync(CreateOrderEnvelope envelope, CancellationToken ct)
	{
		// Load the order or insert a new Pending one (the API only enqueues).
		var orders = _mongo.Database.GetCollection<Order>("orders");

		// Insert as Pending if not exists
		var existing = await orders.Find(x => x.Id == envelope.OrderId).FirstOrDefaultAsync(ct);
		if (existing is null)
		{
			var newOrder = new Order
			{
				Id = envelope.OrderId,
				CustomerId = envelope.CustomerId,
				Items = [],
				TotalAmount = 0m,
				Status = OrderStatus.Pending,
				CreatedAtUtc = DateTime.UtcNow
			};
			await orders.InsertOneAsync(newOrder, cancellationToken: ct);
			existing = newOrder;
		}

		var order = existing;
		_logger.LogInformation("Processing order {OrderId} for customer {CustomerId}", order.Id, order.CustomerId);

		// Validate items exist and reserve quantities
		var computedItems = new List<OrderItem>();
		decimal total = 0m;
		foreach (var reqItem in envelope.Items)
		{
			var inv = await _inventory.GetByIdAsync(reqItem.InventoryItemId);
			if (inv is null)
			{
				order.Status = OrderStatus.Failed;
				order.Notes = $"Inventory item not found: {reqItem.InventoryItemId}";
				order.ProcessedAtUtc = DateTime.UtcNow;
				await orders.ReplaceOneAsync(x => x.Id == order.Id, order, cancellationToken: ct);
				_logger.LogWarning("Order {OrderId} failed: inventory item {ItemId} not found", order.Id, reqItem.InventoryItemId);
				return;
			}

			var reserved = await _inventory.TryReserveAsync(inv.Id, reqItem.Quantity);
			if (!reserved)
			{
				order.Status = OrderStatus.Failed;
				order.Notes = $"Insufficient stock for item: {inv.Id}";
				order.ProcessedAtUtc = DateTime.UtcNow;
				await orders.ReplaceOneAsync(x => x.Id == order.Id, order, cancellationToken: ct);
				_logger.LogWarning("Order {OrderId} failed: insufficient stock for item {ItemId}", order.Id, inv.Id);
				return;
			}

			var capturedUnitPrice = inv.UnitPrice;
			total += capturedUnitPrice * reqItem.Quantity;
			computedItems.Add(new OrderItem
			{
				InventoryItemId = inv.Id,
				Name = inv.Name,
				Quantity = reqItem.Quantity,
				UnitPrice = capturedUnitPrice
			});
		}

		// Simulate business logic and apply discounts
		await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
		order.Items = computedItems;
		order.TotalAmount = total;
		var discount = string.Equals(envelope.PromoCode, "hello", StringComparison.OrdinalIgnoreCase)
			? order.TotalAmount * 0.1m
			: 0m;
		order.AppliedDiscount = discount;
		order.Notes = discount > 0 ? "10% discount applied (promo: hello)" : "No discount";
		order.Status = OrderStatus.Processed;
		order.ProcessedAtUtc = DateTime.UtcNow;

		await orders.ReplaceOneAsync(x => x.Id == order.Id, order);
		ProcessingCounters.IncrementProcessed();
		_logger.LogInformation("Order {OrderId} processed. Total: {Total} Discount: {Discount}", order.Id, order.TotalAmount, order.AppliedDiscount);
	}

	public override Task StopAsync(CancellationToken cancellationToken)
	{
		_channel?.Close();
		_connection?.Close();
		_channel?.Dispose();
		_connection?.Dispose();
		return base.StopAsync(cancellationToken);
	}

	private record CreateOrderEnvelope(Guid OrderId, string CustomerId, List<CreateOrderItemRefMessage> Items, string? PromoCode)
	{
		public record CreateOrderItemRef(Guid InventoryItemId, int Quantity);
	}
}



