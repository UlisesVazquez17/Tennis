// Reglas del Home Run Derby.
//
// La ronda tiene un máximo de lanzamientos. Gastan lanzamiento los swings (con o sin
// contacto) y los strikes que se dejan pasar; una bola que se deja pasar no cuenta.
// Opcionalmente hay un límite de outs: todo batazo en juego que no sea jonrón es out.

using System;

namespace Baseball
{
    public enum PitchResult { HomeRun, Foul, Whiff, Strike, Ball, Wall, Fly, Liner, Grounder, Popup }

    public class Derby
    {
        public int pitchesMax, outsMax;
        public int pitch, outs, homeRuns, totalFt, longestFt;
        public double maxEvMph;
        public bool over;
        // Lo que pasó en cada lanzamiento que contó (para el videomarcador).
        public readonly System.Collections.Generic.List<(PitchResult result, int distFt)> log = new System.Collections.Generic.List<(PitchResult, int)>();

        public Derby(int pitches = 10, int outs = int.MaxValue) { pitchesMax = pitches; outsMax = outs; }

        public static PitchResult FromHit(HitKind k)
        {
            switch (k)
            {
                case HitKind.HomeRun: return PitchResult.HomeRun;
                case HitKind.Foul: return PitchResult.Foul;
                case HitKind.Wall: return PitchResult.Wall;
                case HitKind.Liner: return PitchResult.Liner;
                case HitKind.Grounder: return PitchResult.Grounder;
                case HitKind.Popup: return PitchResult.Popup;
                default: return PitchResult.Fly;
            }
        }

        static bool InPlayOut(PitchResult r) =>
            r == PitchResult.Fly || r == PitchResult.Liner || r == PitchResult.Grounder || r == PitchResult.Popup || r == PitchResult.Wall;

        public void Record(PitchResult r, int distFt = 0, double evMph = 0)
        {
            if (over || r == PitchResult.Ball) return;
            pitch++;
            log.Add((r, distFt));
            if (r == PitchResult.HomeRun)
            {
                homeRuns++;
                totalFt += distFt;
                longestFt = Math.Max(longestFt, distFt);
            }
            else if (InPlayOut(r)) outs++;
            if (evMph > 0) maxEvMph = Math.Max(maxEvMph, evMph);
            over = pitch >= pitchesMax || outs >= outsMax;
        }
    }
}
