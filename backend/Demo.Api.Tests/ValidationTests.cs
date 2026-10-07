using Demo.Api.Contracts;

namespace Demo.Api.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("a@@example.com")]
    [InlineData("Person <a@example.com>")]
    [InlineData("a@example.com,b@example.com")]
    public void Registration_and_login_reject_non_address_email_values(string email)
    {
        Assert.False(new RegisterValidator().Validate(new RegisterRequest("Tester", email, "TestPassword123!")).IsValid);
        Assert.False(new LoginValidator().Validate(new LoginRequest(email, "TestPassword123!")).IsValid);
    }

    [Fact]
    public void Registration_rejects_whitespace_password_and_accepts_normalizable_email()
    {
        Assert.False(new RegisterValidator().Validate(new RegisterRequest("Tester", "test@example.com", new string(' ', 12))).IsValid);
        Assert.True(new RegisterValidator().Validate(new RegisterRequest("Tester", " TEST@example.com ", "TestPassword123!")).IsValid);
    }
}
