using System.Net;

namespace Aggregatarr.Web.Services;

public interface IServiceHttp
{
    HttpClient Create(Uri baseUri, TimeSpan timeout);
}

public sealed class ServiceHttp : IServiceHttp
{
    public HttpClient Create(Uri baseUri, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = timeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseUri,
            Timeout = timeout + TimeSpan.FromSeconds(2)
        };
    }
}
