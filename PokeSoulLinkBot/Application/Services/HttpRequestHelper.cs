using System.Net;
using System.Text.Json;
using Serilog;

namespace PokeSoulLinkBot.Application.Services;

internal static class HttpRequestHelper
{
    private const int MaxAttempts = 2;

    public static Task<HttpResponseMessage> GetAsync(HttpClient httpClient, string requestUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

        return GetWithRetryAsync(() => httpClient.GetAsync(requestUri), requestUri);
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient httpClient, Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(requestUri);

        return GetWithRetryAsync(() => httpClient.GetAsync(requestUri), requestUri.ToString());
    }

    public static async Task<T?> GetFromJsonAsync<T>(
        HttpClient httpClient,
        string requestUri,
        JsonSerializerOptions jsonSerializerOptions)
    {
        ArgumentNullException.ThrowIfNull(jsonSerializerOptions);

        using var response = await GetAsync(httpClient, requestUri);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<T>(responseStream, jsonSerializerOptions);
    }

    public static async Task<string> GetStringAsync(HttpClient httpClient, Uri requestUri)
    {
        using var response = await GetAsync(httpClient, requestUri);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> GetWithRetryAsync(
        Func<Task<HttpResponseMessage>> sendAsync,
        string requestUri)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var response = await sendAsync();
                if (!IsTransientStatusCode(response.StatusCode) || attempt == MaxAttempts)
                {
                    return response;
                }

                Log.Warning(
                    "HTTP GET {RequestUri} returned transient status {StatusCode} on attempt {Attempt}/{MaxAttempts}. Retrying.",
                    requestUri,
                    response.StatusCode,
                    attempt,
                    MaxAttempts);
                response.Dispose();
            }
            catch (Exception exception) when (IsTransientException(exception) && attempt < MaxAttempts)
            {
                Log.Warning(
                    exception,
                    "HTTP GET {RequestUri} failed on attempt {Attempt}/{MaxAttempts}. Retrying.",
                    requestUri,
                    attempt,
                    MaxAttempts);
            }

            await Task.Delay(GetRetryDelay(attempt));
        }

        throw new InvalidOperationException("HTTP retry loop exited unexpectedly.");
    }

    private static bool IsTransientException(Exception exception)
    {
        return exception is HttpRequestException or TaskCanceledException;
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        return TimeSpan.FromMilliseconds(100 * attempt);
    }
}
