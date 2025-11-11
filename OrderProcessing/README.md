##OrderProcessing service

## Overview
.NET 8 microservice with asynchronous order processing:
- `POST /orders` accepts an order and immediately responds `202 Accepted` after enqueuing to RabbitMQ.
- A background worker (`OrderWorker`) consumes messages, inserts the order into MongoDB as Pending, validates inventory,
reserves items in inventory, computes totals and optional promo discounts, and marks it Processed (or Failed).
- `GET /orders/{id}` returns the order document.
- `GET /metrics` returns a simple processed counter.

## Run with Docker Compose
Prereqs: Docker Desktop

```bash
docker compose up --build
```

Services:
- API: http://localhost:8080
- MongoDB: mongodb://admin:admin@localhost:27017/?authSource=admin
- RabbitMQ UI: http://localhost:15672 (guest/guest)

## Endpoints
- Insert an inventory item:

```bash
curl -X POST http://localhost:8080/inventory \
  -H "Content-Type: application/json" \
  -d '{ "name":"Inventory1", "unitPrice": 12.5, "availableQuantity": 100 }'
```
Response contains the generated inventory `id`.

- Submit an order (referencing inventory item IDs):

```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{
        "customerId":"123",
        "items":[
          {"inventoryItemId":"11111111-1111-1111-1111-111111111111","quantity":2},
          {"inventoryItemId":"22222222-2222-2222-2222-222222222222","quantity":1}
        ],
        "promoCode":"hello"
      }'
```
Response:
```json
{ "orderId": "...", "status": "Pending" }
```

- Get order by id:
```bash
curl http://localhost:8080/orders/<orderId>
```

- Metrics (very basic):
```bash
curl http://localhost:8080/metrics
```
Returns:
```json
{ "processedOrders": 5 }
```

## Local Development (without Docker)
1) Ensure MongoDB and RabbitMQ are running locally (`mongodb://admin:admin@localhost:27017/?authSource=admin`, RabbitMQ at `localhost:5672` user `guest/guest`).  
2) From the `OrderProcessing` directory:
```bash
dotnet run
```
API available at `http://localhost:5084` or as printed in console.

## Configuration
Configuration is via `appsettings.*.json` or environment variables:
- Mongo: `Mongo:ConnectionString`, `Mongo:Database`
- Rabbit: `Rabbit:HostName`, `Rabbit:Port`, `Rabbit:UserName`, `Rabbit:Password`, `Rabbit:QueueName`

## Design decisions and trade-offs
- Asynchronous processing via RabbitMQ: separates API from business logic, it ensures that every order is eventually processed.
Because the system guarantees “at-least-once” delivery, the same order might occasionally be processed more than once, so idempotency should be considered.
- Worker inserts orders: handles saving, keeping the endpoint light and supporting retries before data is stored.
- Inventory by ID: orders use item IDs, and the worker saves the current name and price for audit purposes.
- Discount logic: simple promo (`hello`) applied, made to have some business logic.
- Observability: minimal metric (`processedOrders`) for simplicity; production should add structured logs, tracing.

## Assumptions
- Inventory items are pre-created (via POST /inventory) and referenced by GUID in orders.
- Simple stock reservation: decrement count per item.
- Authentication/authorization is not implemented.
- Other logging can be implemented, but it's console for now in order to make it simple.

## Notes
- This is a minimal demo. Retries, idempotency, validation, auth, tracing/metrics is intentionally simplified.

