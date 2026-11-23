// Reloj compartido entre pantalla y teléfono a partir de ping/pong.
// offset ≈ tPantalla − tTeléfono − RTT/2, así que tPantalla ≈ tTeléfono + offset.

using System.Collections.Generic;
using System.Linq;

namespace Baseball.Net
{
    public class ClockSync
    {
        readonly int size;
        readonly List<(double rtt, double offset)> samples = new List<(double, double)>();

        public ClockSync(int size = 15) { this.size = size; }

        // t0: la pantalla envía el ping; tp: el teléfono responde; t1: llega el pong.
        public void Add(double t0, double tp, double t1)
        {
            double rtt = t1 - t0;
            if (!(rtt >= 0) || double.IsNaN(tp) || double.IsInfinity(tp)) return;
            samples.Add((rtt, t1 - tp - rtt / 2));
            if (samples.Count > size) samples.RemoveAt(0);
        }

        public bool Ready => samples.Count > 0;
        public double? Last => Ready ? samples[samples.Count - 1].rtt : (double?)null;
        public double? Avg => Ready ? samples.Average(s => s.rtt) : (double?)null;

        // La muestra con menor RTT es la menos afectada por colas en la red.
        public double Offset => Ready ? samples.OrderBy(s => s.rtt).First().offset : 0;

        public double ToScreen(double tPhone) => tPhone + Offset;
        public void Reset() => samples.Clear();
    }
}
