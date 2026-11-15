using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Shop;

public sealed class ShopTests(PostgresServer server)
{
    private const string Contract = "user-next";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    private static object Cart(string method, long total, params (string Id, int Qty)[] items) => new
    {
        items = items.Select(i => new { productId = i.Id, quantity = i.Qty }).ToArray(),
        deliveryMethod = method,
        addressId = method == "homeDelivery" ? "adr_1" : null,
        vendorId = method == "clickAndCollect" ? "vnd_nugegoda" : null,
        contactName = "Nadeesha Perera",
        contactMobile = SignIn.NadeeshaMobile,
        quoteTotalCents = total,
    };

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private static Task<int> StockAsync(TestApi api, string id) =>
        api.DbAsync(db => db.Products.Where(p => p.Id == id).Select(p => p.Stock).SingleAsync());

    [Fact]
    public async Task The_shop_lists_pieces_with_filters_and_detail()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        var categories = await client.GetJsonAsync("/api/v1/shop/categories");
        categories.MatchesContract(Contract, "GET /shop/categories");
        Assert.Equal((2, 345000L), (categories.GetProperty("newTodayCount").GetInt32(), categories.GetProperty("maxPriceCents").GetInt64()));

        var all = await client.GetJsonAsync("/api/v1/shop/products");
        all.MatchesContract(Contract, "GET /shop/products");
        Assert.Equal((9, false), (all.GetProperty("totalCount").GetInt32(), all.GetProperty("hasMore").GetBoolean()));

        var women = await client.GetJsonAsync("/api/v1/shop/products?gender=women&sort=priceAsc");
        var prices = women.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("priceCents").GetInt64()).ToList();
        Assert.Equal(prices.Order(), prices);
        Assert.All(women.GetProperty("items").EnumerateArray(), p => Assert.Contains(p.Str("gender"), new[] { "women", "unisex" }));
        Assert.Equal(["prd_1", "prd_2"], (await client.GetJsonAsync("/api/v1/shop/products?newToday=true")).GetProperty("items")
            .EnumerateArray().Select(p => p.Str("id")).Order());
        Assert.All((await client.GetJsonAsync("/api/v1/shop/products?sizes=XL,XXL")).GetProperty("items").EnumerateArray(),
            p => Assert.Contains(p.Str("id"), new[] { "prd_1", "prd_2" }));
        Assert.Equal(2, (await client.GetJsonAsync("/api/v1/shop/products?search=denim&pageSize=2")).GetProperty("items").GetArrayLength());

        var jacket = await client.GetJsonAsync("/api/v1/shop/products/prd_1");
        jacket.MatchesContract(Contract, "GET /shop/products/{id}");
        Assert.Equal(("M", true, "Cut from 4 pairs of collected jeans, re-stitched in Moratuwa."),
            (jacket.Str("size"), jacket.GetProperty("isNewToday").GetBoolean(), jacket.Str("description")));
        Assert.All(jacket.GetProperty("related").EnumerateArray(), r => Assert.Equal("women", r.Str("categoryId")));
        await (await client.GetAsync("/api/v1/shop/products/prd_9")).ErrorAsync(HttpStatusCode.NotFound, "NOT_FOUND");

        var vendors = await client.GetJsonAsync("/api/v1/shop/franchise-vendors?lat=6.9022&lng=79.853");
        vendors.MatchesContract(Contract, "GET /shop/franchise-vendors");
        Assert.Equal("vnd_kollupitiya", vendors.GetProperty("items")[0].Str("id"));
        Assert.Equal(JsonValueKind.Null, (await client.GetJsonAsync("/api/v1/shop/franchise-vendors")).GetProperty("items")[0].GetProperty("distanceKm").ValueKind);
    }

    [Fact]
    public async Task Quotes_price_the_cart_and_flag_what_has_gone()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        var quote = await client.PostJsonAsync("/api/v1/orders/quote", new
        {
            items = new[] { new { productId = "prd_1", quantity = 1 }, new { productId = "prd_9", quantity = 1 } },
            deliveryMethod = "clickAndCollect",
            vendorId = "vnd_nugegoda",
        });
        quote.MatchesContract(Contract, "POST /orders/quote");
        Assert.Equal((345000L, 25000L, 370000L), (quote.GetProperty("subtotalCents").GetInt64(),
            quote.GetProperty("transportChargeCents").GetInt64(), quote.GetProperty("totalCents").GetInt64()));
        Assert.Equal(["prd_9"], quote.GetProperty("unavailableItemIds").EnumerateArray().Select(i => i.GetString()));
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("shippingFeeCents").ValueKind);

        var home = await client.PostJsonAsync("/api/v1/orders/quote", new
        {
            items = new[] { new { productId = "prd_1", quantity = 1 } },
            deliveryMethod = "homeDelivery",
            addressId = "adr_1",
        });
        Assert.Equal((35000L, 380000L), (home.GetProperty("shippingFeeCents").GetInt64(), home.GetProperty("totalCents").GetInt64()));

        var nothing = await client.PostJsonAsync("/api/v1/orders/quote", new
        {
            items = new[] { new { productId = "prd_9", quantity = 1 } },
            deliveryMethod = "homeDelivery",
        });
        Assert.Equal(JsonValueKind.Null, nothing.GetProperty("shippingFeeCents").ValueKind);
        await (await client.PostAsJsonAsync("/api/v1/orders/quote", new
        {
            items = new[] { new { productId = "prd_1", quantity = 1 } },
            deliveryMethod = "clickAndCollect",
            vendorId = "vnd_nope",
        })).ErrorAsync(HttpStatusCode.Conflict, "VENDOR_UNAVAILABLE");
    }

    [Fact]
    public async Task An_order_holds_its_pieces_until_the_card_payment_is_confirmed()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var staff = await api.CallCentreAsync();
        var key = Guid.NewGuid().ToString();

        var placed = await (await PlaceAsync(client, Cart("homeDelivery", 380000, ("prd_1", 1)), key)).JsonAsync(HttpStatusCode.Created);
        placed.MatchesContract(Contract, "POST /orders");
        Assert.Equal(("ORD-5135", 380000L, true), (placed.Str("orderNumber"), placed.GetProperty("totalCents").GetInt64(),
            placed.GetProperty("paymentRequired").GetBoolean()));
        Assert.Equal(5, await StockAsync(api, "prd_1"));
        Assert.Equal("ORD-5135", (await (await PlaceAsync(client, Cart("homeDelivery", 380000, ("prd_1", 1)), key)).JsonAsync(HttpStatusCode.Created)).Str("orderId"));
        await (await PlaceAsync(client, Cart("homeDelivery", 380000, ("prd_1", 2)), key)).ErrorAsync(HttpStatusCode.Conflict, "DUPLICATE_REQUEST");

        // Not an order for the call centre until it's paid.
        await (await staff.GetAsync("/api/v1/admin/orders/ORD-5135")).ErrorAsync(HttpStatusCode.NotFound, "ORDER_NOT_FOUND");
        var order = await client.GetJsonAsync("/api/v1/orders/ORD-5135");
        order.MatchesContract(Contract, "GET /orders/{id}");
        Assert.Equal(("placed", "pending", "adr_1"), (order.Str("status"), order.Str("paymentStatus"), order.GetProperty("address").Str("id")));

        await (await client.PostAsJsonAsync("/api/v1/orders/ORD-5135/payment-confirmation", new { status = "succeeded", paymentReference = "forged" }))
            .ErrorAsync(HttpStatusCode.PaymentRequired, "PAYMENT_FAILED");
        Assert.Equal("failed", (await client.PostJsonAsync("/api/v1/orders/ORD-5135/payment-confirmation", new { status = "cancelled" })).Str("paymentStatus"));
        var paid = await client.PostJsonAsync("/api/v1/orders/ORD-5135/payment-confirmation", new { status = "succeeded", paymentReference = "mock_7f3a9c" });
        Assert.Equal("paid", paid.Str("paymentStatus"));
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.Id == "ntf_orderPlaced_ORD-5135")));

        await staff.PostJsonAsync("/api/v1/admin/orders/ORD-5135/status", new { status = "confirmed" });
        await staff.PostJsonAsync("/api/v1/admin/orders/ORD-5135/status", new { status = "outForDelivery" });
        await staff.PostJsonAsync("/api/v1/admin/orders/ORD-5135/status", new { status = "completed" });
        var delivered = await client.GetJsonAsync("/api/v1/orders/ORD-5135");
        Assert.Equal(["placed", "confirmed", "outForDelivery", "delivered"], delivered.GetProperty("statusHistory").EnumerateArray().Select(s => s.Str("status")));

        var list = await client.GetJsonAsync("/api/v1/orders?page=1&pageSize=20");
        list.MatchesContract(Contract, "GET /orders");
        Assert.Equal("ORD-5135", list.GetProperty("items")[0].Str("id"));
    }

    [Fact]
    public async Task Orders_are_refused_for_sold_out_pieces_changed_prices_or_no_address()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var error = await (await PlaceAsync(client, Cart("homeDelivery", 380000, ("prd_1", 1), ("prd_9", 1))))
            .ErrorAsync(HttpStatusCode.Conflict, "OUT_OF_STOCK");
        Assert.Equal(["prd_9"], error.GetProperty("details").GetProperty("productIds").EnumerateArray().Select(i => i.GetString()));
        var changed = await (await PlaceAsync(client, Cart("homeDelivery", 300000, ("prd_1", 1)))).ErrorAsync(HttpStatusCode.Conflict, "PRICE_CHANGED");
        Assert.Equal(380000, changed.GetProperty("details").GetProperty("totalCents").GetInt64());
        await (await PlaceAsync(client, new
        {
            items = new[] { new { productId = "prd_1", quantity = 1 } },
            deliveryMethod = "homeDelivery",
            addressId = "adr_2",
            quoteTotalCents = 380000,
        })).ErrorAsync(HttpStatusCode.UnprocessableEntity, "ADDRESS_REQUIRED");
        Assert.Equal(6, await StockAsync(api, "prd_1"));
    }

    [Fact]
    public async Task Unpaid_orders_are_released_after_thirty_minutes()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();
        var placed = await (await PlaceAsync(client, Cart("clickAndCollect", 100000, ("prd_7", 1)))).JsonAsync(HttpStatusCode.Created);
        Assert.Equal(29, await StockAsync(api, "prd_7"));

        api.Clock.Advance(TimeSpan.FromMinutes(31));
        var staff = await api.CallCentreAsync();
        await staff.GetJsonAsync("/api/v1/admin/counts");
        Assert.Equal(30, await StockAsync(api, "prd_7"));
        var order = await client.GetJsonAsync($"/api/v1/orders/{placed.Str("orderId")}");
        Assert.Equal(("cancelled", JsonValueKind.Null), (order.Str("status"), order.GetProperty("estimatedDeliveryDate").ValueKind));
    }

    [Fact]
    public async Task Two_shoppers_cannot_buy_the_last_one()
    {
        await using var api = await TestApi.StartAsync(server);
        await api.DbAsync(db => db.Products.Where(p => p.Id == "prd_3").ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, 1)));
        var a = await api.CustomerAsync("+94700000011");
        var b = await api.CustomerAsync("+94700000012");
        var body = new
        {
            items = new[] { new { productId = "prd_3", quantity = 1 } },
            deliveryMethod = "clickAndCollect",
            vendorId = "vnd_nugegoda",
            quoteTotalCents = 120000,
        };
        var results = await Task.WhenAll(PlaceAsync(a, body), PlaceAsync(b, body));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        await results.Single(r => r.StatusCode != HttpStatusCode.Created).ErrorAsync(HttpStatusCode.Conflict, "OUT_OF_STOCK");
        Assert.Equal(0, await StockAsync(api, "prd_3"));
    }

    [Fact]
    public async Task Cancelling_a_wallet_paid_order_refunds_it_and_restocks()
    {
        await using var api = await TestApi.StartAsync(server);
        var order = await api.DbAsync(async db =>
        {
            var o = await db.Orders.Include(x => x.Items).SingleAsync(x => x.Id == "ORD-5133");
            o.PaymentMethod = PaymentMethod.Wallet;
            db.WalletTransactions.Add(new WalletTransaction
            {
                Id = "txn_ORD-5133",
                CustomerId = o.CustomerId,
                Type = TransactionType.ShopOrder,
                Status = TransactionStatus.Completed,
                Title = "Order ORD-5133",
                AmountCents = -o.TotalCents,
                OccurredAt = o.CreatedAt,
                OrderId = o.Id,
            });
            await db.SaveChangesAsync();
            return o;
        });
        var stockBefore = await StockAsync(api, order.Items[0].ProductId);
        var staff = await api.CallCentreAsync();
        await staff.PostJsonAsync("/api/v1/admin/orders/ORD-5133/status", new { status = "cancelled" });
        Assert.True(await api.DbAsync(db => db.WalletTransactions.AnyAsync(t => t.Id == "txn_ORD-5133_refund" && t.AmountCents == order.TotalCents)));
        Assert.Equal(stockBefore + order.Items.Where(i => i.ProductId == order.Items[0].ProductId).Sum(i => i.Quantity),
            await StockAsync(api, order.Items[0].ProductId));
    }

    [Fact]
    public async Task Staff_confirm_send_and_complete_orders()
    {
        await using var api = await TestApi.StartAsync(server);
        var staff = await api.CallCentreAsync();

        var list = await staff.GetJsonAsync("/api/v1/admin/orders");
        list.MatchesContract("admin", "GET /admin/orders");
        Assert.Equal("ORD-5134", list.GetProperty("items")[0].Str("id"));
        var placed = await staff.GetJsonAsync("/api/v1/admin/orders?status=placed");
        Assert.All(placed.GetProperty("items").EnumerateArray(), o => Assert.Equal("placed", o.Str("status")));
        Assert.Equal(1, (await staff.GetJsonAsync("/api/v1/admin/orders?q=ord-5133")).GetProperty("totalCount").GetInt32());

        var order = await staff.GetJsonAsync("/api/v1/admin/orders/ORD-5134");
        order.MatchesContract("admin", "GET /admin/orders/{id}");
        var home = order.Str("fulfilmentType") == "homeDelivery";

        var confirmed = await staff.PostJsonAsync("/api/v1/admin/orders/ORD-5134/status", new { status = "confirmed" });
        Assert.Matches(@"^WB-261107-\d{4}$", confirmed.Str("waybillNumber"));
        Assert.Equal("Kasun Jayawardena", confirmed.GetProperty("timeline").EnumerateArray().Last().Str("actor"));
        await (await staff.PostAsJsonAsync("/api/v1/admin/orders/ORD-5134/status", new { status = home ? "readyAtVendor" : "outForDelivery" }))
            .ErrorAsync(HttpStatusCode.Conflict, "ORDER_STATUS_CHANGED");
        await staff.PostJsonAsync("/api/v1/admin/orders/ORD-5134/status", new { status = home ? "outForDelivery" : "readyAtVendor" });
        var done = await staff.PostJsonAsync("/api/v1/admin/orders/ORD-5134/status", new { status = "completed" });
        Assert.Equal("completed", done.Str("status"));
        await (await staff.PostAsJsonAsync("/api/v1/admin/orders/ORD-5134/status", new { status = "cancelled" }))
            .ErrorAsync(HttpStatusCode.Conflict, "ORDER_STATUS_CHANGED");

        var customerId = order.GetProperty("customer").Str("id");
        Assert.Equal(3, await api.DbAsync(db => db.Notifications.CountAsync(n => n.RecipientId == customerId && n.Type == "orderUpdate")));
        await (await staff.GetAsync("/api/v1/admin/orders/ORD-1")).ErrorAsync(HttpStatusCode.NotFound, "ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task Admins_manage_the_catalogue()
    {
        await using var api = await TestApi.StartAsync(server);
        await (await (await api.CallCentreAsync()).GetAsync("/api/v1/admin/products")).ErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        var admin = await api.AdminAsync();

        var list = await admin.GetJsonAsync("/api/v1/admin/products");
        list.MatchesContract("admin", "GET /admin/products");
        Assert.Equal(10, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(["prd_9"], (await admin.GetJsonAsync("/api/v1/admin/products?status=hidden")).GetProperty("items").EnumerateArray().Select(p => p.Str("id")));
        Assert.Equal(["prd_1", "prd_2"], (await admin.GetJsonAsync("/api/v1/admin/products?uploadedOn=2026-11-07")).GetProperty("items").EnumerateArray().Select(p => p.Str("id")).Order());
        Assert.Equal(3, (await admin.GetJsonAsync("/api/v1/admin/products?q=DENIM")).GetProperty("totalCount").GetInt32());

        var created = await admin.PostJsonAsync("/api/v1/admin/products", ContractSamples.Request("admin", "POST /admin/products"), HttpStatusCode.Created);
        ShapeAssert.Matches(ContractSamples.Section("admin", "Catalogue (Admin)")[0].Json, created);
        var bad = await (await admin.PostAsJsonAsync("/api/v1/admin/products", new
        {
            name = "A",
            category = "men",
            priceCents = 0,
            sizes = Array.Empty<string>(),
            stock = 1,
            imageUrls = new[] { "a", "b", "c", "d", "e", "f" },
        })).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        Assert.Equal(["imageUrls", "name", "priceCents", "sizes"], bad.GetProperty("details").EnumerateObject().Select(p => p.Name).Order());

        var soldOut = await (await admin.PatchAsJsonAsync($"/api/v1/admin/products/{created.Str("id")}", new { stock = 0 })).JsonAsync();
        Assert.Equal(0, soldOut.GetProperty("stock").GetInt32());
        Assert.Contains((await admin.GetJsonAsync("/api/v1/admin/products?status=outOfStock")).GetProperty("items").EnumerateArray(), p => p.Str("id") == created.Str("id"));
        var customer = await api.CustomerAsync();
        Assert.Equal(0, (await customer.GetJsonAsync($"/api/v1/shop/products/{created.Str("id")}")).GetProperty("stock").GetInt32());
        await (await admin.GetAsync("/api/v1/admin/products/prd_nope")).ErrorAsync(HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    [Fact]
    public async Task Catalogue_photos_upload_and_are_served_publicly()
    {
        await using var api = await TestApi.StartAsync(server);
        var admin = await api.AdminAsync();
        static MultipartFormDataContent File(byte[] bytes) => new() { { new ByteArrayContent(bytes), "file", "photo.png" } };

        var upload = await (await admin.PostAsync("/api/v1/admin/uploads", File(Png))).JsonAsync(HttpStatusCode.Created);
        upload.MatchesContract("admin", "POST /admin/uploads");
        Assert.StartsWith("img_", upload.Str("id"), StringComparison.Ordinal);

        var served = await api.Client().GetAsync(new Uri(upload.Str("url")).AbsolutePath);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());

        await (await admin.PostAsync("/api/v1/admin/uploads", File("not an image"u8.ToArray())))
            .ErrorAsync(HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_FILE_TYPE");
        var big = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(big, 0);
        await (await admin.PostAsync("/api/v1/admin/uploads", File(big))).ErrorAsync(HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client().GetAsync("/files/pho_secret")).StatusCode);
    }
}
