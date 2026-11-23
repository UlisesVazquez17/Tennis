// IMU del bate: recibe del gateway de motion-controller-lab (ws://127.0.0.1:8765) la
// orientación de la placa y detecta los swings. Solo lee: no le envía nada al gateway.
//
// El WebSocket corre en un hilo aparte; los mensajes se encolan y se entregan en Update.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Baseball.Pose;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Baseball.Net
{
    public class ImuLink : MonoBehaviour
    {
        public string gateway = "ws://127.0.0.1:8765";
        [Tooltip("Atraso aproximado del Bluetooth que se le resta al momento del swing (ms).")]
        public float latencyMs = 20;

        public readonly ImuSwing swing = new ImuSwing();
        public bool Live => Now - lastPacket < 1000;
        public float Hz { get; private set; }
        public string Device { get; private set; } = "";
        public Vector3 BatDirection => heading * Delta * Vector3.up;

        static double Now => ScreenLink.Now;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        static double clockOffset;

        readonly ConcurrentQueue<(Quaternion q, double deviceMs, double recvMs, string device)> inbox = new ConcurrentQueue<(Quaternion, double, double, string)>();
        CancellationTokenSource cts;
        Quaternion raw = Quaternion.identity, reference = Quaternion.identity, heading = Quaternion.identity;
        bool centered;
        double lastPacket = double.NegativeInfinity, deviceToScreen = double.PositiveInfinity;
        double hzStart; int hzCount;
        ImuSwing.Hit? pending;

        Quaternion Delta => raw * Quaternion.Inverse(reference);

        void OnEnable()
        {
            clockOffset = Now - clock.Elapsed.TotalMilliseconds;
            cts = new CancellationTokenSource();
            var url = new Uri(gateway);
            Task.Run(() => Run(url, cts.Token));
        }

        void OnDisable() => cts?.Cancel();

        async Task Run(Uri url, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                using (var socket = new KioskSocket())
                {
                    try
                    {
                        await socket.ConnectAsync(url, ct);
                        while (!ct.IsCancellationRequested)
                        {
                            string text = await socket.ReceiveAsync(ct);
                            if (text == null) break;
                            Parse(text);
                        }
                    }
                    catch (Exception) when (!ct.IsCancellationRequested) { }   // el gateway no está corriendo: se reintenta
                    catch (Exception) { return; }
                }
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
            }
        }

        void Parse(string text)
        {
            double recv = clock.Elapsed.TotalMilliseconds + clockOffset;
            JObject m;
            try { m = JObject.Parse(text); } catch { return; }
            if ((string)m["type"] != "telemetry" || !((bool?)m["validity"]?["quat_valid"] ?? false)) return;
            var q = m["quaternion"];
            if (q == null) return;
            // De la Tierra (x este, y norte, z arriba; mano derecha) a Unity (x este, y arriba,
            // z norte; mano izquierda): se cruzan y y z y el giro cambia de signo.
            var unity = new Quaternion(-(float)q["x"], -(float)q["z"], -(float)q["y"], (float)q["w"]);
            inbox.Enqueue((unity, (double)m["timestamp_ms"], recv, (string)m["device_id"]));
        }

        void Update()
        {
            clockOffset = Now - clock.Elapsed.TotalMilliseconds;
            while (inbox.TryDequeue(out var p))
            {
                raw = p.q; Device = p.device; lastPacket = p.recvMs; hzCount++;
                if (!centered) { Center(); }
                // Hora de la placa → reloj de la pantalla. El menor retraso visto es el más limpio;
                // sube despacio para seguir la deriva entre los dos relojes.
                deviceToScreen = Math.Min(deviceToScreen + 0.02, p.recvMs - p.deviceMs);
                var hit = swing.Feed(p.q, p.deviceMs);
                if (hit != null) pending = hit;
            }
            double now = Now;
            if (now - hzStart >= 1000) { Hz = (float)(hzCount * 1000 / (now - hzStart)); hzCount = 0; hzStart = now; }
        }

        // Con el bate vertical: esta postura pasa a ser "bate hacia arriba".
        public void Center() { reference = raw; heading = Quaternion.identity; centered = true; }

        // Con el bate apuntando al pitcher: alinea la vista con hacia dónde está la persona.
        public void AimAtPitcher()
        {
            var d = Delta * Vector3.up; d.y = 0;
            if (d.sqrMagnitude < 0.04f) return;     // está casi vertical: no hay dirección
            heading = Quaternion.AngleAxis(Vector3.SignedAngle(d, Vector3.forward, Vector3.up), Vector3.up);
        }

        // Entrega el swing detectado, con su momento en el reloj de la pantalla.
        public bool TakeSwing(out ImuSwing.Hit hit, out double screenMs)
        {
            hit = pending ?? default; screenMs = 0;
            if (pending == null) return false;
            pending = null;
            screenMs = hit.deviceMs + deviceToScreen - latencyMs;
            return true;
        }
    }
}
