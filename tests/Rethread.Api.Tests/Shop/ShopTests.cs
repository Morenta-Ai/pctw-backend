using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Data.Entities;
using Rethread.Api.Tests.Contract;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Shop;

public sealed class ShopTests(PostgresServer server)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    /// <summary>The customer with the most in their wallet, so wallet orders go through.</summary>
    private static async Task<HttpClient> RichCustomerAsync(TestApi api)
    {
        var mobile = await api.DbAsync(async db =>
        {
            var active = db.Customers.Where(c => c.Status == AccountStatus.Active).Select(c => c.Id);
            var id = await db.WalletTransactions.Where(t => t.Status != TransactionStatus.Failed && active.Contains(t.CustomerId))
                .GroupBy(t => t.CustomerId).OrderByDescending(g => g.Sum(t => t.AmountCents)).Select(g => g.Key).FirstAsync();
            return await db.Customers.Where(c => c.Id == id).Select(c => c.Mobile!).SingleAsync();
        });
        return await api.CustomerAsync(mobile);
    }

    private static async Task<string> DefaultAddressAsync(HttpClient client) =>
        (await client.GetJsonAsync("/api/v1/addresses")).GetProperty("items")[0].Str("id");

    private static object Order(string productId, string size, int quantity, string addressId, string payment = "wallet") => new
    {
        items = new[] { new { productId, size, quantity } },
        fulfilmentType = "homeDelivery",
        addressId,
        vendorId = (string?)null,
        paymentMethod = payment,
    };

    [Fact]
    public async Task The_shop_shows_published_stock_with_filters_and_sorting()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await api.CustomerAsync();

        var all = await client.GetJsonAsync("/api/v1/products");
        Assert.Equal(9, all.GetProperty("items").GetArrayLength());
        foreach (var p in all.GetProperty("items").EnumerateArray()) p.MatchesContract("user", "GET /products/{id}");
        var jacket = await client.GetJsonAsync("/api/v1/products/prd_1");
        Assert.Equal(("M", true), (jacket.Str("defaultSize"), jacket.GetProperty("isAvailable").GetBoolean()));

        var women = await client.GetJsonAsync("/api/v1/products?category=women&maxPriceCents=300000");
        Assert.All(women.GetProperty("items").EnumerateArray(), p =>
            Assert.True(p.Str("category") == "women" && p.GetProperty("priceCents").GetInt64() <= 300000));
        var cheapest = await client.GetJsonAsync("/api/v1/products?sort=priceAsc");
        var prices = cheapest.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("priceCents").GetInt64()).ToList();
        Assert.Equal(prices.Order(), prices);

        await (await client.GetAsync("/api/v1/products/prd_9")).ErrorAsync(HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
        await (await client.GetAsync("/api/v1/products?sort=random")).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");

        var options = await client.GetJsonAsync("/api/v1/fulfilment-options");
        options.MatchesContract("user", "GET /fulfilment-options");
        Assert.Equal((35000L, 25000L), (options.GetProperty("homeDeliveryChargeCents").GetInt64(), options.GetProperty("clickAndCollectChargeCents").GetInt64()));
    }

    [Fact]
    public async Task A_wallet_order_takes_stock_and_money_and_alerts_the_call_centre()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await RichCustomerAsync(api);
        var balance = (await client.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64();

        var body = JsonNode.Parse(ContractSamples.Request("user", "POST /orders").GetRawText())!;
        body["addressId"] = await DefaultAddressAsync(client);
        var order = await (await client.PostAsJsonAsync("/api/v1/orders", body)).JsonAsync(HttpStatusCode.Created);
        order.MatchesContract("user", "GET /orders/{id}");
        Assert.Equal(("placed", 345000L, 35000L, 380000L), (order.Str("status"), order.GetProperty("subtotalCents").GetInt64(),
            order.GetProperty("transportChargeCents").GetInt64(), order.GetProperty("totalCents").GetInt64()));
        Assert.Equal("ORD-5135", order.Str("id"));

        Assert.Equal(balance - 380000, (await client.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64());
        Assert.Equal(5, await api.DbAsync(db => db.Products.Where(p => p.Id == "prd_1").Select(p => p.Stock).SingleAsync()));
        Assert.True(await api.DbAsync(db => db.Notifications.AnyAsync(n => n.Id == "ntf_orderPlaced_ORD-5135")));

        Assert.Equal("ORD-5135", (await client.GetJsonAsync("/api/v1/orders")).GetProperty("items")[0].Str("id"));
        (await client.GetJsonAsync("/api/v1/orders/ORD-5135")).MatchesContract("user", "GET /orders/{id}");
        await (await (await api.CustomerAsync()).GetAsync("/api/v1/orders/ORD-5135")).ErrorAsync(HttpStatusCode.NotFound, "ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task Orders_are_refused_for_missing_stock_money_or_destination()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await RichCustomerAsync(api);
        var address = await DefaultAddressAsync(client);

        await (await client.PostAsJsonAsync("/api/v1/orders", Order("prd_9", "M", 1, address))).ErrorAsync(HttpStatusCode.Conflict, "PRODUCT_UNAVAILABLE");
        await (await client.PostAsJsonAsync("/api/v1/orders", Order("prd_6", "M", 5, address))).ErrorAsync(HttpStatusCode.Conflict, "PRODUCT_UNAVAILABLE");
        await (await client.PostAsJsonAsync("/api/v1/orders", Order("prd_1", "XXXL", 1, address))).ErrorAsync(HttpStatusCode.Conflict, "PRODUCT_UNAVAILABLE");
        Assert.Equal(4, await api.DbAsync(db => db.Products.Where(p => p.Id == "prd_6").Select(p => p.Stock).SingleAsync()));

        var poor = await api.CustomerAsync("+94700000009");
        var home = await poor.PostJsonAsync("/api/v1/addresses", new { label = "home", line = "1 Galle Road, Dehiwala", latitude = 6.85, longitude = 79.86, isDefault = true }, HttpStatusCode.Created);
        await (await poor.PostAsJsonAsync("/api/v1/orders", Order("prd_1", "M", 1, home.Str("id")))).ErrorAsync(HttpStatusCode.Conflict, "INSUFFICIENT_BALANCE");
        Assert.Equal(6, await api.DbAsync(db => db.Products.Where(p => p.Id == "prd_1").Select(p => p.Stock).SingleAsync()));

        var card = await poor.PostJsonAsync("/api/v1/orders", Order("prd_1", "M", 1, home.Str("id"), "card"), HttpStatusCode.Created);
        Assert.Equal(PaymentStatus.Unpaid, await api.DbAsync(db => db.Orders.Where(o => o.Id == card.Str("id")).Select(o => o.PaymentStatus).SingleAsync()));

        await (await poor.PostAsJsonAsync("/api/v1/orders", Order("prd_1", "M", 1, address))).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        await (await poor.PostAsJsonAsync("/api/v1/orders", new
        {
            items = new[] { new { productId = "prd_1", size = "M", quantity = 1 } },
            fulfilmentType = "clickAndCollect",
            addressId = (string?)null,
            vendorId = "vnd_nope",
            paymentMethod = "card",
        })).ErrorAsync(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Two_shoppers_cannot_buy_the_last_one()
    {
        await using var api = await TestApi.StartAsync(server);
        await api.DbAsync(db => db.Products.Where(p => p.Id == "prd_3").ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, 1)));
        var a = await api.CustomerAsync("+94700000011");
        var b = await api.CustomerAsync("+94700000012");
        var orderBody = new
        {
            items = new[] { new { productId = "prd_3", size = "One size", quantity = 1 } },
            fulfilmentType = "clickAndCollect",
            addressId = (string?)null,
            vendorId = "vnd_nugegoda",
            paymentMethod = "card",
        };
        var results = await Task.WhenAll(a.PostAsJsonAsync("/api/v1/orders", orderBody), b.PostAsJsonAsync("/api/v1/orders", orderBody));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        await results.Single(r => r.StatusCode != HttpStatusCode.Created).ErrorAsync(HttpStatusCode.Conflict, "PRODUCT_UNAVAILABLE");
        Assert.Equal(0, await api.DbAsync(db => db.Products.Where(p => p.Id == "prd_3").Select(p => p.Stock).SingleAsync()));
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
    public async Task Cancelling_a_wallet_order_refunds_it_and_restocks()
    {
        await using var api = await TestApi.StartAsync(server);
        var client = await RichCustomerAsync(api);
        var balance = (await client.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64();
        var order = await client.PostJsonAsync("/api/v1/orders", Order("prd_2", "M", 2, await DefaultAddressAsync(client)), HttpStatusCode.Created);

        var staff = await api.CallCentreAsync();
        await staff.PostJsonAsync($"/api/v1/admin/orders/{order.Str("id")}/status", new { status = "cancelled" });
        Assert.Equal(balance, (await client.GetJsonAsync("/api/v1/wallet")).GetProperty("balanceCents").GetInt64());
        Assert.Equal(9, await api.DbAsync(db => db.Products.Where(p => p.Id == "prd_2").Select(p => p.Stock).SingleAsync()));
        Assert.Equal("cancelled", (await client.GetJsonAsync($"/api/v1/orders/{order.Str("id")}")).Str("status"));
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
        Assert.False((await customer.GetJsonAsync($"/api/v1/products/{created.Str("id")}")).GetProperty("isAvailable").GetBoolean());
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
