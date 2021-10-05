using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Miningcore.Util;
using Newtonsoft.Json;
using NLog;

namespace Miningcore.DataStore.Cloud
{
    internal abstract class ApiEndpoint
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        protected ApiEndpoint()
        {
            Client ??= new HttpClient();
        }

        protected HttpClient Client { get; }

        #region Public Members

        protected async Task<T> GetAsync<T>(string uri, IDictionary<string, string> headers)
        {
            return await InvokeAsync(headers, ParseResponse<T>, uri, HttpMethod.Get);
        }

        protected async Task<HttpResponseMessage> GetAsync(string uri, IDictionary<string, string> headers)
        {
            return await InvokeAsync<HttpResponseMessage>(headers, uri, HttpMethod.Get);
        }

        protected async Task<T> PostAsync<T>(string uri, string data, IDictionary<string, string> headers)
        {
            return await InvokeAsync(headers, ParseResponse<T>, uri, HttpMethod.Post, data);
        }

        protected async Task<HttpResponseMessage> PostAsync(string uri, string data, IDictionary<string, string> headers)
        {
            return await InvokeAsync<HttpResponseMessage>(headers, uri, HttpMethod.Post, data);
        }

        protected async Task<T> DeleteAsync<T>(string uri, IDictionary<string, string> headers)
        {
            return await InvokeAsync(headers, ParseResponse<T>, uri, HttpMethod.Delete);
        }

        #endregion

        #region Private Members

        private static async Task<T> ParseResponse<T>(HttpResponseMessage response)
        {
            var resp = await response.Content.ReadAsStringAsync();
            if(typeof(T) == typeof(string))
            {
                return (T) (object) resp;
            }

            T res = default;
            try
            {
               res = JsonConvert.DeserializeObject<T>(resp);
            }
            catch(JsonException e)
            {
                Logger.Error($"Error while parsing response. resp={resp}, reason={e.Message}");
            }
            
            return res;
        }

        private async Task<T> InvokeAsync<T>(IDictionary<string, string> headers, Func<HttpResponseMessage, Task<T>> actionOnResponse, string url,
            HttpMethod method, string data = null)
        {
            var response = await InvokeAsync<T>(headers, url, method, data);
            if(!response.IsSuccessStatusCode)
            {
                var exception = new HttpRequestException($"Server returned an error. StatusCode : {response.StatusCode}");
                exception.Data.Add("StatusCode", response.StatusCode);
                exception.Data.Add("Content", await response.Content.ReadAsStringAsync());
                throw exception;
            }

            if(actionOnResponse != null)
            {
                return await actionOnResponse(response);
            }

            return default;
        }

        private async Task<HttpResponseMessage> InvokeAsync<T>(IDictionary<string, string> headers, string url, HttpMethod method, string data = null)
        {
            var telemetryUtil = TelemetryUtil.GetTelemetryClient();

            using var request = new HttpRequestMessage(method, url);

            if(headers != null)
            {
                foreach(var (key, value) in headers)
                {
                    switch(key.ToLower())
                    {
                        case WebConstants.HeaderContentType:
                            // Ignoring it since its added to the request content itself
                            break;
                        case WebConstants.HeaderAccept:
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(value));
                            break;
                        case WebConstants.HeaderHost:
                            request.Headers.Host = value;
                            break;
                        case WebConstants.HeaderAuthorization:
                            if(AuthenticationHeaderValue.TryParse(value, out var authHeaderValue))
                            {
                                request.Headers.Authorization = authHeaderValue;
                            }
                            else
                            {
                                request.Headers.TryAddWithoutValidation(key, value);
                            }
                            break;
                        default:
                            request.Headers.Add(key, value);
                            break;
                    }
                }
            }

            if(data != null)
            {
                var content = new StringContent(data, Encoding.UTF8);
                if(headers != null && headers.ContainsKey(WebConstants.HeaderContentType))
                    content.Headers.ContentType = MediaTypeHeaderValue.Parse(headers[WebConstants.HeaderContentType]);
                request.Content = content;
            }

            var response = await Client.SendAsync(request);
            return response;
        }

        #endregion

    }
}
