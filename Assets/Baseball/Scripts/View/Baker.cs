// Convierte composiciones (texto, logos, imágenes) en texturas: anuncios de texto,
// el escudo del estadio, los anuncios con imagen ajustados al tamaño de cada espacio.
// Arma la composición con quads y TextMeshPro lejos de la escena, la fotografía con una
// cámara ortográfica propia y devuelve un Texture2D.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Baseball.View
{
    public static class Baker
    {
        const int Layer = 31;
        static readonly Vector3 Origin = new Vector3(0, -5000, 0);
        static Camera cam;
        static Shader unlit;

        static void Ensure()
        {
            if (cam) return;
            var go = new GameObject("Baker (cámara)") { hideFlags = HideFlags.HideAndDontSave };
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 20;
            cam.transform.position = Origin + Vector3.back * 10;
            unlit = Shader.Find("Universal Render Pipeline/Unlit");
        }

        // Fotografía lo que `build` pone dentro del rectángulo de w × h unidades centrado en el origen.
        public static Texture2D Bake(int px, int py, Action<Transform, Vector2> build)
        {
            Ensure();
            float aspect = px / (float)py;
            var size = new Vector2(10 * aspect, 10);
            var root = new GameObject("Baker (composición)") { hideFlags = HideFlags.HideAndDontSave }.transform;
            root.position = Origin;
            try
            {
                build(root, size);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = Layer; t.gameObject.hideFlags = HideFlags.HideAndDontSave; }
                foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true)) tmp.ForceMeshUpdate();
                cam.orthographicSize = 5;
                cam.aspect = aspect;
                var rt = RenderTexture.GetTemporary(px, py, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.antiAliasing = 4;
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(px, py, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, hideFlags = HideFlags.DontSave };
                tex.ReadPixels(new Rect(0, 0, px, py), 0, 0);
                tex.Apply(true);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                return tex;
            }
            finally
            {
                // Inmediato: si no, la composición siguiente del mismo cuadro la vería.
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
        }

        // ---------- Piezas para componer ----------
        public static Transform Quad(Transform parent, Vector2 center, Vector2 size, Color color, Texture tex = null, float z = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, center.y, z);
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var m = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
            m.SetColor("_BaseColor", color);
            if (tex) m.SetTexture("_BaseMap", tex);
            if (color.a < 1 || tex) { m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000 + (int)(-z * 10); m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        public static Transform Disc(Transform parent, Vector2 center, float radius, Color color, float z = 0)
        {
            var go = new GameObject("Disco");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, center.y, z);
            var mesh = new Mesh();
            const int n = 72;
            var v = new Vector3[n + 1]; var t = new int[n * 3];
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2 / n; v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * radius; t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % n + 1; t[i * 3 + 2] = i + 1; }
            mesh.vertices = v; mesh.triangles = t;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var m = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
            m.SetColor("_BaseColor", color);
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        public static TextMeshPro Text(Transform parent, string text, Vector2 center, Vector2 box, Color color, FontStyles style = FontStyles.Bold, float z = -0.1f)
        {
            var go = new GameObject("Texto");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, center.y, z);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = box;
            tmp.text = text; tmp.color = color; tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 1; tmp.fontSizeMax = 200;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        // ---------- Recetas ----------
        // Anuncio de texto: fondo de color, franja y el texto lo más grande que quepa.
        public static Texture2D TextAd(string text, Color bg, Color fg, Color accent, int px, int py)
        {
            return Bake(px, py, (r, s) =>
            {
                Quad(r, Vector2.zero, s, bg);
                float band = s.y * 0.07f;
                Quad(r, new Vector2(0, -s.y / 2 + band / 2), new Vector2(s.x, band), accent, null, -0.05f);
                Quad(r, new Vector2(0, s.y / 2 - band / 2), new Vector2(s.x, band), accent, null, -0.05f);
                Text(r, text, Vector2.zero, new Vector2(s.x * 0.9f, s.y * 0.72f), fg, FontStyles.Bold | FontStyles.Italic);
            });
        }

        // Imagen ajustada al espacio sin deformarla, con el color de fondo en las orillas.
        public static Texture2D FitImage(Texture2D img, Color bg, int px, int py)
        {
            return Bake(px, py, (r, s) =>
            {
                Quad(r, Vector2.zero, s, bg);
                float ia = img.width / (float)img.height, sa = s.x / s.y;
                var size = ia > sa ? new Vector2(s.x, s.x / ia) : new Vector2(s.y * ia, s.y);
                Quad(r, Vector2.zero, size * 0.96f, Color.white, img, -0.05f);
            });
        }

        // Escudo: círculo con anillo y las iniciales del estadio.
        public static Texture2D Crest(string name, Color primary, Color secondary, int px = 512)
        {
            return Bake(px, px, (r, s) =>
            {
                Disc(r, Vector2.zero, 5f, secondary);
                Disc(r, Vector2.zero, 4.4f, primary, -0.02f);
                Disc(r, Vector2.zero, 3.9f, secondary, -0.04f);
                Disc(r, Vector2.zero, 3.65f, primary, -0.06f);
                Text(r, Initials(name), new Vector2(0, 0.3f), new Vector2(6.2f, 4f), Color.white, FontStyles.Bold | FontStyles.Italic, -0.1f);
                var t = Text(r, "BÉISBOL", new Vector2(0, -2.2f), new Vector2(3.6f, 0.8f), secondary, FontStyles.Bold, -0.1f);
                t.characterSpacing = 12;
            });
        }

        public static string Initials(string name)
        {
            var words = (name ?? "").Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            string s = "";
            foreach (var w in words) if (w.Length > 2 && char.IsLetter(w[0])) s += char.ToUpper(w[0]);
            if (s.Length == 0 && words.Length > 0) s = words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpper();
            return s.Length > 3 ? s.Substring(0, 3) : s;
        }
    }
}
