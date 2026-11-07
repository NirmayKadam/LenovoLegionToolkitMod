using System;
using LenovoLegionToolkit.Lib;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

public class HttpClientFactoryTests
{
    [Fact]
    public void WithProxy_CertificatesAreStillValidated()
    {
        var factory = new HttpClientFactory();
        factory.SetProxy(new Uri("http://10.0.0.1:8080"), "user", "pass");

        using var handler = factory.CreateHandler();

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
        Assert.Null(handler.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void WithoutProxy_CertificatesAreValidated()
    {
        using var handler = new HttpClientFactory().CreateHandler();

        Assert.Null(handler.ServerCertificateCustomValidationCallback);
    }
}
