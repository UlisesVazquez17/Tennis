// Swing detectado con la IMU del bate. Lógica pura, sin MonoBehaviour.
//
// Mide la velocidad de giro entre muestras con la hora de la placa (por Bluetooth los
// paquetes llegan en ráfagas). Al pasar de minDps sigue buscando el pico y dispara cuando
// el giro cae, o cuando pasa maxWaitMs: ese pico es el momento del swing. La potencia sale
// de la velocidad del pico y el ángulo (lift), de qué tanto sube la punta del bate.

using UnityEngine;

namespace Baseball.Pose
{
    public class ImuSwing
    {
        public struct Hit { public double power, lift, deviceMs; public float dps; }

        public float minDps = 350;      // °/s para contar como swing
        public float fullDps = 1100;    // °/s que dan potencia 1
        public float cooldownMs = 600;  // entre swings
        public float maxWaitMs = 150;   // espera máxima por el pico

        bool hasPrev, armed;
        Quaternion prevQ; double prevMs, lastHitMs = double.NegativeInfinity;
        float bestDps, bestLift; double bestMs;

        public float Dps { get; private set; }

        // q: orientación absoluta; deviceMs: hora de la placa. Devuelve un swing al detectarlo.
        public Hit? Feed(Quaternion q, double deviceMs)
        {
            if (!hasPrev || deviceMs <= prevMs) { hasPrev = true; prevQ = q; prevMs = deviceMs; return null; }
            float dt = (float)(deviceMs - prevMs) / 1000f;
            if (dt > 0.5f) { armed = false; prevQ = q; prevMs = deviceMs; Dps = 0; return null; }   // hueco de datos

            float deg = Quaternion.Angle(prevQ, q);
            Dps = deg / dt;
            float rise = (q * Vector3.up).y - (prevQ * Vector3.up).y;
            float lift = deg > 0.01f ? Mathf.Clamp(rise / (deg * Mathf.Deg2Rad), -1, 1) : 0;
            prevQ = q; prevMs = deviceMs;

            if (!armed)
            {
                if (Dps < minDps || deviceMs - lastHitMs < cooldownMs) return null;
                armed = true; bestDps = Dps; bestLift = lift; bestMs = deviceMs;
                return null;
            }
            if (Dps > bestDps) { bestDps = Dps; bestLift = lift; bestMs = deviceMs; }
            if (Dps >= bestDps * 0.7f && deviceMs - bestMs < maxWaitMs) return null;

            armed = false;
            lastHitMs = bestMs;
            return new Hit
            {
                dps = bestDps,
                power = Mathf.Clamp01(0.3f + 0.7f * Mathf.InverseLerp(minDps, fullDps, bestDps)),
                lift = bestLift,
                deviceMs = bestMs,
            };
        }
    }
}
