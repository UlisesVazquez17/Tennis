// Interfaz del kiosco (1080 × 1920, vertical): marcador de transmisión, lanzamiento,
// panel tipo Statcast, avisos, pantalla de espera con el QR y resultado del derby.
// Se arma por código para que la escena solo necesite este componente.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Baseball.View
{
    public enum KioskView { Idle, Connected, Calib, Play, Lost, Paused }

    public class Hud : MonoBehaviour
    {
        static readonly Color Navy = new Color(0.04f, 0.09f, 0.2f, 0.92f);
        static readonly Color Red = new Color(0.8f, 0.1f, 0.14f, 1f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.25f, 1f);

        RectTransform canvas;
        GameObject scoreBar, chip, statcast, toastBox, result, offline;
        readonly Dictionary<KioskView, GameObject> views = new Dictionary<KioskView, GameObject>();
        TMP_Text hrN, pitchN, totalFt, outsN, chipName, chipMph, scEv, scLa, scDist, scHang, toastBig, toastSmall;
        TMP_Text code, lostSecs, resultEyebrow, resultTitle, resultTotal, resultLong, resultEv, resultRank, resultSecs, hint;
        RawImage qr, body;
        Image bodyFrame;
        TMP_Text bodyInfo;
        RectTransform barTop, barBottom, distTag; TMP_Text distText, distLabel;
        float cine, cineTarget;
        public bool Cinematic => cineTarget > 0;
        GameObject outsCell;
        float toastUntil;

        void Awake() => BuildUi();

        // Modo repetición: franjas negras arriba y abajo, sin marcador ni recuadro de cámara.
        public void SetCinematic(bool on)
        {
            cineTarget = on ? 1 : 0;
            if (on) { scoreBar.SetActive(false); chip.SetActive(false); statcast.SetActive(false); bodyFrame.gameObject.SetActive(false); }
            else { scoreBar.SetActive(true); DistanceTag(false, Vector2.zero, ""); }
        }

        // Distancia junto a la bola (posición en pantalla, en píxeles).
        public void DistanceTag(bool show, Vector2 screen, string text, bool final = false)
        {
            distTag.gameObject.SetActive(show);
            if (!show) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var local);
            distTag.anchoredPosition = local + new Vector2(0, final ? 120 : 70);
            distText.text = text;
            distText.fontSize = final ? 120 : 64;
            distLabel.gameObject.SetActive(final);
            distTag.localScale = Vector3.one * (final ? 1 + 0.06f * Mathf.Sin(Time.unscaledTime * 8) : 1);
        }

        void Update()
        {
            cine = Mathf.MoveTowards(cine, cineTarget, Time.unscaledDeltaTime * 3);
            barTop.sizeDelta = new Vector2(0, 190 * cine); barBottom.sizeDelta = new Vector2(0, 190 * cine);
            if (toastBox.activeSelf && Time.unscaledTime > toastUntil) toastBox.SetActive(false);
        }

        // ---------- API ----------
        public void Show(KioskView v)
        {
            foreach (var kv in views) kv.Value.SetActive(kv.Key == v);
            bool live = v == KioskView.Play;
            scoreBar.SetActive(live || v == KioskView.Lost || v == KioskView.Paused);
            if (!live) { chip.SetActive(false); statcast.SetActive(false); }
        }

        public void Offline(bool on) => offline.SetActive(on);

        public void Room(string sala, Texture tex) { code.text = sala; if (tex) qr.texture = tex; qr.color = tex ? Color.white : new Color(1, 1, 1, 0.1f); }

        public void SetDerby(Derby d, bool over)
        {
            hrN.text = d.homeRuns.ToString();
            pitchN.text = $"{Mathf.Min(d.pitch + (over ? 0 : 1), d.pitchesMax)}/{d.pitchesMax}";
            totalFt.text = $"{d.totalFt}";
            outsCell.SetActive(d.outsMax != int.MaxValue);
            outsN.text = $"{d.outs}/{d.outsMax}";
        }

        public void SetPitch(string name, double mph) { chip.SetActive(true); chipName.text = name.ToUpper(); chipMph.text = $"{mph:0} MPH"; statcast.SetActive(false); }
        public void HidePitch() => chip.SetActive(false);

        public void Statcast(double ev, double la, int dist, double hang)
        {
            statcast.SetActive(true);
            scEv.text = $"{ev:0.0}"; scLa.text = $"{la:0}°"; scDist.text = $"{dist}"; scHang.text = $"{hang:0.0}s";
        }

        public void Toast(string big, string small = "", bool warn = false, float secs = 1.5f)
        {
            toastBig.text = big; toastSmall.text = small; toastSmall.gameObject.SetActive(!string.IsNullOrEmpty(small));
            toastBox.GetComponent<Image>().color = warn ? new Color(0.45f, 0.07f, 0.08f, 0.9f) : Navy;
            toastBox.SetActive(true);
            toastUntil = Time.unscaledTime + secs;
        }

        public void Hint(string text) => hint.text = text;

        // Vista de la cámara del cuerpo, como espejo, con borde verde cuando detecta a la persona.
        public void Body(Texture cam, bool tracking, bool show, string info = "")
        {
            bool on = show && cam && !Cinematic;
            bodyFrame.gameObject.SetActive(on);
            if (!on) return;
            bodyInfo.text = info;
            body.texture = cam;
            body.uvRect = new Rect(1, 0, -1, 1);
            bodyFrame.color = tracking ? new Color(0.2f, 0.9f, 0.35f, 0.95f) : new Color(0.9f, 0.25f, 0.2f, 0.95f);
        }
        public void LostSecs(int s) => lostSecs.text = $"{s}";

        public void Result(Derby d, string reason)
        {
            result.SetActive(true);
            resultEyebrow.text = reason == "inactividad" ? "TERMINADO POR INACTIVIDAD" : "FIN DEL DERBY";
            resultTitle.text = d.homeRuns == 1 ? "1 jonrón" : $"{d.homeRuns} jonrones";
            resultTotal.text = $"{d.totalFt} ft"; resultLong.text = $"{d.longestFt} ft"; resultEv.text = $"{d.maxEvMph:0} mph";
            resultRank.text = "Escribe tu nombre en el teléfono para entrar al ranking del día";
            chip.SetActive(false);
        }
        public void ResultRank(string text) => resultRank.text = text;
        public void ResultSecs(int s) => resultSecs.text = $"{s}";
        public void HideResult() => result.SetActive(false);
        public bool ResultShown => result.activeSelf;

        // ---------- Construcción ----------
        void BuildUi()
        {
            var go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 10;
            var s = go.GetComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1080, 1920); s.matchWidthOrHeight = 0.5f;
            canvas = (RectTransform)go.transform;

            // Marcador arriba: logo, jonrones, lanzamiento, pies.
            scoreBar = Panel("Marcador", canvas, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -60), new Vector2(-80, 150), Navy);
            var title = Text(scoreBar.transform, "HOME RUN\nDERBY", 40, Gold, TextAlignmentOptions.Left, FontStyles.Bold | FontStyles.Italic);
            Place(title.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(165, 0), new Vector2(270, 0));
            title.lineSpacing = -20;
            hrN = Cell(scoreBar.transform, "JONRONES", 0.42f);
            pitchN = Cell(scoreBar.transform, "LANZAMIENTO", 0.62f);
            totalFt = Cell(scoreBar.transform, "PIES", 0.82f);
            outsN = Cell(scoreBar.transform, "OUTS", 0.95f); outsCell = outsN.transform.parent.gameObject;
            var stripe = Panel("Franja", scoreBar.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 3), new Vector2(0, 6), Red);

            // Lanzamiento en curso.
            chip = Panel("Lanzamiento", canvas, new Vector2(0, 1), new Vector2(0, 1), new Vector2(240, -290), new Vector2(400, 110), Navy);
            chipName = Text(chip.transform, "", 40, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(chipName.rectTransform, new Vector2(0, 0.45f), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            chipMph = Text(chip.transform, "", 30, Gold, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(chipMph.rectTransform, new Vector2(0, 0), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);

            // Panel Statcast.
            statcast = Panel("Statcast", canvas, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 260), new Vector2(980, 200), Navy);
            var sct = Text(statcast.transform, "STATCAST", 24, Gold, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(sct.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -24), new Vector2(0, 40));
            scEv = Stat(statcast.transform, "VEL. SALIDA (MPH)", 0.125f);
            scLa = Stat(statcast.transform, "ÁNGULO", 0.375f);
            scDist = Stat(statcast.transform, "DISTANCIA (FT)", 0.625f);
            scHang = Stat(statcast.transform, "EN EL AIRE", 0.875f);

            barTop = (RectTransform)Panel("Franja superior", canvas, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero, Color.black).transform;
            barTop.pivot = new Vector2(0.5f, 1); barTop.anchoredPosition = Vector2.zero;
            barBottom = (RectTransform)Panel("Franja inferior", canvas, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero, Color.black).transform;
            barBottom.pivot = new Vector2(0.5f, 0); barBottom.anchoredPosition = Vector2.zero;
            distTag = (RectTransform)new GameObject("Distancia en vivo", typeof(RectTransform)).transform;
            distTag.SetParent(canvas, false);
            distTag.anchorMin = distTag.anchorMax = new Vector2(0.5f, 0.5f); distTag.sizeDelta = new Vector2(700, 200);
            distText = Text(distTag, "", 64, Color.white, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
            distText.outlineWidth = 0.22f; distText.outlineColor = new Color32(5, 15, 40, 255);
            distLabel = Text(distTag, "PIES", 36, Gold, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(distLabel.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -28), new Vector2(0, 50));
            distLabel.outlineWidth = 0.2f; distLabel.outlineColor = new Color32(5, 15, 40, 255);
            distTag.gameObject.SetActive(false);

            bodyFrame = Panel("Cámara del cuerpo", canvas, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-200, -330), new Vector2(330, 196), Color.green).GetComponent<Image>();
            body = new GameObject("Imagen", typeof(RawImage)).GetComponent<RawImage>();
            body.transform.SetParent(bodyFrame.transform, false);
            Place(body.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-10, -10));
            bodyInfo = Text(bodyFrame.transform, "", 22, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(bodyInfo.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -22), new Vector2(0, 34));
            bodyFrame.gameObject.SetActive(false);

            hint = Text(canvas, "", 34, new Color(1, 1, 1, 0.85f), TextAlignmentOptions.Center, FontStyles.Normal);
            Place(hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 110), new Vector2(-80, 60));

            // Aviso grande al centro.
            toastBox = Panel("Aviso", canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -330), new Vector2(860, 230), Navy);
            toastBig = Text(toastBox.transform, "", 82, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(toastBig.rectTransform, new Vector2(0, 0.38f), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            toastSmall = Text(toastBox.transform, "", 38, Gold, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(toastSmall.rectTransform, new Vector2(0, 0), new Vector2(1, 0.42f), Vector2.zero, Vector2.zero);
            toastBox.SetActive(false);

            // Vistas del kiosco.
            var idle = Panel("Espera", canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            // Título arriba y el QR abajo, para que la demo se vea en medio.
            var big = Text(idle.transform, "HOME RUN\nDERBY", 150, Color.white, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
            big.outlineWidth = 0.18f; big.outlineColor = new Color32(5, 15, 40, 255);
            Place(big.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -230), new Vector2(-60, 330));
            var card = Panel("Tarjeta", idle.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 360), new Vector2(940, 560), Navy);
            var qrBg = Panel("QR fondo", card.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(270, 0), new Vector2(460, 460), Color.white);
            qr = new GameObject("QR", typeof(RawImage)).GetComponent<RawImage>();
            qr.transform.SetParent(qrBg.transform, false);
            Place(qr.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-24, -24));
            var scan = Text(card.transform, "Escanea con tu teléfono y batea", 50, Gold, TextAlignmentOptions.Left, FontStyles.Bold);
            Place(scan.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-210, 80), new Vector2(380, 200));
            code = Text(card.transform, "····", 64, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
            Place(code.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-210, -90), new Vector2(380, 80));
            views[KioskView.Idle] = idle;
            views[KioskView.Connected] = Banner("Conectado", "¡Teléfono conectado!", "Sigue los pasos en tu teléfono");
            views[KioskView.Calib] = Banner("Calibrar", "Toma el teléfono como un bate", "Apunta al frente y toca Calibrar");
            views[KioskView.Play] = Panel("Juego", canvas, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            var lost = Banner("Perdido", "Se perdió la conexión del teléfono", "Esperando…");
            lostSecs = lost.GetComponentsInChildren<TMP_Text>()[1];
            views[KioskView.Lost] = lost;
            views[KioskView.Paused] = Banner("Pausa", "Pausa", "Vuelve a la página del bate en tu teléfono");

            // Resultado.
            result = Panel("Resultado", canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(940, 900), Navy);
            resultEyebrow = Text(result.transform, "", 34, Gold, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(resultEyebrow.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -70), new Vector2(0, 60));
            resultTitle = Text(result.transform, "", 120, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(resultTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -200), new Vector2(0, 160));
            resultTotal = Stat(result.transform, "DISTANCIA TOTAL", 0.2f, 470);
            resultLong = Stat(result.transform, "MÁS LARGO", 0.5f, 470);
            resultEv = Stat(result.transform, "VEL. MÁXIMA", 0.8f, 470);
            resultRank = Text(result.transform, "", 36, Color.white, TextAlignmentOptions.Center, FontStyles.Normal);
            Place(resultRank.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 200), new Vector2(-80, 120));
            resultSecs = Text(result.transform, "", 30, new Color(1, 1, 1, 0.6f), TextAlignmentOptions.Center, FontStyles.Normal);
            Place(resultSecs.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 70), new Vector2(0, 50));
            result.SetActive(false);

            offline = Panel("Sin servidor", canvas, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 30), new Vector2(0, 60), new Color(0.45f, 0.07f, 0.08f, 0.9f));
            var off = Text(offline.transform, "Sin conexión con el servidor del kiosco", 28, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(off.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Show(KioskView.Idle);
            chip.SetActive(false); statcast.SetActive(false);
        }

        GameObject Banner(string name, string big, string small)
        {
            var p = Panel(name, canvas, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 360), new Vector2(940, 300), Navy);
            var a = Text(p.transform, big, 60, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(a.rectTransform, new Vector2(0, 0.45f), new Vector2(1, 1), Vector2.zero, new Vector2(-40, 0));
            var b = Text(p.transform, small, 38, Gold, TextAlignmentOptions.Center, FontStyles.Normal);
            Place(b.rectTransform, new Vector2(0, 0), new Vector2(1, 0.5f), Vector2.zero, new Vector2(-40, 0));
            return p;
        }

        TMP_Text Cell(Transform parent, string label, float x)
        {
            var cell = new GameObject(label, typeof(RectTransform));
            cell.transform.SetParent(parent, false);
            Place((RectTransform)cell.transform, new Vector2(x, 0), new Vector2(x, 1), Vector2.zero, new Vector2(190, 0));
            var l = Text(cell.transform, label, 20, new Color(1, 1, 1, 0.65f), TextAlignmentOptions.Center, FontStyles.Bold);
            Place(l.rectTransform, new Vector2(0, 0.62f), new Vector2(1, 0.95f), Vector2.zero, Vector2.zero);
            var v = Text(cell.transform, "0", 62, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(v.rectTransform, new Vector2(0, 0.05f), new Vector2(1, 0.7f), Vector2.zero, Vector2.zero);
            return v;
        }

        TMP_Text Stat(Transform parent, string label, float x, float y = 0)
        {
            var cell = new GameObject(label, typeof(RectTransform));
            cell.transform.SetParent(parent, false);
            var rt = (RectTransform)cell.transform;
            if (y > 0) Place(rt, new Vector2(x, 1), new Vector2(x, 1), new Vector2(0, -y), new Vector2(280, 160));
            else Place(rt, new Vector2(x, 0), new Vector2(x, 0.8f), Vector2.zero, new Vector2(240, 0));
            var v = Text(cell.transform, "—", 64, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(v.rectTransform, new Vector2(0, 0.35f), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            var l = Text(cell.transform, label, 20, new Color(1, 1, 1, 0.65f), TextAlignmentOptions.Center, FontStyles.Bold);
            Place(l.rectTransform, new Vector2(0, 0), new Vector2(1, 0.35f), Vector2.zero, Vector2.zero);
            return v;
        }

        static GameObject Panel(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, aMin, aMax, pos, size);
            var img = go.GetComponent<Image>(); img.color = color; img.raycastTarget = false;
            return go;
        }

        static TMP_Text Text(Transform parent, string text, float size, Color color, TextAlignmentOptions align, FontStyles style)
        {
            var go = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style;
            t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.Normal;
            Place(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return t;
        }

        static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }
    }
}
