// Home Run Derby: lanzamientos, swings, batazos y reglas. Sin MonoBehaviour.
//
// Tiempos en ms del reloj de la pantalla. El swing trae su marca de tiempo ya convertida
// a ese reloj y conecta si cae cerca del paso de la bola por el punto de contacto;
// mientras se decide, la bola va en cámara lenta.

using System;

namespace Baseball
{
    public enum GameMode { Idle, Ready, Windup, Pitch, Flight, Over }

    public enum EventType { Derby, Windup, Pitch, Contact, Call, Landed, HomeRun, Glove, Over }

    public class Pitch
    {
        public PitchType type;
        public string name;
        public double mph, tx, ty;
        public BallState start;
        public V3 cross;
        public double releaseAt, tContact, plateMph;
        public bool strike, arrived, done, caught;
        public SwingInput queued; public double queuedDt;
        public SwingInput swing; public string flag;   // "early" | "late"
    }

    // Swing del teléfono (o de la demo / el teclado).
    public class SwingInput
    {
        public string dir;      // "drive" | "reves"
        public double power, lift;
    }

    public class Batted
    {
        public BallState ball;
        public double t;
        public HitOutcome outcome;
        public SwingResult r;
        public bool landed, hrAnnounced;
    }

    public class GameEvent
    {
        public EventType type;
        public Derby derby;
        public Pitch pitch;
        public SwingResult r;
        public HitOutcome outcome;
        public string text;
        public string callKind;     // "whiff" | "take"
        public V3 point;
        public string reason;
        public PitchResult? last;
    }

    public class DerbyGame
    {
        public const double READY_MS = 1600, WINDUP_MS = 1500, GRACE_MS = 160, SLOWMO = 0.2;
        public const double CALL_MS = 1600, HIT_MS = 3600, HR_MS = 5200, IDLE_MS = 45000;

        public readonly int pitches, outs, level;
        public GameMode mode = GameMode.Idle;
        public char hand = 'R';
        public double graceMs = GRACE_MS;
        public Derby derby;
        public Pitch pitch;
        public BallState ball;
        public Batted batted;
        public double lastSwingAt;

        public event Action<GameEvent> OnEvent;
        readonly Random rnd;
        double nextT; Action<double> nextFn;

        public DerbyGame(int pitches = 10, int outs = int.MaxValue, int level = 1, int? seed = null)
        {
            this.pitches = pitches; this.outs = outs; this.level = level;
            rnd = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        void Emit(GameEvent e) => OnEvent?.Invoke(e);
        void Schedule(double t, Action<double> fn) { nextT = t; nextFn = fn; }

        // Recorre lo que esté programado (la repetición del jonrón detiene el reloj del derby).
        public void Delay(double ms) { if (nextFn != null) nextT += ms; }

        static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));

        // Mezcla de lanzamientos: al principio rectas suaves; luego entran los rompimientos.
        public static (PitchType type, double mph, double tx, double ty) PlanPitch(int index, int total, int level, Random r)
        {
            double k = Clamp(index / (double)Math.Max(1, total - 1), 0, 1);
            double boost = (level - 1) * 8;
            var pool = new (PitchType, double)[] {
                (PitchType.Recta, 1), (PitchType.Cambio, 0.5 * k), (PitchType.Curva, 0.4 * k),
                (PitchType.Slider, 0.4 * k), (PitchType.Sinker, 0.3 * k),
            };
            double sum = 0; foreach (var (_, w) in pool) sum += w;
            double pick = r.NextDouble() * sum; var type = PitchType.Recta;
            foreach (var (name, w) in pool) { if ((pick -= w) <= 0) { type = name; break; } }
            double bas;
            switch (type)
            {
                case PitchType.Sinker: bas = 64 + 10 * k; break;
                case PitchType.Cambio: bas = 58 + 8 * k; break;
                case PitchType.Curva: bas = 56 + 10 * k; break;
                case PitchType.Slider: bas = 62 + 10 * k; break;
                default: bas = 66 + 12 * k; break;
            }
            double mph = bas + boost + (r.NextDouble() - 0.5) * 3;
            // 85 % en la zona; el resto en las orillas.
            double tx, ty;
            if (r.NextDouble() < 0.85) { tx = (r.NextDouble() - 0.5) * 0.36; ty = 0.55 + r.NextDouble() * 0.45; }
            else { tx = (r.NextDouble() < 0.5 ? -1 : 1) * (0.28 + r.NextDouble() * 0.12); ty = 0.4 + r.NextDouble() * 0.8; }
            return (type, mph, tx, ty);
        }

        // Descripción de transmisión: "Elevado al central", "Rola al short"…
        public static string DescribeHit(HitOutcome o)
        {
            if (o.kind == HitKind.HomeRun) return "¡Jonrón!";
            if (o.kind == HitKind.Foul) return "Foul";
            if (o.kind == HitKind.Wall) return "Contra la barda";
            double a = o.sprayDeg; bool infield = o.distFt < 150;
            string zone = infield
                ? (Math.Abs(a) < 6 && o.distFt < 95 ? "al pitcher" : a < -27 ? "a tercera" : a < -4 ? "al short" : a < 27 ? "a segunda" : "a primera")
                : (a < -15 ? "al jardín izquierdo" : a > 15 ? "al jardín derecho" : "al central");
            string kind = o.kind == HitKind.Grounder ? "Rola" : o.kind == HitKind.Liner ? "Línea" : "Elevado";
            return $"{kind} {zone}";
        }

        public void Start(double now)
        {
            derby = new Derby(pitches, outs);
            ball = null; batted = null; pitch = null;
            lastSwingAt = now;
            mode = GameMode.Ready;
            Emit(new GameEvent { type = EventType.Derby, derby = derby });
            Schedule(now + READY_MS + 600, Windup);
        }

        // ---------- Lanzamiento ----------
        void Windup(double now)
        {
            var plan = PlanPitch(derby.pitch, derby.pitchesMax, level, rnd);
            var (start, cross, _) = BallPhysics.SolvePitch(plan.type, plan.mph, plan.tx, plan.ty);
            var atContact = BallPhysics.Simulate(start, (s, _) => s.p.z <= BallPhysics.CONTACT_Z, 3);
            var atPlate = BallPhysics.Simulate(start, (s, _) => s.p.z <= 0, 3);
            double releaseAt = now + WINDUP_MS;
            pitch = new Pitch
            {
                type = plan.type, name = BallPhysics.Spec(plan.type).name, mph = plan.mph, tx = plan.tx, ty = plan.ty,
                start = start, cross = cross, releaseAt = releaseAt,
                tContact = releaseAt + atContact.t * 1000,
                plateMph = atPlate.b.v.Length / BallPhysics.MPH,
                strike = BallPhysics.IsStrike(cross.x, cross.y),
            };
            mode = GameMode.Windup;
            Emit(new GameEvent { type = EventType.Windup, pitch = pitch });
            Schedule(releaseAt, Release);
        }

        void Release(double now)
        {
            ball = pitch.start.Clone();
            mode = GameMode.Pitch;
            Emit(new GameEvent { type = EventType.Pitch, pitch = pitch });
        }

        // ---------- Swing ----------
        // ts: momento del swing en el reloj de la pantalla. Devuelve qué pasó con él.
        public string Swing(SwingInput e, double ts, double now)
        {
            lastSwingAt = now;
            // Un swing hacia el lado contrario (al preparar el bate) no es un intento.
            string expected = hand == 'L' ? "reves" : "drive";
            if (!string.IsNullOrEmpty(e.dir) && e.dir != expected) return "ignored";
            var p = pitch;
            if (p == null || p.done || (mode != GameMode.Pitch && mode != GameMode.Windup)) return "ignored";
            double dt = ts - p.tContact;
            if (Math.Abs(dt) <= Contact.WINDOW_MS)
            {
                if (!p.arrived) { p.queued = e; p.queuedDt = dt; return "queued"; }
                DoContact(e, dt, now);
                return "hit";
            }
            // Fuera de la ventana: swing fallado, salvo que aún pueda llegar uno bueno.
            if (dt < 0) { if (-dt < 1200) { p.swing = e; p.flag ??= "early"; } return "early"; }
            p.swing = e; p.flag = "late";
            return "late";
        }

        void DoContact(SwingInput e, double dt, double now)
        {
            var p = pitch;
            p.done = true;
            var r = Contact.Swing(dt, e.power, e.lift, p.plateMph, ball.p.y, hand, rnd);
            if (r.whiff) { Call("whiff", now, dt < 0 ? "Muy adelantado" : "Muy atrasado"); return; }
            var pos = new V3(ball.p.x, ball.p.y, Math.Max(ball.p.z, 0.1));
            var bb = BallPhysics.BattedBall(r.evMph, r.launchDeg, r.sprayDeg, pos);
            var outcome = BallPhysics.BattedOutcome(bb, r.launchDeg);
            ball = null;
            batted = new Batted { ball = bb, outcome = outcome, r = r };
            mode = GameMode.Flight;
            Emit(new GameEvent { type = EventType.Contact, r = r, outcome = outcome, text = DescribeHit(outcome) });
            double hold = outcome.kind == HitKind.HomeRun ? HR_MS : outcome.kind == HitKind.Foul ? CALL_MS + 600 : HIT_MS;
            var res = Derby.FromHit(outcome.kind);
            Schedule(now + hold, t => Settle(t, res, outcome.distFt, r.evMph));
        }

        // Se resuelve un lanzamiento sin contacto: swing fallado o "Strike"/"Bola".
        void Call(string kind, double now, string reason = null)
        {
            var p = pitch;
            p.done = true;
            string text = kind == "whiff" ? (reason != null ? $"Swing y fallo · {reason}" : "Swing y fallo") : (p.strike ? "Strike" : "Bola");
            Emit(new GameEvent { type = EventType.Call, callKind = kind, text = text, pitch = p, point = p.cross });
            var res = kind == "take" ? (p.strike ? PitchResult.Strike : PitchResult.Ball) : PitchResult.Whiff;
            Schedule(now + CALL_MS, t => Settle(t, res));
        }

        void Settle(double now, PitchResult result, int distFt = 0, double evMph = 0)
        {
            derby.Record(result, distFt, evMph);
            Emit(new GameEvent { type = EventType.Derby, derby = derby, last = result });
            ball = null; batted = null;
            if (derby.over) { Finish(); return; }
            mode = GameMode.Ready;
            Schedule(now + READY_MS, Windup);
        }

        public void Finish(string reason = "")
        {
            mode = GameMode.Over;
            nextFn = null;
            Emit(new GameEvent { type = EventType.Over, derby = derby, reason = reason });
        }

        public void Stop() { mode = GameMode.Idle; nextFn = null; ball = null; batted = null; pitch = null; }

        // 0…1: cercanía de la bola al punto de contacto (para resaltar la zona).
        public double HitZone(double now)
        {
            var p = pitch;
            if (p == null || mode != GameMode.Pitch || p.done) return 0;
            return Clamp(1 - Math.Abs(now - p.tContact) / 300, 0, 1);
        }

        // ---------- Bucle ----------
        // dt en segundos, now en ms.
        public void Update(double dt, double now)
        {
            if (nextFn != null && now >= nextT) { var fn = nextFn; nextFn = null; fn(now); }
            if (mode == GameMode.Idle || mode == GameMode.Over) return;
            if (now - lastSwingAt > IDLE_MS) { Finish("inactividad"); return; }

            if (mode == GameMode.Pitch && ball != null)
            {
                var p = pitch;
                double scale = 1;
                if (!p.done && now >= p.tContact)
                {
                    if (!p.arrived)
                    {
                        p.arrived = true;
                        if (p.queued != null) { DoContact(p.queued, p.queuedDt, now); return; }
                    }
                    if (now >= p.tContact + Contact.WINDOW_MS + graceMs)
                    {
                        if (p.swing != null) Call("whiff", now, p.flag == "early" ? "Muy adelantado" : "Muy atrasado");
                        else Call("take", now);
                    }
                    else scale = SLOWMO;
                }
                // La bola sigue hasta el guante del catcher.
                if (ball.p.z > -0.55)
                {
                    for (double t = 0; t < dt * scale; t += BallPhysics.DT) BallPhysics.Step(ball, BallPhysics.DT);
                }
                else if (!p.caught)
                {
                    p.caught = true;
                    Emit(new GameEvent { type = EventType.Glove });
                }
            }

            if (mode == GameMode.Flight && batted != null)
            {
                var b = batted;
                for (double t = 0; t < dt; t += BallPhysics.DT)
                {
                    if (b.landed && Math.Sqrt(b.ball.v.x * b.ball.v.x + b.ball.v.z * b.ball.v.z) < 0.5) break;
                    BallPhysics.Step(b.ball, BallPhysics.DT);
                    b.t += BallPhysics.DT;
                    if (b.ball.p.y <= 0 && b.ball.v.y < 0)
                    {
                        var s = b.ball;
                        s.p = new V3(s.p.x, 0, s.p.z);
                        s.v = new V3(s.v.x * 0.6, -s.v.y * 0.35, s.v.z * 0.6);
                        s.w = new V3(0, 0, 0);
                        if (!b.landed) Emit(new GameEvent { type = EventType.Landed, point = s.p, outcome = b.outcome });
                        b.landed = true;
                    }
                }
                if (b.outcome.kind == HitKind.HomeRun && !b.hrAnnounced && b.outcome.fenceT.HasValue && b.t >= b.outcome.fenceT.Value)
                {
                    b.hrAnnounced = true;
                    Emit(new GameEvent { type = EventType.HomeRun, outcome = b.outcome, r = b.r });
                }
            }
        }
    }
}
