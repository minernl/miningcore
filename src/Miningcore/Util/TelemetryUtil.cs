

using System;
using System.Threading.Tasks;
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

        public static void TrackDependency(DependencyType type, string name, string data, DateTimeOffset startTime, TimeSpan duration, bool success)
        {
            //TODO: Disabling dependency telemetry to see if it affected the memory usage
            //telemetryClient?.TrackDependency(type.ToString(), name, data, startTime, duration, success);
        }

        public static async Task<T> TrackDependency<T>(Func<Task<T>> operation, DependencyType type, string name, string data)
        {
            var success = false;
            var startTime = DateTime.UtcNow;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await operation();
                success = true;
                return result;
            }
            finally
            {
                timer.Stop();
                TrackDependency(type, name, data, startTime, timer.Elapsed, success);
            }
        }

        public static void cleanup()
        {
            if(null != telemetryClient)
                telemetryClient.Flush();
        }
    }

    public enum DependencyType
    {
        Sql,
        Http,
        Daemon,
        Web3
    }
}
