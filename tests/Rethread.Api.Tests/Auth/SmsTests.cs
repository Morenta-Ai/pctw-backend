using System.Net;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rethread.Api.Auth;
using Rethread.Api.Tests.Infrastructure;

namespace Rethread.Api.Tests.Auth;

public sealed class SmsTests(PostgresServer server)
{
    private static readonly NotifyLkOptions Account = new()
    {
        UserId = "33",
        ApiKey = "test-key-0123456789a",
        SenderId = "ReThread",
        BaseUrl = new Uri("https://notify.test/api/v1/"),
    };

    [Fact]
    public async Task Notify_lk_gets_the_number_without_a_plus_and_the_account_in_the_form()
    {
        var notifyLk = new FakeNotifyLk("""{"status":"success","data":"Sent"}""");
        await Sender(notifyLk).SendAsync("+94771234567", "123456 is your ReThread code.", TestContext.Current.CancellationToken);

        var (uri, form) = Assert.Single(notifyLk.Requests);
        Assert.Equal("https://notify.test/api/v1/send", uri.ToString());
        Assert.Empty(uri.Query);
        Assert.Equal(new Dictionary<string, string>
        {
            ["user_id"] = "33",
            ["api_key"] = "test-key-0123456789a",
            ["sender_id"] = "ReThread",
            ["to"] = "94771234567",
            ["message"] = "123456 is your ReThread code.",
        }, form);
    }

    [Theory]
    [InlineData("+94771234567", "94771234567")]
    [InlineData("0771234567", "94771234567")]
    [InlineData("94 77 123 4567", "94771234567")]
    public void Numbers_are_written_the_way_notify_lk_expects(string mobile, string expected) =>
        Assert.Equal(expected, NotifyLkSmsSender.ToNotifyLkNumber(mobile));

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"status":"error","errors":"api_key is invalid"}""")]
    [InlineData(HttpStatusCode.OK, """{"status":"error","errors":["The to field is required."]}""")]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad gateway</html>")]
    public async Task A_refused_message_is_reported_as_not_sent(HttpStatusCode status, string body)
    {
        var sender = Sender(new FakeNotifyLk(body, status));
        await Assert.ThrowsAsync<SmsNotSentException>(() =>
            sender.SendAsync("+94771234567", "text", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unreachable_notify_lk_is_reported_as_not_sent()
    {
        var sender = Sender(new FakeNotifyLk(_ => throw new HttpRequestException("no route")));
        await Assert.ThrowsAsync<SmsNotSentException>(() =>
            sender.SendAsync("+94771234567", "text", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Texts_go_through_notify_lk_only_when_its_api_key_is_set()
    {
        await using (var api = await TestApi.StartAsync(server))
        {
            Assert.IsType<LoggingSmsSender>(api.Factory.Services.GetRequiredService<ISmsSender>());
        }

        await using var configured = await TestApi.StartAsync(server, new Dictionary<string, string>
        {
            ["Sms:NotifyLk:UserId"] = "33",
            ["Sms:NotifyLk:ApiKey"] = "test-key-0123456789a",
        });
        Assert.IsType<NotifyLkSmsSender>(configured.Factory.Services.GetRequiredService<ISmsSender>());
    }

    [Fact]
    public async Task When_the_code_cannot_be_sent_the_customer_can_ask_again_straight_away()
    {
        await using var api = await TestApi.StartAsync(server);
        await using var failing = api.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<ISmsSender>(new FailingSmsSender())));
        var request = new { channel = "sms", destination = SignIn.NadeeshaMobile };

        var refused = await failing.CreateClient().PostAsJsonAsync("/api/v1/auth/otp/request", request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal(0, await api.DbAsync(db => db.OtpChallenges.CountAsync()));

        // Not held back by the resend wait, because the unsent code was dropped.
        var challenge = await api.Client().PostJsonAsync("/api/v1/auth/otp/request", request);
        Assert.StartsWith("otp_", challenge.Str("requestId"), StringComparison.Ordinal);
    }

    private static NotifyLkSmsSender Sender(FakeNotifyLk notifyLk) =>
        new(new HttpClient(notifyLk), Options.Create(Account), NullLogger<NotifyLkSmsSender>.Instance);

    private sealed class FailingSmsSender : ISmsSender
    {
        public Task SendAsync(string mobile, string text, CancellationToken ct) =>
            throw new SmsNotSentException("notify.lk is down");
    }

    private sealed class FakeNotifyLk(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public FakeNotifyLk(string body, HttpStatusCode status = HttpStatusCode.OK)
            : this(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) })
        {
        }

        public List<(Uri Uri, Dictionary<string, string> Form)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            var form = body.Split('&').Select(pair => pair.Split('=')).ToDictionary(
                p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p[1]));
            Requests.Add((request.RequestUri!, form));
            return respond(request);
        }
    }
}
