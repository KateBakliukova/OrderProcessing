using MongoDB.Driver;
using OrderProcessing.Background;
using OrderProcessing.Data;
using OrderProcessing.Messaging;
using OrderProcessing.Metrics;
using OrderProcessing.Models;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));
builder.Services.Configure<RabbitSettings>(builder.Configuration.GetSection("Rabbit"));

// Services
builder.Services.AddSingleton<IMongoContext, MongoContext>();
builder.Services.AddSingleton<IOrderQueuePublisher, OrderQueuePublisher>();
builder.Services.AddSingleton<IInventoryRepository, InventoryRepository>();
builder.Services.AddHostedService<OrderWorker>();

var app = builder.Build();

// HTTP endpoints
// Enqueue order for asynchronous processing. The worker will persist and process it.
app.MapPost("/orders", (CreateOrderRequest request, IOrderQueuePublisher publisher) =>
{
	if (string.IsNullOrWhiteSpace(request.CustomerId) || request.Items is null || request.Items.Count == 0)
	{
		return Results.BadRequest(new { error = "CustomerId and Items are required" });
	}

	// Create correlation/order id and enqueue for processing (worker will persist)
	var orderId = Guid.NewGuid();
	var msg = new CreateOrderMessage(
		orderId,
		request.CustomerId,
		request.Items.Select(i => new CreateOrderItemRefMessage(i.InventoryItemId, i.Quantity)).ToList(),
		request.PromoCode
	);
	publisher.PublishOrder(msg);

	return Results.Accepted($"/orders/{orderId}", new { orderId, status = OrderStatus.Pending.ToString() });
});

// Query the current state of an order by its Guid id.
app.MapGet("/orders/{id}", async (Guid id, IMongoContext mongo) =>
{
	var orders = mongo.Database.GetCollection<Order>("orders");
	var order = await orders.Find(x => x.Id == id).FirstOrDefaultAsync();
	return order is null ? Results.NotFound() : Results.Ok(order);
});

app.MapGet("/metrics", () =>
{
	return Results.Ok(new { processedOrders = ProcessingCounters.GetProcessed() });
});

// Inventory insert endpoint
// Create new inventory item with generated Guid id.
app.MapPost("/inventory", async (InventoryCreateRequest req, IInventoryRepository repo) =>
{
	if (string.IsNullOrWhiteSpace(req.Name) || req.UnitPrice < 0 || req.AvailableQuantity < 0)
	{
		return Results.BadRequest(new { error = "Name is required; UnitPrice and AvailableQuantity must be >= 0" });
	}

	var item = new InventoryItem
	{
		Id = Guid.NewGuid(),
		Name = req.Name,
		UnitPrice = req.UnitPrice,
		AvailableQuantity = req.AvailableQuantity
	};

	await repo.CreateAsync(item);
	return Results.Created($"/inventory/{item.Id}", item);
});

app.Run();
