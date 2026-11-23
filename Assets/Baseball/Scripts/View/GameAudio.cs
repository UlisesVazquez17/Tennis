// Sonidos sintetizados al arrancar (no hacen falta archivos): batazo, guante, rugido del
// público y murmullo de fondo.

using UnityEngine;

namespace Baseball.View
{
    public class GameAudio : MonoBehaviour
    {
        [Range(0, 1)] public float volume = 0.8f;
        AudioSource fx, crowd;
        AudioClip crack, glove, roar, murmur;
        const int RATE = 44100;

        void Awake()
        {
            fx = gameObject.AddComponent<AudioSource>();
            crowd = gameObject.AddComponent<AudioSource>();
            var rnd = new System.Random(3);
            crack = Make("Batazo", 0.35f, (t, n) => n * Mathf.Exp(-t * 40) * 0.9f + Mathf.Sin(t * 2 * Mathf.PI * 1800) * Mathf.Exp(-t * 25) * 0.5f, rnd);
            glove = Make("Guante", 0.18f, (t, n) => (n * 0.6f + Mathf.Sin(t * 2 * Mathf.PI * 180) * 0.6f) * Mathf.Exp(-t * 45), rnd);
            float lp = 0;
            roar = Make("Rugido", 4f, (t, n) => { lp += (n - lp) * 0.08f; return lp * 2.2f * Mathf.Min(1, t * 3) * Mathf.Exp(-Mathf.Max(0, t - 1.2f) * 1.1f); }, rnd);
            float lp2 = 0;
            murmur = Make("Murmullo", 6f, (t, n) => { lp2 += (n - lp2) * 0.04f; return lp2 * 1.4f * (0.8f + 0.2f * Mathf.Sin(t * 1.3f)); }, rnd);
            crowd.clip = murmur; crowd.loop = true; crowd.volume = 0.25f * volume; crowd.Play();
        }

        static AudioClip Make(string name, float secs, System.Func<float, float, float> f, System.Random rnd)
        {
            int n = (int)(RATE * secs);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)RATE, (float)(rnd.NextDouble() * 2 - 1)), -1, 1);
            var clip = AudioClip.Create(name, n, 1, RATE, false);
            clip.SetData(data, 0);
            return clip;
        }

        // quality 0…1 (punto dulce) y power 0…1.
        public void Bat(double quality, double power)
        {
            fx.pitch = 0.85f + 0.3f * (float)quality;
            fx.PlayOneShot(crack, volume * (0.5f + 0.5f * (float)power));
        }

        public void Glove() { fx.pitch = 1; fx.PlayOneShot(glove, volume * 0.8f); }
        public void Roar() { fx.pitch = 1; fx.PlayOneShot(roar, volume); }
    }
}
