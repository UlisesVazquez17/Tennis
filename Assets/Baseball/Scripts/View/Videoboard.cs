// Videomarcador del jardín izquierdo: escudo y nombre del estadio, el marcador del derby
// lanzamiento por lanzamiento (como las entradas de un partido), la línea del lanzamiento
// en curso y un anuncio rotativo que se desliza. Los jonrones se celebran en grande.

using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Baseball.View
{
    public class Videoboard : MonoBehaviour
    {
        const int Columns = 10;
        Transform root;
        Material bgMat, crestMat, adMat, adOldMat, flashMat;
        Transform adQuad, adOldQuad, flashQuad;
        TextMeshPro title, status, flashBig, flashSmall;
        readonly List<TextMeshPro> heads = new List<TextMeshPro>(), cells = new List<TextMeshPro>();
        TextMeshPro hrCell, ftCell;
        Color primary, secondary;
        float slideT = 1, flashUntil;

        public void Attach(Transform anchor)
        {
            root = transform;
            root.SetParent(anchor, false);
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            bgMat = new Material(unlit); bgMat.SetColor("_BaseColor", new Color(0.02f, 0.03f, 0.06f));
            Quad("Fondo", new Vector3(0, 0, 0.05f), new Vector2(32, 17.4f), bgMat);

            crestMat = Transparent(unlit);
            Quad("Escudo", new Vector3(-13.2f, 5.6f, 0), new Vector2(4.6f, 4.6f), crestMat);
            title = Text("Nombre", new Vector3(1.6f, 6.6f, 0), new Vector2(24, 2.6f), 10, TextAlignmentOptions.Left);
            status = Text("Estado", new Vector3(1.6f, 4.6f, 0), new Vector2(24, 1.6f), 5, TextAlignmentOptions.Left);
            status.color = new Color(0.8f, 0.85f, 0.95f);

            // Marcador: LANZ 1…10 | JR | PIES
            float x0 = -12.6f, dx = 2.05f;
            Text("Etiqueta", new Vector3(x0 - 1.7f, 2.2f, 0), new Vector2(3, 1.2f), 4.5f, TextAlignmentOptions.Center).text = "LANZ";
            Text("Etiqueta", new Vector3(x0 - 1.7f, 0.7f, 0), new Vector2(3, 1.4f), 5, TextAlignmentOptions.Center).text = "TÚ";
            for (int i = 0; i < Columns; i++)
            {
                var h = Text("Columna", new Vector3(x0 + 1 + i * dx, 2.2f, 0), new Vector2(dx, 1.2f), 4.5f, TextAlignmentOptions.Center);
                h.text = (i + 1).ToString(); heads.Add(h);
                cells.Add(Text("Celda", new Vector3(x0 + 1 + i * dx, 0.7f, 0), new Vector2(dx, 1.4f), 5.5f, TextAlignmentOptions.Center));
            }
            Text("Etiqueta", new Vector3(10.6f, 2.2f, 0), new Vector2(2.4f, 1.2f), 4.5f, TextAlignmentOptions.Center).text = "JR";
            Text("Etiqueta", new Vector3(13.6f, 2.2f, 0), new Vector2(3.4f, 1.2f), 4.5f, TextAlignmentOptions.Center).text = "PIES";
            hrCell = Text("JR", new Vector3(10.6f, 0.7f, 0), new Vector2(2.4f, 1.4f), 6.5f, TextAlignmentOptions.Center);
            ftCell = Text("Pies", new Vector3(13.6f, 0.7f, 0), new Vector2(3.4f, 1.4f), 6.5f, TextAlignmentOptions.Center);

            // Anuncio rotativo (dos quads para la transición).
            adMat = new Material(unlit); adOldMat = new Material(unlit);
            adQuad = Quad("Anuncio", new Vector3(0, -4.4f, 0), new Vector2(30.4f, 7.7f), adMat);
            adOldQuad = Quad("Anuncio anterior", new Vector3(0, -4.4f, 0.01f), new Vector2(30.4f, 7.7f), adOldMat);

            // Celebración encima del anuncio.
            flashMat = new Material(unlit);
            flashQuad = Quad("Celebración", new Vector3(0, -4.4f, -0.02f), new Vector2(30.4f, 7.7f), flashMat);
            flashBig = Text("Celebración", new Vector3(0, -3.6f, -0.05f), new Vector2(30, 4.5f), 22, TextAlignmentOptions.Center);
            flashBig.fontStyle = FontStyles.Bold | FontStyles.Italic;
            flashSmall = Text("Celebración", new Vector3(0, -6.9f, -0.05f), new Vector2(30, 1.8f), 7, TextAlignmentOptions.Center);
            SetFlash(false);
            Clear();
        }

        public void SetIdentity(string name, Texture2D crest, Color primary, Color secondary)
        {
            this.primary = primary;
            // En la pantalla oscura, un secundario muy oscuro no se leería: se usa uno aclarado.
            float luma = 0.3f * secondary.r + 0.59f * secondary.g + 0.11f * secondary.b;
            this.secondary = luma < 0.35f ? Color.Lerp(secondary, Color.white, 0.75f) : secondary;
            secondary = this.secondary;
            title.text = name.ToUpper();
            title.color = Color.white;
            crestMat.SetTexture("_BaseMap", crest);
            bgMat.SetColor("_BaseColor", Color.Lerp(new Color(0.02f, 0.03f, 0.06f), primary, 0.25f));
            foreach (var h in heads) h.color = secondary;
            flashMat.SetColor("_BaseColor", primary);
            flashSmall.color = secondary;
        }

        public void SetAd(Texture2D tex)
        {
            if (adMat.GetTexture("_BaseMap") == tex) return;
            adOldMat.SetTexture("_BaseMap", adMat.GetTexture("_BaseMap"));
            adMat.SetTexture("_BaseMap", tex);
            slideT = 0;
        }

        public void Status(string text) => status.text = text;

        public void Clear()
        {
            foreach (var c in cells) { c.text = "–"; c.color = new Color(1, 1, 1, 0.35f); }
            hrCell.text = "0"; ftCell.text = "0";
            status.text = "ESCANEA EL QR Y BATEA";
        }

        public void SetDerby(Derby d)
        {
            for (int i = 0; i < Columns; i++)
            {
                var c = cells[i];
                if (i >= d.log.Count) { c.text = i < d.pitchesMax ? "–" : ""; c.color = new Color(1, 1, 1, 0.35f); continue; }
                var (r, ft) = d.log[i];
                switch (r)
                {
                    case PitchResult.HomeRun: c.text = "JR"; c.color = secondary; break;
                    case PitchResult.Foul: c.text = "F"; c.color = Color.white; break;
                    case PitchResult.Whiff: case PitchResult.Strike: c.text = "K"; c.color = new Color(1f, 0.45f, 0.4f); break;
                    default: c.text = "•"; c.color = Color.white; break;
                }
            }
            for (int i = 0; i < Columns; i++) heads[i].color = i == d.log.Count && !d.over ? Color.white : secondary;
            hrCell.text = d.homeRuns.ToString(); hrCell.color = secondary;
            ftCell.text = d.totalFt.ToString(); ftCell.color = Color.white;
        }

        public void Flash(string big, string small, float seconds = 4)
        {
            flashBig.text = big; flashSmall.text = small;
            flashUntil = Time.time + seconds;
            SetFlash(true);
        }

        void SetFlash(bool on) { flashQuad.gameObject.SetActive(on); flashBig.gameObject.SetActive(on); flashSmall.gameObject.SetActive(on); }

        void Update()
        {
            if (slideT < 1)
            {
                // El anuncio nuevo se despliega de arriba abajo sobre el anterior, como una persiana.
                slideT = Mathf.Min(1, slideT + Time.deltaTime / 0.7f);
                float k = Mathf.SmoothStep(0, 1, slideT);
                adQuad.localScale = new Vector3(30.4f, 7.7f * k, 1);
                adQuad.localPosition = new Vector3(0, -4.4f + 7.7f * (1 - k) / 2, 0);
                adOldQuad.gameObject.SetActive(slideT < 1);
            }
            if (flashQuad.gameObject.activeSelf)
            {
                if (Time.time > flashUntil) SetFlash(false);
                else
                {
                    float p = Mathf.PingPong(Time.time * 3, 1);
                    flashMat.SetColor("_BaseColor", Color.Lerp(primary, secondary * 0.6f, p * 0.5f));
                    flashBig.color = Color.Lerp(Color.white, secondary, p);
                    flashBig.transform.localScale = Vector3.one * (1 + 0.05f * Mathf.Sin(Time.time * 10));
                }
            }
        }

        Transform Quad(string name, Vector3 pos, Vector2 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        TextMeshPro Text(string name, Vector3 pos, Vector2 box, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos + new Vector3(0, 0, -0.03f);
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = box;
            t.fontSize = size; t.alignment = align; t.fontStyle = FontStyles.Bold;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            t.color = Color.white;
            return t;
        }

        static Material Transparent(Shader unlit)
        {
            var m = new Material(unlit);
            m.SetFloat("_Surface", 1); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0); m.renderQueue = 3000; m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return m;
        }
    }
}
