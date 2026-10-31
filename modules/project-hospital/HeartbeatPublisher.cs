using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HappyCheats.ProjectHospital
{
    // The worker only republishes an immutable main-thread snapshot. It never
    // reads Unity/game objects or executes a trainer command.
    internal sealed class HeartbeatPublisher : IDisposable
    {
        private readonly object gate = new object();
        private readonly string path;
        private readonly Func<DateTime> clock;
        private readonly Timer timer;
        private TrainerStatus snapshot;
        private DateTime lastMainPublish;
        private bool disposed;
        internal string LastError { get; private set; }

        internal HeartbeatPublisher(string path, bool startTimer = true, Func<DateTime> clock = null)
        {
            this.path = path;
            this.clock = clock ?? (() => DateTime.UtcNow);
            if (startTimer) timer = new Timer(_ => BackgroundPulse(), null, 1000, 1000);
        }

        internal void Publish(TrainerStatus status)
        {
            lock (gate)
            {
                if (disposed) return;
                snapshot = Copy(status);
                lastMainPublish = clock();
                Write();
            }
        }

        internal void Pulse()
        {
            lock (gate) { if (!disposed && snapshot != null) Write(); }
        }

        private void BackgroundPulse()
        {
            try { Pulse(); }
            catch (Exception error) { lock (gate) { LastError = error.GetType().Name + ": " + error.Message; } }
        }

        private void Write()
        {
            var status = Copy(snapshot);
            var now = clock();
            var waiting = now - lastMainPublish > TimeSpan.FromSeconds(2);
            status.updated_utc = (now.Ticks - 621355968000000000L) / TimeSpan.TicksPerSecond;
            status.values["bridge_waiting_for_game"] = waiting ? "True" : "False";
            if (waiting && status.bindings_valid)
                status.message = "Trainer connected. Return to Project Hospital to process controls.";
            Protocol.WriteAtomic(path, status);
            LastError = null;
        }

        private static TrainerStatus Copy(TrainerStatus value) => new TrainerStatus {
            schema_version = value.schema_version, plugin_version = value.plugin_version, version = value.version,
            session_id = value.session_id, process_id = value.process_id, ready = value.ready, message = value.message,
            values = new Dictionary<string, string>(value.values), enabled = new Dictionary<string, bool>(value.enabled),
            last_command = value.last_command, updated_utc = value.updated_utc, assembly_sha256 = value.assembly_sha256,
            bindings_valid = value.bindings_valid, initialization_error = value.initialization_error
        };

        public void Dispose()
        {
            lock (gate) { disposed = true; if (timer != null) timer.Dispose(); }
        }
    }
}
