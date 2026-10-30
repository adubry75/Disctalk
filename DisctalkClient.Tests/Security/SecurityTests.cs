using DisctalkClient.Core.Abstractions;
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

    [Fact]
    public void Redacts_user_tokens_in_headers_gateway_payloads_and_legacy_form()
    {
        var mfa = "mfa." + new string('Q', 84);
        var text = $"Authorization: {TestData.Token}\n{{\"op\":2,\"d\":{{\"token\":\"{mfa}\",\"capabilities\":16}}}} {mfa}";

        var redacted = SecretRedactor.Redact(text);

        Assert.DoesNotContain(TestData.Token, redacted);
        Assert.DoesNotContain(mfa, redacted);
        Assert.Contains("\"capabilities\":16", redacted);
    }

    [Theory]
    [InlineData("Unknown Message")]
    [InlineData("see https://discord.com/channels/1/2/3")]
    [InlineData("v1.2.3")]
    public void Leaves_ordinary_text_alone(string text) => Assert.Equal(text, SecretRedactor.Redact(text));
}

public class TokenValidatorTests
{
    [Theory]
    [InlineData(AccountKind.Bot)]
    [InlineData(AccountKind.User)]
    public void Accepts_token_shaped_strings_and_strips_prefixes_and_quotes(AccountKind kind)
    {
        Assert.Null(TokenValidator.Validate(kind, TestData.Token));
        Assert.Null(TokenValidator.Validate(kind, "Bot " + TestData.Token));
        Assert.Equal(TestData.Token, TokenValidator.Normalize(kind, $"  Bot {TestData.Token} "));
        // Copying a header value out of devtools often brings the quotes along.
        Assert.Equal(TestData.Token, TokenValidator.Normalize(kind, $"\"{TestData.Token}\""));
    }

    [Fact]
    public void Accepts_legacy_mfa_user_tokens_only_in_user_mode()
    {
        var mfa = "mfa." + new string('A', 84);
        Assert.Null(TokenValidator.Validate(AccountKind.User, mfa));
        Assert.NotNull(TokenValidator.Validate(AccountKind.Bot, mfa));
    }

    [Fact]
    public void Reads_the_account_id_from_the_first_segment() =>
        Assert.Equal(110000000000000100UL, TokenValidator.AccountId(TestData.Token));

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("aaaa.bbbb")]
    [InlineData("Zm9vYmFy.cccccc.dddddddddddddddddddddddddddddddd")] // first part isn't a numeric id
    [InlineData("0123456789abcdef0123456789abcdef")]                   // client secret shape
    public void Rejects_junk_without_echoing_it(string token)
    {
        foreach (var kind in Enum.GetValues<AccountKind>())
        {
            var error = TokenValidator.Validate(kind, token);
            Assert.NotNull(error);
            if (token.Length > 3) Assert.DoesNotContain(token, error);
        }
    }
}
