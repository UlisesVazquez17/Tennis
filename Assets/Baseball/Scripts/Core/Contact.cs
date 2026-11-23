// Contacto bate-pelota. Lógica pura.
//
// Velocidad de salida con la eficiencia de colisión q (Alan Nathan):
//   EV = q · v_lanzamiento + (1 + q) · v_bate
// En el punto dulce q ≈ 0.2; un contacto malo baja q hasta valores negativos.
//
// El timing decide la calidad y la dirección: antes = jalada, después = banda contraria.
// El plano del swing (lift) decide el ángulo de salida.

using System;

namespace Baseball
{
    public struct SwingResult
    {
        public bool whiff;
        public double evMph, launchDeg, sprayDeg, quality, batMph;
    }

    public static class Contact
    {
        public const double WINDOW_MS = 150;      // fuera de ±150 ms, el bate no toca la bola
        public const double QUALITY_MS = 85;      // ancho del "punto dulce" en tiempo
        public const double SPRAY_PER_MS = 0.42;  // grados de dirección por ms de adelanto o atraso
        public const double BAT_MIN_MPH = 54, BAT_MAX_MPH = 84;
        public const double LAUNCH_BASE = 20, LAUNCH_PER_LIFT = 22;
        public const double Q_SWEET = 0.21, Q_MISHIT = -0.06;

        static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));

        // Normal estándar (Box-Muller).
        public static double Gauss(Random rnd)
        {
            double u = Math.Max(1e-9, rnd.NextDouble()), v = rnd.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
        }

        public static double BatSpeedMph(double power) => BAT_MIN_MPH + (BAT_MAX_MPH - BAT_MIN_MPH) * Clamp(power, 0, 1);

        // dtMs: swing − paso de la bola por el punto de contacto (negativo = antes).
        // hand: 'R' (derecho) o 'L' (zurdo).
        public static SwingResult Swing(double dtMs, double power, double lift, double pitchMph, double pitchY, char hand, Random rnd)
        {
            if (Math.Abs(dtMs) > WINDOW_MS) return new SwingResult { whiff = true };
            double quality = Math.Exp(-Math.Pow(dtMs / QUALITY_MS, 2));
            double batMph = BatSpeedMph(power);
            double q = Q_MISHIT + (Q_SWEET - Q_MISHIT) * quality;

            // Error vertical: con mal timing es más fácil darle por arriba (rola) o por abajo (elevado).
            double vMiss = Gauss(rnd) * (4 + 22 * (1 - quality));
            // Bolas bajas tienden a rolas; altas, a elevados.
            double launchDeg = Clamp(LAUNCH_BASE + LAUNCH_PER_LIFT * Clamp(lift, -1, 1) + (pitchY - 0.8) * 15 + vMiss, -60, 85);
            // Pegarle lejos del centro de la bola también le quita velocidad.
            double offCenter = 1 - 0.35 * Math.Min(1, Math.Abs(vMiss) / 30);
            double evMph = Math.Max(20, (q * pitchMph + (1 + q) * batMph) * offCenter * (0.97 + 0.06 * rnd.NextDouble()));

            double pull = hand == 'L' ? -1 : 1;
            double sprayDeg = Clamp(dtMs * SPRAY_PER_MS * pull + Gauss(rnd) * 4, -80, 80);
            return new SwingResult { evMph = evMph, launchDeg = launchDeg, sprayDeg = sprayDeg, quality = quality, batMph = batMph };
        }
    }
}
