

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;


namespace Miningcore.Util
{
    public static class TelemetryUtil
    {
        private static TelemetryClient telemetryClient = null;
        private static DependencyTrackingTelemetryModule depModule = null;

        public static void init(string applicationInsightsKey)
        {
            if(!string.IsNullOrEmpty(applicationInsightsKey))
            {
                TelemetryConfiguration configuration = TelemetryConfiguration.CreateDefault();
                configuration.InstrumentationKey = applicationInsightsKey;
                telemetryClient = new TelemetryClient(configuration);
                depModule = new DependencyTrackingTelemetryModule();
                depModule.Initialize(configuration);
            }
        }

        public static TelemetryClient GetTelemetryClient()
        {
            return telemetryClient;
        }

        public static void cleanup()
        {
            if(null != telemetryClient)
                telemetryClient.Flush();
        }
    }
}