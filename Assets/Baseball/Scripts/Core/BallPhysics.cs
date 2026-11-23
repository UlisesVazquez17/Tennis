// Física del béisbol. Lógica pura, sin MonoBehaviour.
//
// Coordenadas en metros, vistas desde atrás del home: x hacia primera base (derecha),
// y hacia arriba, z hacia el jardín central. El vértice del home está en el origen.
// Coinciden con los ejes de Unity, así que una posición se dibuja tal cual.
//
// Vuelo con gravedad, resistencia del aire y efecto Magnus, con el coeficiente de
// sustentación de Sawicki-Hubbard-Stronge, CL = 1 / (2.32 + 0.4 / S), S = rω/v, escalado
// con CL_SCALE. Portado de app/static/js/beisbol/physics.js (proyecto tenis).

using System;

namespace Baseball
{
    [Serializable]
    public struct V3
    {
        public double x, y, z;
        public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 operator *(V3 a, double k) => new V3(a.x * k, a.y * k, a.z * k);
        public static V3 Cross(V3 a, V3 b) => new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public V3 Unit { get { double n = Length; return n > 0 ? this * (1 / n) : this; } }
        public UnityEngine.Vector3 ToUnity() => new UnityEngine.Vector3((float)x, (float)y, (float)z);
    }

    // Estado de una bola: posición (m), velocidad (m/s) y giro (rad/s, vector).
    public class BallState
    {
        public V3 p, v, w;
        public BallState(V3 p, V3 v, V3 w) { this.p = p; this.v = v; this.w = w; }
        public BallState Clone() => new BallState(p, v, w);
    }

    public enum PitchType { Recta, Sinker, Cambio, Curva, Slider }

    public enum HitKind { HomeRun, Foul, Wall, Fly, Liner, Grounder, Popup }

    public struct HitOutcome
    {
        public HitKind kind;
        public int distFt;
        public double sprayDeg, hangS;
        public int apexFt;
        public double? fenceT;      // cuándo cruza la barda (s), si llega
        public V3 landing;
    }

    public static class BallPhysics
    {
        public const double FT = 0.3048, MPH = 0.44704, RPM = Math.PI / 30;

        public const double MASS = 0.145, R = 0.0366, RHO = 1.2, CD = 0.36, G = 9.81;
        public const double CL_SCALE = 0.75;   // calibrado contra Statcast: 103 mph a 28° ≈ 410 pies
        public const double MOUND_Z = 18.44;   // goma del pitcher (60.5 pies)
        public const double RELEASE_Z = 16.7;  // el pitcher suelta la bola con ~1.7 m de extensión
        public const double RELEASE_Y = 1.8;
        public const double CONTACT_Z = 0.45;  // el contacto ideal es un poco delante del home
        public const double ZONE_HALF_W = 0.216 + 0.037;
        public const double ZONE_LOW = 0.48, ZONE_HIGH = 1.07;
        public const double WALL_H = 3.05;     // barda de 10 pies
        public const double FOUL_DEG = 45;

        // Barda: distancia (pies) según el ángulo de dirección (0 = central, negativo = izquierdo).
        static readonly double[,] FENCE = { { -45, 330 }, { -22.5, 375 }, { 0, 400 }, { 22.5, 375 }, { 45, 330 } };

        public static double FenceFt(double angleDeg)
        {
            double a = Math.Max(-45, Math.Min(45, angleDeg));
            for (int i = 1; i < FENCE.GetLength(0); i++)
            {
                double a0 = FENCE[i - 1, 0], d0 = FENCE[i - 1, 1], a1 = FENCE[i, 0], d1 = FENCE[i, 1];
                if (a <= a1) return d0 + (d1 - d0) * (a - a0) / (a1 - a0);
            }
            return FENCE[FENCE.GetLength(0) - 1, 1];
        }

        static readonly double K = 0.5 * RHO * Math.PI * R * R / MASS;

        public static V3 Accel(V3 v, V3 w)
        {
            double speed = v.Length;
            var a = new V3(0, -G, 0);
            if (speed < 1e-6) return a;
            a -= v * (K * CD * speed);
            double wn = w.Length;
            if (wn > 1e-6)
            {
                double S = R * wn / speed, CL = CL_SCALE / (2.32 + 0.4 / S);
                V3 m = V3.Cross(w, v);
                double mn = m.Length;
                if (mn > 1e-9) a += m * (K * CL * speed * speed / mn);
            }
            return a;
        }

        // Un paso de integración (punto medio).
        public static void Step(BallState b, double dt)
        {
            V3 a1 = Accel(b.v, b.w);
            V3 vm = b.v + a1 * (dt / 2);
            V3 a2 = Accel(vm, b.w);
            b.p += vm * dt;
            b.v += a2 * dt;
        }

        public const double DT = 1.0 / 600;

        // Simula hasta que done(b, t) sea verdadero o pase maxT. Devuelve el estado final y t.
        public static (BallState b, double t) Simulate(BallState b0, Func<BallState, double, bool> done, double maxT = 10)
        {
            var b = b0.Clone();
            double t = 0;
            while (t < maxT && !done(b, t)) { Step(b, DT); t += DT; }
            return (b, t);
        }

        // ---------- Lanzamientos ----------
        // Giro (rpm) y su eje para un pitcher derecho. La bola va hacia −z: +x es backspin
        // (la recta "sube"), −x topspin (la curva cae) y −y barre la bola hacia +x.
        public struct PitchSpec { public string name; public double rpm; public V3 axis; }

        public static PitchSpec Spec(PitchType t)
        {
            switch (t)
            {
                case PitchType.Sinker: return new PitchSpec { name = "Sinker", rpm = 2100, axis = new V3(0.45, 0.85, 0) };
                case PitchType.Cambio: return new PitchSpec { name = "Cambio", rpm = 1700, axis = new V3(0.75, 0.6, 0) };
                case PitchType.Curva: return new PitchSpec { name = "Curva", rpm = 2500, axis = new V3(-0.85, -0.45, 0.2) };
                case PitchType.Slider: return new PitchSpec { name = "Slider", rpm = 2400, axis = new V3(-0.25, -0.75, 0.6) };
                default: return new PitchSpec { name = "Recta", rpm = 2250, axis = new V3(0.95, 0.25, 0) };
            }
        }

        // Velocidad inicial para que la bola cruce el home (z = 0) en (tx, ty).
        public static (BallState start, V3 cross, double crossT) SolvePitch(PitchType type, double mph, double tx, double ty, double releaseX = -0.55)
        {
            var spec = Spec(type);
            V3 w = spec.axis.Unit * (spec.rpm * RPM);
            var p0 = new V3(releaseX, RELEASE_Y, RELEASE_Z);
            double speed = mph * MPH;
            var aim = new V3(tx, ty + 0.9, 0);   // primera estimación: apuntar arriba por la gravedad
            V3 cross = default; double crossT = 0;
            for (int k = 0; k < 12; k++)
            {
                var b0 = new BallState(p0, (aim - p0).Unit * speed, w);
                var (b, t) = Simulate(b0, (s, _) => s.p.z <= 0, 3);
                cross = b.p; crossT = t;
                double ex = tx - b.p.x, ey = ty - b.p.y;
                if (Math.Abs(ex) < 0.002 && Math.Abs(ey) < 0.002) return (b0, cross, crossT);
                aim = new V3(aim.x + ex, aim.y + ey, 0);
            }
            return (new BallState(p0, (aim - p0).Unit * speed, w), cross, crossT);
        }

        public static bool IsStrike(double x, double y) =>
            Math.Abs(x) <= ZONE_HALF_W && y >= ZONE_LOW - R && y <= ZONE_HIGH + R;

        // ---------- Batazos ----------
        public static BallState BattedBall(double evMph, double launchDeg, double sprayDeg, V3 p, double? spinRpm = null)
        {
            double la = launchDeg * Math.PI / 180, sp = sprayDeg * Math.PI / 180;
            var dir = new V3(Math.Sin(sp) * Math.Cos(la), Math.Sin(la), Math.Cos(sp) * Math.Cos(la));
            // Backspin en elevados y líneas; topspin en rolas.
            double rpm = spinRpm ?? (launchDeg > 5 ? Math.Min(3000, 1100 + 45 * launchDeg) : -900);
            V3 h = new V3(dir.x, 0, dir.z).Unit;
            V3 axis = V3.Cross(h, new V3(0, 1, 0));
            return new BallState(p, dir * (evMph * MPH), axis * (rpm * RPM));
        }

        public static double Horiz(V3 p) => Math.Sqrt(p.x * p.x + p.z * p.z);
        public static double SprayOf(V3 p) => Math.Atan2(p.x, p.z) * 180 / Math.PI;

        public static HitOutcome BattedOutcome(BallState b0, double launchDeg)
        {
            double apex = 0; double? fenceT = null; bool? overWall = null;
            var (b, t) = Simulate(b0, (s, tt) =>
            {
                apex = Math.Max(apex, s.p.y);
                double r = Horiz(s.p) / FT;
                if (overWall == null && s.p.z > 0 && r >= FenceFt(SprayOf(s.p)))
                {
                    fenceT = tt;
                    overWall = s.p.y > WALL_H && Math.Abs(SprayOf(s.p)) <= FOUL_DEG;
                }
                return s.p.y <= 0 && tt > 0.05;
            }, 12);
            double distFt = Horiz(b.p) / FT, spray = SprayOf(b.p);
            bool foul = b.p.z < 0 || Math.Abs(spray) > FOUL_DEG;
            HitKind kind;
            if (overWall == true) kind = HitKind.HomeRun;
            else if (foul) kind = HitKind.Foul;
            else if (overWall == false) kind = HitKind.Wall;
            else if (launchDeg < 10) kind = HitKind.Grounder;
            else if (launchDeg < 25) kind = HitKind.Liner;
            else if (launchDeg > 50) kind = HitKind.Popup;
            else kind = HitKind.Fly;
            return new HitOutcome
            {
                kind = kind, distFt = (int)Math.Round(distFt), sprayDeg = spray, hangS = t,
                apexFt = (int)Math.Round(apex / FT), fenceT = fenceT, landing = b.p,
            };
        }
    }
}
