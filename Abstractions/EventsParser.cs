using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EventsParser.Models;

namespace EventsParser
{
    public abstract class EventsParser
    {
        private readonly HttpClient httpClient;
        private readonly ILogger logger;

        public EventsParser(HttpClient httpClient, ILogger<EventsParser> logger)
        {
            this.httpClient = httpClient;
            this.logger = logger;
        }

        internal abstract Task<ICollection<EventData>> ParseEventsFrom(string htmlContent, CancellationToken cancellationToken = default);

        public async Task<ICollection<EventData>> GetEventsFromWeb(Uri uri, CancellationToken cancellationToken = default)
        {
            var result = new List<EventData>();

            var htmlContent = await GetHtmlContentFromWebAsync(uri, cancellationToken);

            result.AddRange(await ParseEventsFrom(htmlContent, cancellationToken));

            Log(LogLevel.Information, $"Finished parsing events. Total events: {result.Count}");

            return result;
        }

        internal async Task<string> GetHtmlContentFromWebAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);

                Log(LogLevel.Information, $"--> Sending request to {request.RequestUri}");
                var response = await httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    Log(LogLevel.Information, $"<-- Response: {response.StatusCode.ToString()}");
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
                else
                {
                    Log(LogLevel.Error, response.StatusCode.ToString());
                    return string.Empty;
                }
            }
            catch (TaskCanceledException exception)
            {
                Log(LogLevel.Information, $"Task was canceled in {this.GetType().Name}", exception);
                throw;
            }
            catch (InvalidOperationException exception)
            {
                Log(LogLevel.Error, $"The requestUri must be an absolute URI or BaseAddress must be set. Uri: {uri.ToString()}", exception);
                throw;
            }
            catch (HttpRequestException exception)
            {
                Log(LogLevel.Error, $"The request failed due to an underlying issue such as network connectivity, DNS failure, server certificate validation or timeout.", exception);
                throw;
            }            
        }

        internal void Log(LogLevel logLevel, string message, Exception exception = default)
        {
            if (exception is not null)
                logger?.Log(logLevel, message);
            else
                logger?.Log(logLevel, exception, message);
        }
    }
}