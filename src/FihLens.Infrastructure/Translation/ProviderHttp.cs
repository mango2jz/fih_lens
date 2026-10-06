using System.Net;
using System.Net.Http;
using System.Text;
using FihLens.Application.Translation;

namespace FihLens.Infrastructure.Translation;

internal static class ProviderHttp
{
    public static async Task<string> PostAsync(HttpClient client, HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            using (request)
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead,
                       cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new TranslationProviderException(MapStatus(response.StatusCode));

                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TranslationProviderException(TranslationProviderFailureKind.Timeout, exception);
        }
        catch (TimeoutException exception)
        {
            throw new TranslationProviderException(TranslationProviderFailureKind.Timeout, exception);
        }
        catch (HttpRequestException exception)
        {
            var failureKind = exception.HttpRequestError is HttpRequestError.ConfigurationLimitExceeded
                or HttpRequestError.InvalidResponse
                ? TranslationProviderFailureKind.InvalidResponse
                : TranslationProviderFailureKind.Unavailable;
            throw new TranslationProviderException(failureKind, exception);
        }
    }

    public static void SetJsonContent(HttpRequestMessage request, string json) =>
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

    private static TranslationProviderFailureKind MapStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.PaymentRequired or HttpStatusCode.Forbidden =>
            TranslationProviderFailureKind.Authentication,
        HttpStatusCode.TooManyRequests => TranslationProviderFailureKind.RateLimited,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => TranslationProviderFailureKind.Timeout,
        _ when (int)statusCode >= 500 => TranslationProviderFailureKind.Unavailable,
        _ => TranslationProviderFailureKind.RequestRejected
    };
}
