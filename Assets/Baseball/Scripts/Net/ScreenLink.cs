// Conexión de la pantalla (este juego) con el servidor FastAPI del kiosco: sala,
// reconexión, ping/pong y reloj compartido. Es el mismo protocolo que usa
// app/static/js/pantalla/net.js, así que el teléfono (/bate) no cambia.
//
// El WebSocket corre en un hilo aparte; los mensajes se encolan y se entregan en Update.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Baseball.Net
{
    public class ScreenLink : MonoBehaviour
    {
        [Tooltip("Servidor del kiosco (python run.py). El certificado propio se acepta solo para este host.")]
        public string server = "https://127.0.0.1:8443";
        public string game = "beisbol";
        public float pingSeconds = 2f;

        public bool Online { get; private set; }
        public bool ControlOn { get; private set; }
        public JObject Room { get; private set; }
        public readonly ClockSync clock = new ClockSync();

        // Eventos en el hilo principal.
        public event Action<bool> OnStatus;
        public event Action<JObject> OnRoom;
        public event Action<JObject> OnPeer;
        public event Action<JObject> OnMessage;

        // Reloj de la pantalla en ms (el mismo que usa el juego).
        public static double Now => Time.realtimeSinceStartupAsDouble * 1000.0;

        readonly ConcurrentQueue<Action> inbox = new ConcurrentQueue<Action>();
        readonly ConcurrentDictionary<int, double> pending = new ConcurrentDictionary<int, double>();
        KioskSocket ws;
        CancellationTokenSource cts;
        int attempt, pingId;
        float pingTimer;

        string TokenKey => $"{game}.pantalla.token";

        void OnEnable()
        {
            cts = new CancellationTokenSource();
            var token = PlayerPrefs.GetString(TokenKey, "");
            // Fuera del hilo principal, para marcar la hora de llegada de cada pong sin esperar al cuadro.
            Task.Run(() => Run(token, cts.Token));
        }

        void OnDisable()
        {
            cts?.Cancel();
            ws?.Dispose();
        }

        async Task Run(string token, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var url = new UriBuilder(server) { Scheme = server.StartsWith("https") ? "wss" : "ws", Path = "/ws/pantalla" };
                url.Query = $"juego={game}" + (token.Length > 0 ? $"&token={Uri.EscapeDataString(token)}" : "");
                var socket = new KioskSocket();
                ws = socket;
                try
                {
                    await socket.ConnectAsync(url.Uri, ct);
                    attempt = 0;
                    inbox.Enqueue(() => { Online = true; OnStatus?.Invoke(true); });
                    while (!ct.IsCancellationRequested)
                    {
                        string text = await socket.ReceiveAsync(ct);
                        if (text == null) break;
                        JObject m;
                        try { m = JObject.Parse(text); } catch { continue; }
                        if ((string)m["t"] == "room") token = (string)m["token"] ?? token;
                        double t1 = (string)m["t"] == "pong" ? NowThreadSafe() : 0;
                        inbox.Enqueue(() => Handle(m, t1));
                    }
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    inbox.Enqueue(() => Debug.LogWarning($"[ScreenLink] {url.Uri}: {e.GetBaseException().Message}"));
                }
                catch { return; }
                finally { socket.Dispose(); }
                inbox.Enqueue(() => { Online = false; ControlOn = false; clock.Reset(); OnStatus?.Invoke(false); });
                int delay = Math.Min(8000, 500 * (1 << Math.Min(attempt++, 5)));
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return; }
            }
        }

        // Time.realtimeSinceStartup solo se puede leer en el hilo principal.
        static readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        static double swOffset;
        static double NowThreadSafe() => sw.Elapsed.TotalMilliseconds + swOffset;

        void Update()
        {
            swOffset = Now - sw.Elapsed.TotalMilliseconds;
            while (inbox.TryDequeue(out var a)) a();
            pingTimer += Time.unscaledDeltaTime;
            if (pingTimer >= pingSeconds) { pingTimer = 0; Ping(); }
        }

        void Handle(JObject m, double t1)
        {
            switch ((string)m["t"])
            {
                case "room":
                    PlayerPrefs.SetString(TokenKey, (string)m["token"] ?? "");
                    Room = m; ControlOn = (bool?)m["control"] ?? false;
                    OnRoom?.Invoke(m);
                    return;
                case "peer":
                    if ((string)m["who"] == "raqueta")
                    {
                        ControlOn = (bool?)m["on"] ?? false;
                        if (!ControlOn) { clock.Reset(); pending.Clear(); }
                        OnPeer?.Invoke(m);
                    }
                    return;
                case "pong":
                    if (pending.TryRemove((int?)m["id"] ?? -1, out double t0)) clock.Add(t0, (double?)m["tp"] ?? double.NaN, t1);
                    return;
                default:
                    OnMessage?.Invoke(m);
                    return;
            }
        }

        public void Send(object msg)
        {
            var socket = ws;
            if (socket == null || !socket.Open) return;
            string text = JObject.FromObject(msg).ToString(Newtonsoft.Json.Formatting.None);
            socket.SendAsync(text, CancellationToken.None).ContinueWith(t => { }, TaskContinuationOptions.OnlyOnFaulted);
        }

        void Ping()
        {
            if (!ControlOn) return;
            int id = ++pingId; double ts = Now;
            pending[id] = ts;
            foreach (var kv in pending) if (ts - kv.Value > 10000) pending.TryRemove(kv.Key, out _);
            Send(new { t = "ping", id, ts = Math.Round(ts) });
            if (clock.Ready) Send(new { t = "lat", rtt = Math.Round(clock.Last.Value) });
        }

        // Pide al servidor liberar el lugar del teléfono (no volvió a tiempo, o terminó el turno).
        public void FreeControl() => Send(new { t = "free" });

        // Imagen del QR de la sala actual.
        public string QrUrl(string sala, int size = 600) => $"{server.TrimEnd('/')}/qr.png?sala={sala}&size={size}";
    }
}
