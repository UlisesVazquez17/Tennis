// Dos dirigibles que dan vueltas alrededor del estadio, cada uno con su anuncio en los
// costados. El primero lleva la cámara "Dirigible".

using UnityEngine;

namespace Baseball.View
{
    public class Blimps : MonoBehaviour
    {
        static readonly Vector3 Center = new Vector3(0, 0, 45);
        readonly Transform[] ships = new Transform[2];
        readonly Material[] ads = new Material[2];
        readonly float[] radius = { 210, 260 }, height = { 72, 88 }, speed = { 2.2f, -1.6f }, start = { 40, 220 };

        void Awake()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var skin = new Material(lit); skin.SetColor("_BaseColor", new Color(0.82f, 0.83f, 0.86f)); skin.SetFloat("_Smoothness", 0.55f);
            var dark = new Material(lit); dark.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.14f));
            var light = new Material(lit); light.EnableKeyword("_EMISSION"); light.SetColor("_EmissionColor", new Color(4, 0.3f, 0.2f));
            for (int i = 0; i < 2; i++)
            {
                var s = new GameObject($"Dirigible {i + 1}").transform;
                s.SetParent(transform, false);
                Part(PrimitiveType.Sphere, s, Vector3.zero, new Vector3(5.6f, 5.6f, 20), skin);
                Part(PrimitiveType.Cube, s, new Vector3(0, -3.1f, 1.5f), new Vector3(1.6f, 1.2f, 4.5f), dark);
                Part(PrimitiveType.Cube, s, new Vector3(0, 2.6f, -8.6f), new Vector3(0.3f, 3.2f, 3), skin);
                Part(PrimitiveType.Cube, s, new Vector3(0, -2.6f, -8.6f), new Vector3(0.3f, 3.2f, 3), skin);
                Part(PrimitiveType.Cube, s, new Vector3(0, 0, -8.6f), new Vector3(5.6f, 0.3f, 3), skin);
                Part(PrimitiveType.Sphere, s, new Vector3(0, 0, 10), Vector3.one * 0.5f, light);
                ads[i] = new Material(unlit);
                // Un anuncio por costado, legible desde afuera.
                foreach (var side in new[] { -1f, 1f })
                {
                    var q = Part(PrimitiveType.Quad, s, new Vector3(side * 2.86f, 0.2f, 0.5f), new Vector3(13, 3.3f, 1), ads[i]);
                    q.localRotation = Quaternion.Euler(0, side > 0 ? -90 : 90, 0);
                }
                ships[i] = s;
            }
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        public void SetAd(int i, Texture2D tex) => ads[i].SetTexture("_BaseMap", tex);

        public Transform Ship(int i) => ships[i];

        void Update()
        {
            for (int i = 0; i < 2; i++)
            {
                float a = (start[i] + Time.time * speed[i]) * Mathf.Deg2Rad;
                var p = Center + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius[i] + Vector3.up * (height[i] + Mathf.Sin(Time.time * 0.3f + i) * 1.5f);
                var tangent = new Vector3(Mathf.Cos(a), 0, -Mathf.Sin(a)) * Mathf.Sign(speed[i]);
                ships[i].SetPositionAndRotation(p, Quaternion.LookRotation(tangent) * Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 0.4f + i) * 2));
            }
        }
    }
}
