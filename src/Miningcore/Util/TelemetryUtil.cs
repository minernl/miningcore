

using System;
using System.Collections.Generic;
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
            telemetryClient?.TrackDependency(type.ToString(), name, data, startTime, duration, success);
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

        public static async Task TrackDependency(Func<Task> operation, DependencyType type, string name, string data)
        {
            var success = false;
            var startTime = DateTime.UtcNow;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await operation();
                success = true;
            }
            finally
            {
                timer.Stop();
                TrackDependency(type, name, data, startTime, timer.Elapsed, success);
            }
        }

        public static void TrackMetric(string name, double val)
        {
            telemetryClient?.GetMetric(name).TrackValue(val);
        }

        public static void TrackMetric(string name, string dimension, double val, string dimensionVal)
        {
            telemetryClient?.GetMetric(name, dimension).TrackValue(val, dimensionVal);
        }

        public static void TrackMetric(string name, string dimension, string dimension2, double val, string dimensionVal, string dimensionVal2)
        {
            telemetryClient?.GetMetric(name, dimension, dimension2).TrackValue(val, dimensionVal, dimensionVal2);
        }

        public static void TrackEvent(string name, IDictionary<string, string> props)
        {
            telemetryClient?.TrackEvent(name, props);
        }

        public static void Cleanup()
        {
            telemetryClient?.Flush();
        }
    }

    public enum DependencyType
    {
        Sql,
        Http,
        Daemon,
        Web3,
        EtherScan
    }
}
