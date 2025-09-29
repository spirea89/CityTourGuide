using System;

namespace CityTour.Services
{
    public sealed class OpenAiResponseParseException : InvalidOperationException
    {
        public OpenAiResponseParseException(string failureContext, string responseBody, Exception innerException)
            : base($"Failed to parse the {failureContext} response from OpenAI.", innerException)
        {
            FailureContext = failureContext;
            ResponseBody = responseBody;
        }

        public string FailureContext { get; }

        public string ResponseBody { get; }
    }
}
