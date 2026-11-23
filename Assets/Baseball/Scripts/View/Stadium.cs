// Estadio nocturno con medidas reales, generado por código: pasto podado en cuadros,
// tierra del cuadro, pista de advertencia, barda (la misma curva que usa la física),
// postes de foul, gradas de dos pisos detrás del cuadro y bleachers en los jardines,
// dugouts, torres de luz con brillo, pantalla del bateador y una ciudad iluminada al fondo.
//
// También deja listos los espacios publicitarios (AdSurfaces) y los asientos del público
// (Seats); de llenarlos se encargan Ads y Crowd. Los colores salen de la identidad del
// estadio (Apply). Se construye al activarse, también en el Editor, y no se guarda.

using System.Collections.Generic;
using System.Linq;
using Baseball.Config;
using TMPro;
using UnityEngine;

namespace Baseball.View
{
    public struct Seat { public Vector3 pos; public Vector3 facing; public float angle; }

    [ExecuteAlways]
    public class Stadium : MonoBehaviour
    {
        public int seed = 7;

        [Header("Materiales realistas (opcional)")]
        [Tooltip("Con estas texturas el pasto y la tierra usan materiales PBR en lugar de colores generados.")]
        public Texture2D grassColor;
        public Texture2D grassNormal, dirtColor, dirtNormal;
        [Tooltip("Metros que cubre una repetición de cada textura.")]
        public float grassTileMeters = 2.5f, dirtTileMeters = 3.5f;
        [Range(0, 0.3f)] public float mowingContrast = 0.12f;
        public Color grassTint = new Color(0.92f, 1f, 0.9f), dirtTint = new Color(1f, 0.93f, 0.86f);
        bool Realistic => grassColor && dirtColor;
        [Tooltip("Noche: lámparas, halos y ventanas de la ciudad encendidas. De día, apagadas.")]
        public bool nightLights = true;

        // Identidad (Apply la cambia).
        Color primary = new Color(0.04f, 0.16f, 0.44f), secondary = new Color(0.95f, 0.72f, 0.02f), seatColor = new Color(0.09f, 0.21f, 0.37f);

        const float FT = (float)BallPhysics.FT;
        static readonly Vector3 Mound = new Vector3(0, 0, (float)BallPhysics.MOUND_Z);
        static readonly Vector3 Center = new Vector3(0, 0, 45);

        public readonly Dictionary<string, List<Renderer>> AdSurfaces = new Dictionary<string, List<Renderer>>();
        public readonly List<Seat> Seats = new List<Seat>();
        public Renderer Ribbon { get; private set; }
        public Transform VideoboardAnchor { get; private set; }
        public event System.Action Built;

        readonly List<Object> owned = new List<Object>();
        Transform root;
        Material padding, stripe, seatsMat, dugoutMat, facade, exterior;
        readonly List<Vector3> outV = new List<Vector3>(); readonly List<int> outT = new List<int>();
        readonly List<Transform> glows = new List<Transform>();

        void OnEnable() => Build();
        void OnDisable() => Clear();

        void Clear()
        {
            foreach (var o in owned) if (o) DestroyImmediate(o);
            owned.Clear(); glows.Clear(); AdSurfaces.Clear(); Seats.Clear();
            if (root) DestroyImmediate(root.gameObject);
        }

        T Own<T>(T o) where T : Object { o.hideFlags = HideFlags.DontSave; owned.Add(o); return o; }

        public void Apply(StadiumConfig c)
        {
            primary = c.Primary; secondary = c.Secondary; seatColor = c.Seats;
            if (!padding) return;
            padding.SetColor("_BaseColor", primary * 0.8f);
            stripe.SetColor("_BaseColor", secondary);
            dugoutMat.SetColor("_BaseColor", primary * 0.55f);
            facade.SetColor("_BaseColor", primary * 0.45f);
            seatsMat.SetTexture("_BaseMap", Own(SeatTexture()));
        }

        public void Build()
        {
            Clear();
            root = new GameObject("Estadio (generado)").transform;
            root.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            root.SetParent(transform, false);
            Random.InitState(seed);

            Material grass, clay, track;
            if (Realistic)
            {
                // Las UV del piso cubren 150 m por unidad: la repetición sale de los metros por mosaico.
                grass = Pbr(grassColor, grassNormal, 150 / grassTileMeters, grassTint, 0.12f);
                clay = Pbr(dirtColor, dirtNormal, 150 / dirtTileMeters, dirtTint, 0.08f);
                track = Pbr(dirtColor, dirtNormal, 150 / dirtTileMeters, dirtTint * 0.82f, 0.06f);
            }
            else
            {
                grass = Lit(GrassTexture(), 0.08f);
                clay = Lit(NoiseTexture(new Color(0.6f, 0.4f, 0.26f), 0.08f, 256), 0.03f); clay.mainTextureScale = new Vector2(30, 30);
                track = Lit(NoiseTexture(new Color(0.42f, 0.28f, 0.19f), 0.1f, 256), 0.02f); track.mainTextureScale = new Vector2(60, 60);
            }
            var chalk = Lit(null, 0.1f, Color.white);
            padding = Lit(null, 0.15f, primary * 0.8f);
            stripe = Lit(null, 0.3f, secondary);
            var pole = Lit(null, 0.4f, new Color(1f, 0.85f, 0.1f), new Color(0.6f, 0.5f, 0.05f));
            var eye = Lit(null, 0f, new Color(0.02f, 0.02f, 0.025f));
            var concrete = Lit(null, 0.05f, new Color(0.32f, 0.33f, 0.36f));
            var steel = Lit(null, 0.5f, new Color(0.2f, 0.21f, 0.23f));
            dugoutMat = Lit(null, 0.2f, primary * 0.55f);
            facade = Lit(null, 0.2f, primary * 0.45f);
            seatsMat = Lit(Own(SeatTexture()), 0.25f);
            exterior = Lit(Own(ExteriorTexture()), 0.2f, Color.white, Color.white);
            exterior.SetTexture("_EmissionMap", exterior.GetTexture("_BaseMap"));
            exterior.SetFloat("_Cull", 0);   // se ve de los dos lados, sin importar el orden de la malla
            outV.Clear(); outT.Clear();
            var ground = Lit(NoiseTexture(new Color(0.1f, 0.1f, 0.11f), 0.04f, 128), 0.1f); ground.mainTextureScale = new Vector2(80, 80);

            // ---------- Campo ----------
            Mesh("Estacionamiento", Disc(Vector3.zero, 900, 64, -0.05f), ground);
            // Solo dentro del estadio: de la orilla hacia afuera queda debajo de las gradas.
            Mesh("Pasto", Disc(Center, 88, 128, 0), grass);
            // Con textura real, las franjas de podado van encima como una capa casi transparente.
            if (Realistic && mowingContrast > 0) Mesh("Franjas de podado", Disc(Center, 88, 128, 0.0015f), MowingOverlay());
            // Pista de advertencia: 15 pies de tierra antes de la barda.
            var fence = new List<Vector3>();
            for (int a = -45; a <= 45; a++) fence.Add(Dir(a) * (float)BallPhysics.FenceFt(a) * FT);
            Mesh("Pista de advertencia", Band(fence, 15 * FT, 0.003f), track);
            Mesh("Tierra del cuadro", FairClip(Disc(Mound, 95 * FT, 96, 0.004f)), clay);
            Mesh("Círculo del home", Disc(Vector3.zero, 13 * FT, 48, 0.006f), clay);
            float b = 90 * FT / Mathf.Sqrt(2);
            var inner = new[] { new Vector3(0, 0, 3f), new Vector3(b - 2.2f, 0, b), new Vector3(0, 0, 2 * b - 3.4f), new Vector3(-b + 2.2f, 0, b) };
            Mesh("Pasto del cuadro", Polygon(inner, 0.008f), grass);
            Mesh("Montículo", Dome(Mound, 9 * FT, 0.25f, 32), clay);

            var lPole = Dir(-45) * (float)BallPhysics.FenceFt(-45) * FT;
            var rPole = Dir(45) * (float)BallPhysics.FenceFt(45) * FT;
            Mesh("Línea de foul (3B)", Strip(Vector3.zero, lPole, 0.08f, 0.012f), chalk);
            Mesh("Línea de foul (1B)", Strip(Vector3.zero, rPole, 0.08f, 0.012f), chalk);
            foreach (var p in new[] { new Vector3(b, 0, b), new Vector3(0, 0, 2 * b), new Vector3(-b, 0, b) })
                Box("Base", p + Vector3.up * 0.04f, new Vector3(0.38f, 0.08f, 0.38f), chalk, 45);
            float h = 8.5f / 12 * FT;
            Mesh("Home", Polygon(new[] { Vector3.zero, new Vector3(h, 0, h), new Vector3(h, 0, 2 * h), new Vector3(-h, 0, 2 * h), new Vector3(-h, 0, h) }, 0.014f), chalk);
            Box("Goma del pitcher", Mound + new Vector3(0, 0.26f, 0), new Vector3(0.61f, 0.03f, 0.15f), chalk, 0);
            BattersBoxes(chalk);

            // ---------- Barda ----------
            Mesh("Barda", Wall(fence, (float)BallPhysics.WALL_H, 0.3f), padding);
            Mesh("Barda (franja)", Wall(fence.Select(p => p + Vector3.up * ((float)BallPhysics.WALL_H - 0.12f)).ToList(), 0.12f, 0.32f), stripe);
            Box("Poste de foul (3B)", lPole + Vector3.up * 14, new Vector3(0.25f, 28, 0.25f), pole, 0);
            Box("Poste de foul (1B)", rPole + Vector3.up * 14, new Vector3(0.25f, 28, 0.25f), pole, 0);
            DistanceMarks();
            FencePanels();

            // ---------- Gradas ----------
            // Detrás del cuadro: dos pisos. En los jardines: bleachers, con un hueco al centro
            // para la pantalla del bateador.
            var infield = InfieldEdge();
            Stands("Primer piso", infield, 16, 21, 8.8f, 1.2f, 0, seatsMat, true);
            Mesh("Barda de las gradas", WallAlong(infield, 1.2f), padding);
            Stands("Segundo piso", infield, 14, 19, 12f, 11.6f, 17, seatsMat, true);
            Mesh("Fachada del segundo piso", Facade(infield, 17, 10.2f, 11.6f, 0), facade);
            Mesh("Techo del primer piso", Soffit(infield, 17, 38, 10.2f), concrete);
            Ribbon = AdRenderer("Cinta LED", Facade(infield, 16.9f, 10.35f, 11.45f, 1));

            foreach (var side in new[] { -1, 1 })
            {
                var edge = new List<Vector3>();
                for (int a = 14; a <= 45; a += 2) edge.Add(Dir(a * side) * ((float)BallPhysics.FenceFt(a * side) * FT + 1.5f));
                if (side < 0) edge.Reverse();
                Stands(side < 0 ? "Bleachers izquierdo" : "Bleachers derecho", edge, 14, 18, 8f, 1.2f, 0, seatsMat, false);
            }
            Mesh("Exterior de las gradas", Build(new List<Vector3>(outV), new List<int>(outT), ExteriorUV()), exterior);
            Box("Pantalla del bateador", Dir(0) * (400 * FT + 9) + Vector3.up * 4.5f, new Vector3(30, 9, 0.6f), eye, 0);

            Dugouts();
            HomeScreen();
            GrassLogos();
            Structures(steel, concrete);
            LightTowers(steel);
            City();
            Built?.Invoke();
        }

        // ---------- Piezas ----------
        void BattersBoxes(Material chalk)
        {
            float w = 4 * FT, l = 6 * FT, gap = 0.5f * FT + 8.5f / 12 * FT, t = 0.06f;
            foreach (float side in new[] { -1f, 1f })
            {
                float x0 = side * gap, x1 = side * (gap + w), z0 = -l / 2 + 0.2f, z1 = l / 2 + 0.2f;
                var c = new[] { new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), new Vector3(x0, 0, z1) };
                for (int i = 0; i < 4; i++) Mesh("Caja de bateo", Strip(c[i], c[(i + 1) % 4], t, 0.013f), chalk);
            }
        }

        void DistanceMarks()
        {
            foreach (var a in new[] { -45f, -22.5f, 0f, 22.5f, 45f })
            {
                float d = (float)BallPhysics.FenceFt(a);
                var go = Child($"Marca {d:0}");
                var p = Dir(a) * (d * FT - 0.17f);
                go.transform.position = p + Vector3.up * 2.55f;
                go.transform.rotation = Quaternion.LookRotation(p.normalized);
                var tmp = go.AddComponent<TextMeshPro>();
                tmp.text = $"{d:0}"; tmp.fontSize = 9; tmp.color = Color.white; tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontStyle = FontStyles.Bold; tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.rectTransform.sizeDelta = new Vector2(6, 1.2f);
            }
        }

        // Paneles publicitarios en la cara interior de la barda.
        void FencePanels()
        {
            foreach (var a in new[] { -38f, -30f, -22f, -14f, 14f, 22f, 30f, 38f })
            {
                var p = Dir(a) * ((float)BallPhysics.FenceFt(a) * FT - 0.03f);
                Register(Slots.Barda, Quad("Panel de barda", p + Vector3.up * 1.25f, Quaternion.LookRotation(p.normalized), new Vector2(11.5f, 1.6f)));
            }
        }

        void Dugouts()
        {
            var hole = Lit(null, 0, new Color(0.02f, 0.02f, 0.03f));
            foreach (var side in new[] { -1f, 1f })
            {
                // Paralelo a la línea, entre el campo y la grada, de 20 a 38 m del home.
                var along = Dir(45 * side); var outward = Dir(135 * side);
                var c = along * 29 + outward * 15.6f;
                var rot = Quaternion.LookRotation(-outward);
                Box("Dugout", c + Vector3.up * 1.0f, new Vector3(18, 2, 3.2f), dugoutMat, 0).transform.rotation = rot;
                Box("Hueco del dugout", c + Vector3.up * 0.75f - outward * 1.55f, new Vector3(16.5f, 1.1f, 0.2f), hole, 0).transform.rotation = rot;
                // Anuncio en el techo, para las cámaras altas.
                Register(Slots.Dugouts, Quad("Techo del dugout", c + Vector3.up * 2.02f, rot * Quaternion.Euler(90, 0, 0), new Vector2(17.5f, 3f)));
            }
        }

        // Pantalla en la barda de detrás de home: se ve en la cámara de transmisión.
        void HomeScreen() =>
            Register(Slots.Home, Quad("Pantalla detrás de home", new Vector3(0, 0.62f, -60 * FT + 0.05f), Quaternion.LookRotation(Vector3.back), new Vector2(13, 1.05f)));

        void GrassLogos()
        {
            foreach (var x in new[] { -10.5f, 10.5f })
                Register(Slots.Pasto, Quad("Logo en el pasto", new Vector3(x, 0.015f, -10.5f), Quaternion.Euler(90, 180, 0), new Vector2(6, 6), true));
        }

        // Videomarcador sobre los bleachers del izquierdo y espectacular sobre los del derecho.
        void Structures(Material steel, Material concrete)
        {
            {
                float a = -28;
                var dir = Dir(a);
                var c = dir * ((float)BallPhysics.FenceFt(a) * FT + 22);
                VideoboardAnchor = Child("Videomarcador").transform;
                VideoboardAnchor.SetPositionAndRotation(c + Vector3.up * 21, Quaternion.LookRotation(dir));
                Box("Marco del videomarcador", c + Vector3.up * 21 + dir * 0.6f, new Vector3(34, 19, 1), steel, a);
                foreach (var dx in new[] { -10f, 10f }) Box("Columna", c + Quaternion.Euler(0, a, 0) * new Vector3(dx, 0, 1.5f) + Vector3.up * 6, new Vector3(1.2f, 12, 1.2f), concrete, a);
            }
            {
                float a = 27;
                var dir = Dir(a);
                var c = dir * ((float)BallPhysics.FenceFt(a) * FT + 22);
                Box("Marco del espectacular", c + Vector3.up * 19 + dir * 0.5f, new Vector3(42, 14, 0.8f), steel, a);
                foreach (var dx in new[] { -14f, 0f, 14f }) Box("Columna", c + Quaternion.Euler(0, a, 0) * new Vector3(dx, 0, 1.2f) + Vector3.up * 6, new Vector3(1f, 12, 1f), concrete, a);
                Register(Slots.Espectacular, Quad("Espectacular", c + Vector3.up * 19, Quaternion.LookRotation(dir), new Vector2(40, 12.5f)));
            }
        }

        // Torres de luz detrás de las gradas, con un panel de lámparas y un halo que brilla.
        void LightTowers(Material steel)
        {
            var lamp = Lit(null, 0.2f, Color.white, nightLights ? new Color(6f, 5.8f, 5.2f) : new Color(0.15f, 0.15f, 0.14f));
            var halo = Own(new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")));
            halo.SetTexture("_BaseMap", Own(GlowTexture()));
            halo.SetColor("_BaseColor", new Color(1f, 0.97f, 0.88f, 0.55f));
            halo.SetFloat("_Surface", 1); halo.SetFloat("_Blend", 2);   // aditivo
            halo.SetOverrideTag("RenderType", "Transparent");
            halo.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); halo.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            halo.SetInt("_ZWrite", 0); halo.renderQueue = 3100; halo.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            foreach (var a in new[] { -125f, -75f, -35f, 35f, 75f, 125f })
            {
                var basePos = Center + Dir(a) * (Mathf.Abs(a) > 100 ? 75 : Mathf.Abs(a) > 50 ? 110 : 160);
                const float top = 62;
                Box("Torre de luz", basePos + Vector3.up * top / 2, new Vector3(1.4f, top, 1.4f), steel, 0);
                var bankPos = basePos + Vector3.up * (top + 3);
                var face = Quaternion.LookRotation((Center - bankPos).normalized);
                var bank = Box("Lámparas", bankPos, new Vector3(14, 7, 0.8f), steel, 0);
                bank.transform.rotation = face;
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        var l = Box("Lámpara", Vector3.zero, Vector3.one, lamp, 0);
                        l.transform.SetParent(bank.transform, false);
                        l.transform.localPosition = new Vector3((i - 2.5f) * 0.15f, (j - 1) * 0.28f, -0.6f);
                        l.transform.localScale = new Vector3(1.6f / 14, 1.6f / 7, 0.5f);
                    }
                var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
                g.name = "Halo"; g.hideFlags = HideFlags.DontSave;
                DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(root, false);
                g.transform.position = bankPos + face * Vector3.forward * 1.5f;
                g.transform.localScale = Vector3.one * 34;
                var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = halo; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glows.Add(g.transform);
                g.SetActive(nightLights);
            }
        }

        // Ciudad de noche alrededor: edificios con ventanas encendidas.
        void City()
        {
            var mats = new[] { Windows(0.25f), Windows(0.4f), Windows(0.15f) };
            for (int i = 0; i < 150; i++)
            {
                float a = Random.Range(0f, 360f), r = Random.Range(330f, 700f);
                // Que no tape la vista desde el home hacia el central.
                if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 12 && r < 450) r += 200;
                float hgt = Random.Range(18f, 40f) + (Random.value < 0.25f ? Random.Range(40f, 120f) : 0);
                float w = Random.Range(18f, 40f);
                var go = Box("Edificio", Center + Dir(a) * r + Vector3.up * hgt / 2, new Vector3(w, hgt, Random.Range(18f, 40f)), mats[i % 3], Random.Range(0f, 90f));
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var block = new MaterialPropertyBlock();
                block.SetVector("_BaseMap_ST", new Vector4(w / 12f, hgt / 12f, Random.value, Random.value));
                mr.SetPropertyBlock(block);
            }
        }

        void LateUpdate()
        {
            // Los halos siempre miran a la cámara activa.
            var cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView) cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (!cam) return;
            foreach (var g in glows) if (g) g.rotation = Quaternion.LookRotation(g.position - cam.transform.position);
        }

        // Borde interior de las gradas del cuadro: del poste izquierdo, paralelo a la línea de
        // tercera, un arco detrás del home (60 pies) y paralelo a la de primera hasta el poste derecho.
        static List<Vector3> InfieldEdge()
        {
            var pts = new List<Vector3>();
            float off = 60 * FT, pole = (float)BallPhysics.FenceFt(45) * FT + 4;
            var l = Dir(-45); var ln = Dir(-135);
            for (float d = pole; d > 0; d -= 4) pts.Add(l * d + ln * off);
            for (int a = 225; a >= 135; a -= 5) pts.Add(Dir(a) * off);
            var r = Dir(45); var rn = Dir(135);
            for (float d = 4; d <= pole; d += 4) pts.Add(r * d + rn * off);
            return pts;
        }

        // ---------- Espacios publicitarios ----------
        Renderer Quad(string name, Vector3 pos, Quaternion rot, Vector2 size, bool cutout = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name; go.hideFlags = HideFlags.DontSave;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = AdMaterial(cutout);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }

        Renderer AdRenderer(string name, Mesh mesh)
        {
            var go = Child(name);
            go.AddComponent<MeshFilter>().sharedMesh = Own(mesh);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = AdMaterial(false);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }

        // Los anuncios no dependen de la luz, como una pantalla o una lona iluminada.
        public Material AdMaterial(bool transparent)
        {
            var m = Own(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            m.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.92f));
            if (transparent)
            {
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.renderQueue = 2990; m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetColor("_BaseColor", new Color(1, 1, 1, 0.8f));
            }
            return m;
        }

        void Register(string slot, Renderer r)
        {
            if (!AdSurfaces.TryGetValue(slot, out var list)) AdSurfaces[slot] = list = new List<Renderer>();
            list.Add(r);
        }

        // ---------- Mallas ----------
        static Vector3 Dir(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0, Mathf.Cos(deg * Mathf.Deg2Rad));

        GameObject Child(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root, false);
            return go;
        }

        void Mesh(string name, Mesh mesh, Material mat)
        {
            var go = Child(name);
            Own(mesh).name = name;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, float yaw)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.hideFlags = HideFlags.DontSave;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static Mesh Build(List<Vector3> v, List<int> t, List<Vector2> uv = null)
        {
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.SetUVs(0, uv ?? v.Select(p => new Vector2(p.x / 150 + 0.5f, p.z / 150 + 0.5f)).ToList());
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh Disc(Vector3 c, float r, int n, float y)
        {
            var v = new List<Vector3> { c + Vector3.up * y }; var t = new List<int>();
            for (int i = 0; i <= n; i++) { float a = i * Mathf.PI * 2 / n; v.Add(c + new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r)); }
            for (int i = 1; i <= n; i++) { t.Add(0); t.Add(i); t.Add(i + 1); }
            return Build(v, t);
        }

        // Franja de ancho w por dentro de una polilínea que rodea el origen.
        static Mesh Band(List<Vector3> line, float w, float y)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var p in line) { v.Add(new Vector3(p.x, y, p.z)); var q = p - p.normalized * w; v.Add(new Vector3(q.x, y, q.z)); }
            for (int i = 0; i < line.Count - 1; i++) { int a = i * 2; t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            var m = Build(v, t);
            if (m.normals[0].y < 0) { t.Reverse(); m.SetTriangles(t, 0); m.RecalculateNormals(); }
            return m;
        }

        static Mesh FairClip(Mesh m)
        {
            var v = m.vertices.ToList(); var t = m.triangles;
            var keep = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                bool ok = true;
                for (int k = 0; k < 3; k++) { var p = v[t[i + k]]; if (Mathf.Abs(p.x) > p.z + 1.5f) ok = false; }
                if (ok) keep.AddRange(new[] { t[i], t[i + 1], t[i + 2] });
            }
            var clipped = Build(v, keep);
            DestroyImmediate(m);
            return clipped;
        }

        static Mesh Polygon(IEnumerable<Vector3> pts, float y)
        {
            var v = pts.Select(p => new Vector3(p.x, y, p.z)).ToList(); var t = new List<int>();
            for (int i = 1; i < v.Count - 1; i++) { t.Add(0); t.Add(i); t.Add(i + 1); }
            if (Vector3.Cross(v[1] - v[0], v[2] - v[0]).y < 0) t.Reverse();
            return Build(v, t);
        }

        static Mesh Dome(Vector3 c, float r, float h, int n)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            const int rings = 6;
            for (int j = 0; j <= rings; j++)
            {
                float rr = r * j / rings, yy = h * (1 - Mathf.Pow((float)j / rings, 2)) + 0.005f;
                for (int i = 0; i <= n; i++) { float a = i * Mathf.PI * 2 / n; v.Add(c + new Vector3(Mathf.Sin(a) * rr, yy, Mathf.Cos(a) * rr)); }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < n; i++) { int a = j * (n + 1) + i, bb = a + n + 1; t.AddRange(new[] { a, bb, a + 1, a + 1, bb, bb + 1 }); }
            return Build(v, t);
        }

        static Mesh Strip(Vector3 a, Vector3 b, float w, float y)
        {
            var side = Vector3.Cross(Vector3.up, (b - a).normalized) * (w / 2);
            var v = new List<Vector3> { a - side, a + side, b + side, b - side }.Select(p => new Vector3(p.x, y, p.z)).ToList();
            var t = new List<int> { 0, 2, 1, 0, 3, 2 };
            if (Vector3.Cross(v[2] - v[0], v[1] - v[0]).y < 0) t.Reverse();
            return Build(v, t);
        }

        static Mesh Wall(List<Vector3> line, float height, float thick)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector3 a = line[i], b = line[i + 1];
                var outA = new Vector3(a.x, 0, a.z).normalized * thick; var outB = new Vector3(b.x, 0, b.z).normalized * thick;
                var up = Vector3.up * height;
                Quad(v, t, a, b, b + up, a + up);
                Quad(v, t, b + outB, a + outA, a + outA + up, b + outB + up);
                Quad(v, t, a + up, b + up, b + outB + up, a + outA + up);
            }
            return Build(v, t);
        }

        static Vector3 Out(Vector3 p) { var n = p - Center; n.y = 0; return n.normalized; }

        // Murito vertical a lo largo del borde de las gradas, mirando al campo.
        static Mesh WallAlong(List<Vector3> edge, float height)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < edge.Count - 1; i++)
                Quad(v, t, edge[i + 1], edge[i], edge[i] + Vector3.up * height, edge[i + 1] + Vector3.up * height);
            return FaceField(Build(v, t));
        }

        // Cara vertical a cierta distancia del borde (la fachada del segundo piso). Con
        // uvLength > 0, la U crece con la longitud para que la cinta LED repita su textura.
        static Mesh Facade(List<Vector3> edge, float inset, float y0, float y1, float uvLength)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            float u = 0, hgt = y1 - y0;
            for (int i = 0; i < edge.Count - 1; i++)
            {
                var a = edge[i] + Out(edge[i]) * inset; var b = edge[i + 1] + Out(edge[i + 1]) * inset;
                float du = uvLength > 0 ? Vector3.Distance(a, b) / hgt / 16f : 1;
                int s = v.Count;
                v.Add(new Vector3(b.x, y0, b.z)); v.Add(new Vector3(a.x, y0, a.z)); v.Add(new Vector3(a.x, y1, a.z)); v.Add(new Vector3(b.x, y1, b.z));
                uv.Add(new Vector2(u + du, 0)); uv.Add(new Vector2(u, 0)); uv.Add(new Vector2(u, 1)); uv.Add(new Vector2(u + du, 1));
                t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                u += du;
            }
            return FaceField(Build(v, t, uv));
        }

        static Mesh Soffit(List<Vector3> edge, float d0, float d1, float y)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < edge.Count - 1; i++)
            {
                Vector3 a = edge[i], b = edge[i + 1];
                Quad(v, t, a + Out(a) * d0 + Vector3.up * y, b + Out(b) * d0 + Vector3.up * y, b + Out(b) * d1 + Vector3.up * y, a + Out(a) * d1 + Vector3.up * y);
            }
            var m = Build(v, t);
            if (m.normals[0].y > 0) { t.Reverse(); m.SetTriangles(t, 0); m.RecalculateNormals(); }
            return m;
        }

        // Voltea la malla si sus caras no miran hacia el campo.
        static Mesh FaceField(Mesh m)
        {
            var n = m.normals; var v = m.vertices; int toward = 0, total = 0;
            for (int i = 0; i < n.Length; i += 4) { total++; if (Vector3.Dot(n[i], -Out(v[i])) > 0) toward++; }
            if (toward < total / 2) { m.triangles = m.triangles.Reverse().ToArray(); m.RecalculateNormals(); }
            return m;
        }

        // Gradas escalonadas hacia afuera del borde. Guarda un asiento cada 0.55 m por fila.
        void Stands(string name, List<Vector3> edge, int rows, float depth, float height, float y0, float inset, Material mat, bool infield)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            float u = 0;
            for (int i = 0; i < edge.Count - 1; i++)
            {
                Vector3 na = Out(edge[i]), nb = Out(edge[i + 1]);
                Vector3 a = edge[i] + na * inset, b = edge[i + 1] + nb * inset;
                float len = Vector3.Distance(a, b), du = len / 0.55f;
                for (int r = 0; r < rows; r++)
                {
                    float d0 = depth * r / rows, d1 = depth * (r + 1) / rows;
                    float h0 = y0 + height * r / rows, h1 = y0 + height * (r + 1) / rows;
                    int s = v.Count;
                    v.Add(a + na * d0 + Vector3.up * h0); v.Add(b + nb * d0 + Vector3.up * h0);
                    v.Add(b + nb * d0 + Vector3.up * h1); v.Add(a + na * d0 + Vector3.up * h1);
                    v.Add(a + na * d1 + Vector3.up * h1); v.Add(b + nb * d1 + Vector3.up * h1);
                    uv.Add(new Vector2(u, 0)); uv.Add(new Vector2(u + du, 0)); uv.Add(new Vector2(u + du, 0.35f)); uv.Add(new Vector2(u, 0.35f));
                    uv.Add(new Vector2(u, 1)); uv.Add(new Vector2(u + du, 1));
                    t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2, s + 3, s + 5, s + 2, s + 3, s + 4, s + 5 });
                    int n = Mathf.Max(1, Mathf.FloorToInt(len / 0.55f));
                    for (int k = 0; k < n; k++)
                    {
                        float f = (k + 0.5f) / n;
                        var p = Vector3.Lerp(a + na * (d0 + d1) / 2, b + nb * (d0 + d1) / 2, f) + Vector3.up * h1;
                        Seats.Add(new Seat { pos = p, facing = -Vector3.Lerp(na, nb, f).normalized, angle = Mathf.Atan2(p.x - Center.x, p.z - Center.z) * Mathf.Rad2Deg });
                    }
                }
                // Pared de atrás, del piso hasta arriba de la grada: va en la malla del exterior.
                Quad(outV, outT, a + na * depth, b + nb * depth, b + nb * depth + Vector3.up * (y0 + height), a + na * depth + Vector3.up * (y0 + height));
                u += du;
            }
            var m = Build(v, t, uv);
            // Las huellas deben mirar hacia arriba; si quedaron al revés, se invierte todo.
            var nn = m.normals; int up = 0, total = 0;
            for (int i = 0; i + 4 < nn.Length; i += 6) { total++; if (nn[i + 4].y > 0) up++; }
            if (up < total / 2) { t.Reverse(); m.SetTriangles(t, 0); m.RecalculateNormals(); }
            Mesh(name, m, mat);
        }

        // UV del exterior: U a lo largo (cada 12 m) y V con la altura (cada 12 m).
        List<Vector2> ExteriorUV()
        {
            var uv = new List<Vector2>();
            for (int i = 0; i < outV.Count; i += 4)
            {
                float w = Vector3.Distance(outV[i], outV[i + 1]) / 12f, h = (outV[i + 2].y - outV[i + 1].y) / 12f;
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(w, 0)); uv.Add(new Vector2(w, h)); uv.Add(new Vector2(0, h));
            }
            return uv;
        }

        // Fachada exterior: concreto con franjas de luz de los pasillos.
        Texture2D ExteriorTexture()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    bool band = y % 32 > 27;
                    bool column = x % 32 < 3;
                    px[y * S + x] = band ? new Color(0.9f, 0.8f, 0.55f) * 0.9f : column ? new Color(0.06f, 0.06f, 0.07f) : new Color(0.13f, 0.14f, 0.16f);
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
        }

        // ---------- Materiales y texturas ----------
        Material Pbr(Texture2D color, Texture2D normal, float tiling, Color tint, float smooth)
        {
            var m = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
            m.SetTexture("_BaseMap", color);
            m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            m.SetColor("_BaseColor", tint);
            if (normal)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        // Cuadros de podado de 5 m: claros y oscuros alternados sobre el pasto real.
        Material MowingOverlay()
        {
            const int S = 512;
            var tex = Own(new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 });
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float wx = (x / (float)S - 0.5f) * 150, wz = (y / (float)S - 0.5f) * 150;
                    bool light = (Mathf.FloorToInt(wx / 5f) + Mathf.FloorToInt(wz / 5f)) % 2 == 0;
                    px[y * S + x] = light ? new Color(1, 1, 0.85f, mowingContrast) : new Color(0, 0.05f, 0, mowingContrast * 0.8f);
                }
            tex.SetPixels(px); tex.Apply(true);
            var m = Own(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0); m.renderQueue = 2950; m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return m;
        }

        Material Lit(Texture2D tex, float smooth, Color? color = null, Color? emission = null)
        {
            var m = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color ?? Color.white);
            m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            return m;
        }

        Texture2D GrassTexture()
        {
            // Cuadros de podado de 5 m en la textura que cubre el disco de 300 m.
            const int S = 1024;
            Color a = new Color(0.17f, 0.4f, 0.14f), b = new Color(0.21f, 0.47f, 0.17f);
            var tex = Own(new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 });
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float wx = (x / (float)S - 0.5f) * 150, wz = (y / (float)S - 0.5f) * 150;
                    bool band = (Mathf.FloorToInt(wx / 5f) + Mathf.FloorToInt(wz / 5f)) % 2 == 0;
                    var c = band ? a : b;
                    float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.05f - 0.025f;
                    px[y * S + x] = new Color(c.r + n, c.g + n, c.b + n);
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        Texture2D NoiseTexture(Color c, float amount, int S)
        {
            var tex = Own(new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat });
            var px = new Color[S * S];
            for (int i = 0; i < px.Length; i++)
            {
                float n = (Random.value - 0.5f) * amount + (Mathf.PerlinNoise(i % S * 0.05f, i / S * 0.05f) - 0.5f) * amount;
                px[i] = new Color(c.r + n, c.g + n * 0.8f, c.b + n * 0.6f);
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        // Escalón: concreto en la contrahuella y el asiento del color elegido en la huella.
        Texture2D SeatTexture()
        {
            const int W = 32, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool riser = y < H * 0.35f;
                    var c = riser ? new Color(0.22f, 0.22f, 0.24f) : (y > H * 0.82f ? new Color(0.3f, 0.3f, 0.32f) : seatColor);
                    if (!riser && x < 3) c *= 0.6f;
                    px[y * W + x] = c;
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        Material Windows(float lit)
        {
            const int S = 64;
            var tex = Own(new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat });
            var em = Own(new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat });
            var px = new Color[S * S]; var ex = new Color[S * S];
            for (int wy = 0; wy < 8; wy++)
                for (int wx = 0; wx < 8; wx++)
                {
                    bool on = Random.value < lit;
                    var glow = on ? Color.Lerp(new Color(1f, 0.82f, 0.5f), new Color(0.7f, 0.85f, 1f), Random.value) : Color.black;
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                        {
                            int i = (wy * 8 + y) * S + wx * 8 + x;
                            bool window = x > 1 && x < 7 && y > 1 && y < 6;
                            px[i] = window ? new Color(0.05f, 0.06f, 0.08f) : new Color(0.08f, 0.08f, 0.1f);
                            ex[i] = window ? glow * 1.4f : Color.black;
                        }
                }
            tex.SetPixels(px); tex.Apply(true); em.SetPixels(ex); em.Apply(true);
            var m = Lit(tex, 0.3f, nightLights ? Color.white : new Color(1.6f, 1.6f, 1.7f), nightLights ? Color.white : Color.black);
            m.SetTexture("_EmissionMap", em);
            return m;
        }

        static Texture2D GlowTexture()
        {
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                    float a = Mathf.Clamp01(1 - d); a = a * a * a;
                    px[y * S + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }
    }
}
