// Jugadores provisionales hechos con primitivas: bateador, pitcher, catcher, ampáyer y
// fildeadores. Tienen articulaciones (hombros, codos, cadera) para animar el lanzamiento,
// el swing y la carrera. Están pensados para cambiarse por personajes con rig (Mixamo)
// sin tocar el resto del juego: cada uno expone Pose(t) o métodos equivalentes.

using System.Collections.Generic;
using UnityEngine;

namespace Baseball.View
{
    public class Figure
    {
        public Transform root, hips, chest, head, armL, armR, foreL, foreR, legL, legR, shinL, shinR;
        Material jerseyMat, pantsMat, capMat;

        static Material Mat(Color c, float smooth = 0.25f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            m.hideFlags = HideFlags.DontSave;
            return m;
        }

        static Transform Part(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.gameObject.hideFlags = HideFlags.DontSave;
            t.SetParent(parent, false); t.localPosition = pos;
            return t;
        }

        static void Shape(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.hideFlags = HideFlags.DontSave;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        // Una cápsula de largo `len` que cuelga hacia abajo desde la articulación.
        static void Limb(Transform joint, float len, float thick, Material m) =>
            Shape(PrimitiveType.Capsule, joint, new Vector3(0, -len / 2, 0), new Vector3(thick, len / 2, thick), m);

        public Figure(string name, Transform parent, Color jersey, Color pants, Color cap, Color skin)
        {
            Material mj = Mat(jersey), mp = Mat(pants), mc = Mat(cap, 0.5f), ms = Mat(skin, 0.3f), shoe = Mat(new Color(0.08f, 0.08f, 0.08f), 0.5f);
            jerseyMat = mj; pantsMat = mp; capMat = mc;
            root = Part(name, parent, Vector3.zero);
            hips = Part("Cadera", root, new Vector3(0, 0.95f, 0));
            Shape(PrimitiveType.Capsule, hips, new Vector3(0, 0.02f, 0), new Vector3(0.34f, 0.12f, 0.22f), mp);
            chest = Part("Torso", hips, new Vector3(0, 0.1f, 0));
            Shape(PrimitiveType.Capsule, chest, new Vector3(0, 0.27f, 0), new Vector3(0.4f, 0.27f, 0.25f), mj);
            head = Part("Cabeza", chest, new Vector3(0, 0.62f, 0));
            Shape(PrimitiveType.Sphere, head, new Vector3(0, 0.1f, 0), new Vector3(0.21f, 0.24f, 0.22f), ms);
            Shape(PrimitiveType.Sphere, head, new Vector3(0, 0.17f, 0), new Vector3(0.23f, 0.14f, 0.24f), mc);
            Shape(PrimitiveType.Cube, head, new Vector3(0, 0.15f, 0.13f), new Vector3(0.2f, 0.02f, 0.12f), mc);

            armL = Part("Brazo izq", chest, new Vector3(-0.24f, 0.5f, 0)); Limb(armL, 0.3f, 0.11f, mj);
            foreL = Part("Antebrazo izq", armL, new Vector3(0, -0.3f, 0)); Limb(foreL, 0.28f, 0.09f, ms);
            armR = Part("Brazo der", chest, new Vector3(0.24f, 0.5f, 0)); Limb(armR, 0.3f, 0.11f, mj);
            foreR = Part("Antebrazo der", armR, new Vector3(0, -0.3f, 0)); Limb(foreR, 0.28f, 0.09f, ms);

            legL = Part("Pierna izq", hips, new Vector3(-0.11f, -0.02f, 0)); Limb(legL, 0.45f, 0.15f, mp);
            shinL = Part("Espinilla izq", legL, new Vector3(0, -0.45f, 0)); Limb(shinL, 0.45f, 0.12f, mp);
            Shape(PrimitiveType.Cube, shinL, new Vector3(0, -0.46f, 0.05f), new Vector3(0.11f, 0.07f, 0.26f), shoe);
            legR = Part("Pierna der", hips, new Vector3(0.11f, -0.02f, 0)); Limb(legR, 0.45f, 0.15f, mp);
            shinR = Part("Espinilla der", legR, new Vector3(0, -0.45f, 0)); Limb(shinR, 0.45f, 0.12f, mp);
            Shape(PrimitiveType.Cube, shinR, new Vector3(0, -0.46f, 0.05f), new Vector3(0.11f, 0.07f, 0.26f), shoe);
        }

        // Uniforme con los colores del estadio.
        public void Recolor(Color jersey, Color pants, Color cap)
        {
            jerseyMat.SetColor("_BaseColor", jersey); pantsMat.SetColor("_BaseColor", pants); capMat.SetColor("_BaseColor", cap);
        }

        public void Place(Vector3 pos, float yaw) { root.localPosition = pos; root.localRotation = Quaternion.Euler(0, yaw, 0); }

        public void Neutral()
        {
            foreach (var t in new[] { hips, chest, head, armL, armR, foreL, foreR, legL, legR, shinL, shinR }) t.localRotation = Quaternion.identity;
            hips.localPosition = new Vector3(0, 0.95f, 0);
            armL.localRotation = Quaternion.Euler(0, 0, -8); armR.localRotation = Quaternion.Euler(0, 0, 8);
        }

        // Postura de agachado (catcher, fildeadores listos).
        public void Crouch(float k)
        {
            hips.localPosition = new Vector3(0, Mathf.Lerp(0.95f, 0.5f, k), 0);
            legL.localRotation = Quaternion.Euler(-95 * k, 0, -15 * k); shinL.localRotation = Quaternion.Euler(120 * k, 0, 0);
            legR.localRotation = Quaternion.Euler(-95 * k, 0, 15 * k); shinR.localRotation = Quaternion.Euler(120 * k, 0, 0);
            chest.localRotation = Quaternion.Euler(25 * k, 0, 0);
        }

        // Ciclo de carrera; phase en radianes.
        public void Run(float phase, float k)
        {
            float s = Mathf.Sin(phase) * k;
            legL.localRotation = Quaternion.Euler(-45 * s, 0, 0); shinL.localRotation = Quaternion.Euler(Mathf.Max(0, 60 * Mathf.Sin(phase + 1.2f)) * k, 0, 0);
            legR.localRotation = Quaternion.Euler(45 * s, 0, 0); shinR.localRotation = Quaternion.Euler(Mathf.Max(0, -60 * Mathf.Sin(phase + 1.2f)) * k, 0, 0);
            armL.localRotation = Quaternion.Euler(50 * s, 0, -10); armR.localRotation = Quaternion.Euler(-50 * s, 0, 10);
            foreL.localRotation = foreR.localRotation = Quaternion.Euler(-70 * k, 0, 0);
            chest.localRotation = Quaternion.Euler(12 * k, 0, 0);
            hips.localPosition = new Vector3(0, 0.95f - 0.04f * k + 0.04f * Mathf.Abs(Mathf.Cos(phase)) * k, 0);
        }
    }

    // ---------- Pitcher ----------
    public class Pitcher : IPitcher
    {
        public const float RELEASE_AT = 0.72f;   // fracción del movimiento en que suelta la bola
        public readonly Figure f;
        public Pitcher(Transform parent)
        {
            f = new Figure("Pitcher", parent, new Color(0.92f, 0.92f, 0.94f), new Color(0.9f, 0.9f, 0.92f), new Color(0.05f, 0.15f, 0.4f), new Color(0.82f, 0.62f, 0.48f));
            f.Place(new Vector3(0, 0.25f, (float)BallPhysics.MOUND_Z), 180);
        }

        // t: 0 = de pie, RELEASE_AT = suelta la bola, 1 = terminó.
        public void Pose(float t)
        {
            f.Neutral();
            if (t <= 0) return;
            // Pierna izquierda arriba, brazo derecho atrás, zancada y suelta, seguimiento.
            float lift = Smooth(0.05f, 0.4f, t) * (1 - Smooth(0.45f, 0.62f, t));
            float stride = Smooth(0.45f, 0.7f, t);
            float throwK = Smooth(0.55f, RELEASE_AT, t);
            float follow = Smooth(RELEASE_AT, 1f, t);
            f.legL.localRotation = Quaternion.Euler(-80 * lift - 40 * stride * (1 - follow), 0, 0);
            f.shinL.localRotation = Quaternion.Euler(90 * lift + 20 * stride, 0, 0);
            f.legR.localRotation = Quaternion.Euler(25 * stride, 0, 0);
            f.chest.localRotation = Quaternion.Euler(10 * stride + 35 * follow, -60 * lift + 70 * throwK, 0);
            f.hips.localPosition = new Vector3(0, 0.95f - 0.25f * stride, 0.6f * stride);
            float back = Smooth(0.25f, 0.55f, t) * (1 - throwK);
            f.armR.localRotation = Quaternion.Euler(170 * back - 60 * throwK * (1 - follow) - 200 * follow * 0 + 150 * throwK, 0, 40 * back + 20);
            f.foreR.localRotation = Quaternion.Euler(-90 * back - 20 * throwK, 0, 0);
            f.armL.localRotation = Quaternion.Euler(-80 * lift + 40 * throwK, 0, -40);
            f.foreL.localRotation = Quaternion.Euler(-60, 0, 0);
        }

        // Punto aproximado de la mano para dibujar la bola antes de soltarla.
        public Vector3 HandPos => f.foreR.TransformPoint(new Vector3(0, -0.3f, 0));

        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, t));
    }

    // ---------- Bateador ----------
    public interface IBatter
    {
        char hand { get; }
        void SetHand(char h);
        void Swing(float power);
        void Reset();
        void Update(float dt);
    }

    public class Batter : IBatter
    {
        public readonly Figure f;
        public readonly Transform bat;
        public char hand { get; private set; } = 'R';
        float swingT = -1, power;

        public Batter(Transform parent)
        {
            f = new Figure("Bateador", parent, new Color(0.08f, 0.16f, 0.38f), new Color(0.88f, 0.88f, 0.9f), new Color(0.75f, 0.1f, 0.12f), new Color(0.62f, 0.44f, 0.32f));
            // El bate: un pivote en las manos, con el barril de 0.86 m.
            bat = new GameObject("Bate").transform;
            bat.gameObject.hideFlags = HideFlags.DontSave;
            bat.SetParent(f.chest, false);
            var wood = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
            wood.SetColor("_BaseColor", new Color(0.72f, 0.52f, 0.3f)); wood.SetFloat("_Smoothness", 0.6f);
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.hideFlags = HideFlags.DontSave;
            Object.DestroyImmediate(barrel.GetComponent<Collider>());
            barrel.transform.SetParent(bat, false);
            barrel.transform.localPosition = new Vector3(0, 0.43f, 0);
            barrel.transform.localScale = new Vector3(0.06f, 0.43f, 0.06f);
            barrel.GetComponent<MeshRenderer>().sharedMaterial = wood;
            SetHand('R');
        }

        public void SetHand(char h)
        {
            hand = h;
            // El derecho se para del lado de tercera (x < 0) mirando al home (+x).
            float side = h == 'L' ? 1 : -1;
            f.Place(new Vector3(side * 0.85f, 0, 0.2f), h == 'L' ? -90 : 90);
            f.root.localScale = new Vector3(h == 'L' ? -1 : 1, 1, 1);
        }

        public void Swing(float pow) { swingT = 0; power = Mathf.Clamp01(pow); }
        public void Reset() => swingT = -1;

        public void Update(float dt)
        {
            if (swingT >= 0) swingT += dt / Mathf.Lerp(0.28f, 0.18f, power);
            float t = Mathf.Clamp01(swingT < 0 ? 0 : swingT);
            f.Neutral();
            // Postura: rodillas flexionadas, manos atrás de la oreja, bate arriba.
            f.legL.localRotation = Quaternion.Euler(-15, 0, -10); f.shinL.localRotation = Quaternion.Euler(25, 0, 0);
            f.legR.localRotation = Quaternion.Euler(-15, 0, 10); f.shinR.localRotation = Quaternion.Euler(25, 0, 0);
            f.hips.localPosition = new Vector3(0, 0.88f, 0);
            float k = Mathf.SmoothStep(0, 1, t);
            // La cadera y el torso giran hacia el pitcher; el bate barre de atrás hacia adelante.
            f.chest.localRotation = Quaternion.Euler(15, -95 * k, 0);
            f.armL.localRotation = Quaternion.Euler(-70 - 20 * k, 30 - 60 * k, -20);
            f.armR.localRotation = Quaternion.Euler(-80 - 10 * k, -10 - 40 * k, 20);
            f.foreL.localRotation = Quaternion.Euler(-40 + 30 * k, 0, 0);
            f.foreR.localRotation = Quaternion.Euler(-70 + 40 * k, 0, 0);
            bat.localPosition = Vector3.Lerp(new Vector3(0.12f, 0.75f, -0.1f), new Vector3(0.05f, 0.45f, 0.35f), k);
            bat.localRotation = Quaternion.Euler(Mathf.Lerp(-35, 0, k), Mathf.Lerp(0, -200, k), Mathf.Lerp(-20, -95, Mathf.Sin(k * Mathf.PI) * 0.6f + k * 0.4f));
        }
    }

    // ---------- Fildeadores ----------
    public class Fielders
    {
        readonly List<(Figure f, Vector3 home)> all = new List<(Figure, Vector3)>();
        Vector3 target; bool chasing; float phase;

        static readonly Vector3[] Spots = {
            new Vector3(19.5f, 0, 24f),  // primera
            new Vector3(8f, 0, 35f),     // segunda
            new Vector3(-8.5f, 0, 35f),  // short
            new Vector3(-20f, 0, 24f),   // tercera
            new Vector3(-58f, 0, 82f),   // izquierdo
            new Vector3(0, 0, 100f),     // central
            new Vector3(58f, 0, 82f),    // derecho
        };

        public void Recolor(Color jersey, Color pants, Color cap) { foreach (var (f, _) in all) f.Recolor(jersey, pants, cap); }

        public Fielders(Transform parent)
        {
            foreach (var p in Spots)
            {
                var f = new Figure("Fildeador", parent, new Color(0.92f, 0.92f, 0.94f), new Color(0.9f, 0.9f, 0.92f), new Color(0.05f, 0.15f, 0.4f), new Color(0.8f, 0.6f, 0.46f));
                f.Place(p, Yaw(p, Vector3.zero));
                all.Add((f, p));
            }
            Reset();
        }

        static float Yaw(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;

        public void Reset()
        {
            chasing = false;
            foreach (var (f, p) in all) { f.Place(p, Yaw(p, Vector3.zero)); f.Neutral(); f.Crouch(0.25f); }
        }

        // El más cercano corre a donde cae la bola; los demás se acercan un poco.
        public void Chase(Vector3 landing) { target = new Vector3(landing.x, 0, landing.z); chasing = true; }

        public void Update(float dt)
        {
            if (!chasing) return;
            phase += dt * 11;
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < all.Count; i++) { float d = Vector3.Distance(all[i].home, target); if (d < bd) { bd = d; best = i; } }
            for (int i = 0; i < all.Count; i++)
            {
                var (f, _) = all[i];
                var pos = f.root.localPosition;
                var to = target - pos; to.y = 0;
                float speed = i == best ? 8.5f : 2.5f;
                bool arrived = to.magnitude < (i == best ? 0.6f : 25f);
                if (!arrived) pos += to.normalized * Mathf.Min(speed * dt, to.magnitude);
                f.Place(pos, Yaw(pos, arrived ? Vector3.zero : target));
                f.Neutral();
                if (!arrived) f.Run(phase + i, 1); else f.Crouch(0.15f);
            }
        }
    }

    // ---------- Catcher y ampáyer ----------
    public class Battery
    {
        public readonly Figure catcher, umpire;
        Vector3 glove;

        public Battery(Transform parent)
        {
            catcher = new Figure("Catcher", parent, new Color(0.92f, 0.92f, 0.94f), new Color(0.9f, 0.9f, 0.92f), new Color(0.05f, 0.15f, 0.4f), new Color(0.7f, 0.5f, 0.38f));
            catcher.Place(new Vector3(0, 0, -0.95f), 0);
            umpire = new Figure("Ampáyer", parent, new Color(0.12f, 0.12f, 0.14f), new Color(0.35f, 0.36f, 0.4f), new Color(0.1f, 0.1f, 0.1f), new Color(0.85f, 0.66f, 0.5f));
            SetHand('R');
            Target(0, 0.8f);
        }

        // El ampáyer se asoma por el lado contrario al bateador.
        public void SetHand(char h) => umpire.Place(new Vector3(h == 'L' ? -0.35f : 0.35f, 0, -1.75f), 0);

        // En la toma de bateo la cámara queda justo detrás del ampáyer y el catcher: se ocultan para no tapar.
        public void ShowUmpire(bool on)
        {
            if (umpire.root.gameObject.activeSelf != on) umpire.root.gameObject.SetActive(on);
            if (catcher.root.gameObject.activeSelf != on) catcher.root.gameObject.SetActive(on);
        }

        public void Target(double tx, double ty) => glove = new Vector3((float)tx, (float)ty, -0.5f);

        public void Update()
        {
            catcher.Neutral(); catcher.Crouch(1);
            umpire.Neutral(); umpire.Crouch(0.55f);
            // Brazo del guante hacia donde viene la bola.
            var local = catcher.chest.InverseTransformPoint(glove) - catcher.armL.localPosition;
            catcher.armL.localRotation = Quaternion.FromToRotation(Vector3.down, local.normalized);
            catcher.foreL.localRotation = Quaternion.identity;
        }
    }
}
