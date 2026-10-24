using DisctalkClient.Core.Security;

namespace DisctalkClient.Tests.Security;

public class SecretRedactorTests
{
    [Fact]
    public void Redacts_bot_tokens_webhook_tokens_and_auth_headers()
    {
        var webhookToken = new string('x', 68);
        var text = $"token={TestData.Token} url=https://discord.com/api/v10/webhooks/123/{webhookToken}?wait=true Authorization: Bot abc.def.ghi";

        var redacted = SecretRedactor.Redact(text);

        Assert.DoesNotContain(TestData.Token, redacted);
        Assert.DoesNotContain(webhookToken, redacted);
        Assert.DoesNotContain("abc.def.ghi", redacted);
        Assert.Contains("webhooks/123/[REDACTED]", redacted);
    }

    [Theory]
    [InlineData("Unknown Message")]
    [InlineData("see https://discord.com/channels/1/2/3")]
    [InlineData("v1.2.3")]
    public void Leaves_ordinary_text_alone(string text) => Assert.Equal(text, SecretRedactor.Redact(text));
}

public class BotTokenValidatorTests
{
    [Fact]
    public void Accepts_token_shaped_strings_and_strips_bot_prefix()
    {
        Assert.Null(BotTokenValidator.Validate(TestData.Token));
        Assert.Null(BotTokenValidator.Validate("Bot " + TestData.Token));
        Assert.Equal(TestData.Token, BotTokenValidator.Normalize($"  Bot {TestData.Token} "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("aaaa.bbbb")]
    [InlineData("Zm9vYmFy.cccccc.dddddddddddddddddddddddddddddddd")] // first part isn't a numeric id
    [InlineData("0123456789abcdef0123456789abcdef")]                   // client secret shape
    public void Rejects_junk_without_echoing_it(string token)
    {
        var error = BotTokenValidator.Validate(token);
        Assert.NotNull(error);
        if (token.Length > 3) Assert.DoesNotContain(token, error);
    }
}
