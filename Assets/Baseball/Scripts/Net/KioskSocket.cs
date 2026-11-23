// Cliente WebSocket mínimo (RFC 6455) sobre TcpClient + SslStream.
//
// Existe porque el ClientWebSocket de Unity valida el certificado con su propia pila TLS
// e ignora ServerCertificateValidationCallback, así que rechaza el certificado
// autofirmado del servidor del kiosco. SslStream sí respeta su callback.
// Solo implementa lo que usa el protocolo: mensajes de texto, ping/pong y cierre.

using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Baseball.Net
{
    public class KioskSocket : IDisposable
    {
        TcpClient tcp;
        Stream stream;
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        public bool Open { get; private set; }

        public async Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            bool tls = uri.Scheme == "wss" || uri.Scheme == "https";
            int port = uri.IsDefaultPort ? (tls ? 443 : 80) : uri.Port;
            tcp = new TcpClient { NoDelay = true };
            using (ct.Register(() => tcp.Close()))
                await tcp.ConnectAsync(uri.Host, port);
            stream = tcp.GetStream();
            if (tls)
            {
                // El servidor del kiosco usa un certificado autofirmado (app/certs.py).
                var ssl = new SslStream(stream, false, (s, c, ch, e) => true);
                await ssl.AuthenticateAsClientAsync(uri.Host);
                stream = ssl;
            }

            var key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var req = $"GET {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Host}:{port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                      $"Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(req);
            await stream.WriteAsync(bytes, 0, bytes.Length, ct);

            // Respuesta del handshake, hasta la línea en blanco.
            var head = new StringBuilder();
            var one = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n"))
            {
                if (await stream.ReadAsync(one, 0, 1, ct) == 0) throw new IOException("El servidor cerró durante el handshake");
                head.Append((char)one[0]);
                if (head.Length > 8192) throw new IOException("Handshake demasiado largo");
            }
            var text = head.ToString();
            if (!text.StartsWith("HTTP/1.1 101")) throw new IOException($"Handshake rechazado: {text.Split('\r')[0]}");
            string accept;
            using (var sha = SHA1.Create())
                accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            if (text.IndexOf(accept, StringComparison.Ordinal) < 0) throw new IOException("Sec-WebSocket-Accept no coincide");
            Open = true;
        }

        // Devuelve el siguiente mensaje de texto, o null si la conexión se cerró.
        public async Task<string> ReceiveAsync(CancellationToken ct)
        {
            var msg = new MemoryStream();
            while (true)
            {
                var h = await ReadExact(2, ct);
                if (h == null) return Closed();
                bool fin = (h[0] & 0x80) != 0;
                int op = h[0] & 0x0F;
                long len = h[1] & 0x7F;
                if (len == 126) { var b = await ReadExact(2, ct); if (b == null) return Closed(); len = (b[0] << 8) | b[1]; }
                else if (len == 127) { var b = await ReadExact(8, ct); if (b == null) return Closed(); len = 0; for (int i = 0; i < 8; i++) len = (len << 8) | b[i]; }
                byte[] mask = (h[1] & 0x80) != 0 ? await ReadExact(4, ct) : null;
                var payload = len > 0 ? await ReadExact((int)len, ct) : new byte[0];
                if (payload == null) return Closed();
                if (mask != null) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];

                switch (op)
                {
                    case 0x8: await SendFrame(0x8, payload, ct); return Closed();   // cierre
                    case 0x9: await SendFrame(0xA, payload, ct); continue;          // ping → pong
                    case 0xA: continue;                                              // pong
                    case 0x1: case 0x0: case 0x2:
                        msg.Write(payload, 0, payload.Length);
                        if (fin) return Encoding.UTF8.GetString(msg.ToArray());
                        continue;
                }
            }
        }

        public Task SendAsync(string text, CancellationToken ct) => SendFrame(0x1, Encoding.UTF8.GetBytes(text), ct);

        async Task SendFrame(int op, byte[] payload, CancellationToken ct)
        {
            if (!Open) return;
            // El cliente siempre enmascara sus tramas.
            var mask = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(mask);
            var frame = new MemoryStream();
            frame.WriteByte((byte)(0x80 | op));
            if (payload.Length < 126) frame.WriteByte((byte)(0x80 | payload.Length));
            else if (payload.Length <= 0xFFFF) { frame.WriteByte(0x80 | 126); frame.WriteByte((byte)(payload.Length >> 8)); frame.WriteByte((byte)payload.Length); }
            else { frame.WriteByte(0x80 | 127); for (int i = 7; i >= 0; i--) frame.WriteByte((byte)((long)payload.Length >> (8 * i))); }
            frame.Write(mask, 0, 4);
            for (int i = 0; i < payload.Length; i++) frame.WriteByte((byte)(payload[i] ^ mask[i % 4]));
            var bytes = frame.ToArray();
            await sendLock.WaitAsync(ct);
            try { await stream.WriteAsync(bytes, 0, bytes.Length, ct); }
            catch { Open = false; throw; }
            finally { sendLock.Release(); }
        }

        async Task<byte[]> ReadExact(int n, CancellationToken ct)
        {
            var buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = await stream.ReadAsync(buf, got, n - got, ct);
                if (r == 0) return null;
                got += r;
            }
            return buf;
        }

        string Closed() { Open = false; return null; }

        public void Dispose()
        {
            Open = false;
            try { stream?.Dispose(); } catch { }
            try { tcp?.Close(); } catch { }
        }
    }
}
