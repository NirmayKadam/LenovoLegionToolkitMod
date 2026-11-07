using System;
using System.Net;
using System.Net.Http;

namespace LenovoLegionToolkit.Lib;

public class HttpClientFactory
{
    private Uri? _url;
    private string? _username;
    private string? _password;

    // Certificates are always validated, including through a proxy. (There used to be an option to accept
    // any certificate; it let anyone on the network impersonate update, driver and warranty servers.
    // Corporate inspection proxies install their root certificate in Windows, so normal validation works.)
    public HttpClientHandler CreateHandler()
    {
        var handler = new HttpClientHandler();

        if (_url is not null)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(_url)
            {
                UseDefaultCredentials = false,
                BypassProxyOnLocal = false,
            };

            if (_username is not null && _password is not null)
                handler.DefaultProxyCredentials = new NetworkCredential(_username, _password);
        }

        return handler;
    }

    public HttpClient Create() => new(CreateHandler(), true);

    public void SetProxy(Uri? url, string? username, string? password)
    {
        _url = url;
        _username = username;
        _password = password;
    }
}
