// Swing detectado con el cuerpo, para jugar sin teléfono: cuando las dos manos barren
// rápido de lado a lado frente al cuerpo, cuenta como swing. La potencia sale de la
// velocidad de las manos y el ángulo (lift), de qué tanto suben durante el barrido.

using UnityEngine;

namespace Baseball.Pose
{
    public class BodySwing
    {
        public struct Hit { public double power, lift, agoMs; public float speed; }

        public float minSpeed = 2.2f;      // m/s de las manos para contar como swing
        public float fullSpeed = 7f;       // m/s que dan potencia 1
        public float cooldown = 1.0f;      // segundos entre swings
        public float cameraLatencyMs = 70; // la imagen llega con este atraso aproximado

        readonly BodyTracker tracker;
        int lastSample = -1;
        Vector3 lastHands; float lastTime, lastSwing = -10;

        public BodySwing(BodyTracker t) { tracker = t; }

        public Hit? Update(char hand)
        {
            if (!tracker.Tracking || tracker.Samples == lastSample) return null;
            lastSample = tracker.Samples;
            var w = tracker.World;
            var hands = (w[BodyTracker.LWrist] + w[BodyTracker.RWrist]) * 0.5f;
            float t = tracker.SampleTime, dt = t - lastTime;
            var prev = lastHands; lastHands = hands; lastTime = t;
            if (dt <= 0 || dt > 0.25f) return null;
            var v = (hands - prev) / dt;
            float side = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            if (side < minSpeed || t - lastSwing < cooldown) return null;
            // Las manos deben estar juntas (agarrando el bate), no un brazo suelto.
            if (Vector3.Distance(w[BodyTracker.LWrist], w[BodyTracker.RWrist]) > 0.35f) return null;
            lastSwing = t;
            return new Hit
            {
                speed = side,
                power = Mathf.Clamp01((side - minSpeed) / (fullSpeed - minSpeed) * 0.7f + 0.3f),
                lift = Mathf.Clamp(v.y / Mathf.Max(side, 0.1f), -1, 1),
                agoMs = (Time.realtimeSinceStartup - t) * 1000 + cameraLatencyMs,
            };
        }
    }
}
