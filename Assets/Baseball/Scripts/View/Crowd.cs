// Público: miles de aficionados dibujados con instancias (un quad recortado por persona),
// sentados en los asientos que deja Stadium. Se mueven un poco todo el tiempo, se paran a
// celebrar los jonrones y hacen la ola alrededor del estadio.
//
// Las camisetas mezclan los colores del estadio (Apply) con colores al azar.

using System.Collections.Generic;
using Baseball.Config;
using UnityEngine;

namespace Baseball.View
{
    [ExecuteAlways]
    public class Crowd : MonoBehaviour
    {
        public Stadium stadium;
        [Range(0, 1)] public float occupancy = 0.78f;

        const int Variants = 8;           // combinaciones de camiseta/cabello en el atlas
        const int Poses = 2;              // sentado y con los brazos arriba
        const int Batch = 1023;

        struct Fan { public Vector3 pos; public Quaternion rot; public int variant; public float phase, energy, angle; }
        readonly List<Fan> fans = new List<Fan>();
        readonly Mesh[,] meshes = new Mesh[Variants, Poses];
        readonly List<Matrix4x4>[,] buckets = new List<Matrix4x4>[Variants, Poses];
        Material mat;
        Texture2D atlas;
        Color primary = new Color(0.04f, 0.16f, 0.44f), secondary = new Color(0.95f, 0.72f, 0.02f);

        float waveStart = -100, cheerUntil;
        const float WaveSeconds = 14, WaveLaps = 2;

        void OnEnable()
        {
            if (!stadium) stadium = FindAnyObjectByType<Stadium>();
            if (stadium) stadium.Built += Populate;
            BuildAssets();
            Populate();
        }

        void OnDisable()
        {
            if (stadium) stadium.Built -= Populate;
            if (mat) DestroyImmediate(mat);
            if (atlas) DestroyImmediate(atlas);
            foreach (var m in meshes) if (m) DestroyImmediate(m);
        }

        public void Apply(StadiumConfig c)
        {
            primary = c.Primary; secondary = c.Secondary;
            if (atlas) DestroyImmediate(atlas);
            atlas = Atlas();
            if (mat) mat.SetTexture("_BaseMap", atlas);
        }

        // La ola: da dos vueltas al estadio.
        public void Wave() => waveStart = Time.realtimeSinceStartup;
        public bool Waving => Time.realtimeSinceStartup - waveStart < WaveSeconds;

        // Todos de pie un momento (jonrón).
        public void Cheer(float seconds = 4) => cheerUntil = Time.realtimeSinceStartup + seconds;

        void Populate()
        {
            fans.Clear();
            if (!stadium) return;
            var rnd = new System.Random(11);
            foreach (var s in stadium.Seats)
            {
                if (rnd.NextDouble() > occupancy) continue;
                var jitter = new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f) * 0.12f;
                fans.Add(new Fan
                {
                    pos = s.pos + jitter - s.facing * 0.05f,
                    rot = Quaternion.LookRotation(-s.facing),
                    variant = rnd.Next(Variants),
                    phase = (float)rnd.NextDouble() * 10,
                    energy = 0.3f + (float)rnd.NextDouble() * 0.7f,
                    angle = s.angle,
                });
            }
        }

        void BuildAssets()
        {
            atlas = Atlas();
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave, enableInstancing = true };
            mat.SetTexture("_BaseMap", atlas);
            mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", 0.5f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Cull", 0);
            mat.SetColor("_BaseColor", new Color(1.15f, 1.15f, 1.15f));
            for (int v = 0; v < Variants; v++)
                for (int p = 0; p < Poses; p++)
                {
                    meshes[v, p] = FanMesh(v, p);
                    buckets[v, p] = new List<Matrix4x4>(4096);
                }
        }

        // Quad de 0.55 × 1.0 m con los pies en el origen, apuntando a una celda del atlas.
        static Mesh FanMesh(int v, int p)
        {
            float u0 = v / (float)Variants, u1 = (v + 1f) / Variants, v0 = p / (float)Poses, v1 = (p + 1f) / Poses;
            var m = new Mesh { hideFlags = HideFlags.DontSave };
            m.vertices = new[] { new Vector3(-0.28f, 0, 0), new Vector3(0.28f, 0, 0), new Vector3(0.28f, 1.0f, 0), new Vector3(-0.28f, 1.0f, 0) };
            m.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.bounds = new Bounds(Vector3.up * 0.5f, Vector3.one * 2);
            return m;
        }

        void Update()
        {
            if (!mat || fans.Count == 0) return;
            float t = Time.realtimeSinceStartup;
            float waveT = t - waveStart;
            bool waving = waveT < WaveSeconds;
            // La cresta de la ola recorre los ángulos (−180…180) alrededor del centro del campo.
            float crest = waving ? -180 + 360 * WaveLaps * (waveT / WaveSeconds) : 0;
            bool cheering = t < cheerUntil;

            foreach (var b in buckets) b.Clear();
            foreach (var f in fans)
            {
                float lift = 0; int pose = 0;
                if (waving)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(f.angle, crest));
                    float k = Mathf.Clamp01(1 - d / 16f);
                    lift = k * 0.32f; if (k > 0.35f) pose = 1;
                }
                if (cheering) { lift = Mathf.Max(lift, (0.2f + 0.12f * Mathf.Sin(t * 9 + f.phase)) * f.energy); if (f.energy > 0.45f) pose = 1; }
                // Siempre algo de movimiento: se acomodan, voltean.
                float idle = Mathf.Sin(t * (1.2f + f.energy) + f.phase) * 0.015f * f.energy;
                var pos = f.pos + Vector3.up * (lift + idle - 0.3f);
                buckets[f.variant, pose].Add(Matrix4x4.TRS(pos, f.rot, Vector3.one));
            }

            var rp = new RenderParams(mat) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true, worldBounds = new Bounds(new Vector3(0, 10, 45), Vector3.one * 400) };
            for (int v = 0; v < Variants; v++)
                for (int p = 0; p < Poses; p++)
                {
                    var list = buckets[v, p];
                    for (int start = 0; start < list.Count; start += Batch)
                        Graphics.RenderMeshInstanced(rp, meshes[v, p], 0, list, Mathf.Min(Batch, list.Count - start), start);
                }
        }

        // Atlas de 8 × 2 celdas (variantes × poses), 64 × 112 px cada una: siluetas sencillas
        // con cabeza, torso con la camiseta y brazos (abajo o arriba).
        Texture2D Atlas()
        {
            const int CW = 64, CH = 112;
            var tex = new Texture2D(CW * Variants, CH * Poses, TextureFormat.RGBA32, true) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[tex.width * tex.height];
            var rnd = new System.Random(5);
            Color[] skin = { new Color(0.95f, 0.8f, 0.66f), new Color(0.82f, 0.62f, 0.47f), new Color(0.6f, 0.42f, 0.29f), new Color(0.38f, 0.26f, 0.18f) };
            Color[] hair = { new Color(0.08f, 0.06f, 0.05f), new Color(0.3f, 0.2f, 0.1f), new Color(0.75f, 0.6f, 0.3f), new Color(0.15f, 0.15f, 0.15f) };
            Color[] other = { Color.white, new Color(0.15f, 0.15f, 0.17f), new Color(0.8f, 0.1f, 0.12f), new Color(0.2f, 0.5f, 0.85f), new Color(0.9f, 0.5f, 0.15f) };
            for (int v = 0; v < Variants; v++)
            {
                // Mitad de la gente con la camiseta del equipo.
                Color shirt = v < 3 ? primary : v < 5 ? secondary : other[rnd.Next(other.Length)];
                Color sk = skin[rnd.Next(skin.Length)], hr = hair[rnd.Next(hair.Length)];
                bool cap = rnd.NextDouble() < 0.4;
                for (int p = 0; p < Poses; p++)
                {
                    int ox = v * CW, oy = p * CH;
                    void Fill(float x0, float y0, float x1, float y1, Color c)
                    {
                        for (int y = Mathf.Max(0, (int)y0); y < Mathf.Min(CH, (int)y1); y++)
                            for (int x = Mathf.Max(0, (int)x0); x < Mathf.Min(CW, (int)x1); x++)
                                px[(oy + y) * tex.width + ox + x] = c;
                    }
                    void Circle(float cx, float cy, float r, Color c)
                    {
                        for (int y = (int)(cy - r); y <= cy + r; y++)
                            for (int x = (int)(cx - r); x <= cx + r; x++)
                                if (x >= 0 && x < CW && y >= 0 && y < CH && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) px[(oy + y) * tex.width + ox + x] = c;
                    }
                    Fill(18, 30, 46, 76, shirt);                   // torso
                    Fill(20, 6, 44, 32, shirt * 0.55f);             // piernas (sentado, se ven poco)
                    Circle(32, 86, 10, sk);                          // cabeza
                    Circle(32, 92, 9, hr); Fill(22, 84, 42, 92, sk); // cabello
                    if (cap) { Fill(21, 92, 43, 98, shirt); Fill(30, 89, 50, 92, shirt); }
                    if (p == 0) { Fill(10, 36, 18, 70, shirt); Fill(46, 36, 54, 70, shirt); Fill(10, 30, 18, 36, sk); Fill(46, 30, 54, 36, sk); }
                    else { Fill(8, 66, 16, 104, shirt); Fill(48, 66, 56, 104, shirt); Fill(8, 104, 16, 111, sk); Fill(48, 104, 56, 111, sk); }
                }
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }
    }
}
