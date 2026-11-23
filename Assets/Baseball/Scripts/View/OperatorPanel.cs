// Panel del operador: nombre y colores del estadio, cámara, los nueve espacios
// publicitarios, la ola y guardar o cargar la configuración como archivo.
//
// Se abre con F1, o manteniendo el dedo 2 s en la esquina superior derecha de la pantalla
// (para el tótem sin teclado). Esc o "Cerrar" lo cierran.

using System;
using System.Collections.Generic;
using Baseball.Config;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Baseball.View
{
    public class OperatorPanel : MonoBehaviour
    {
        public Branding branding;
        public BroadcastCamera cam;
        public Crowd crowd;

        static readonly Color Bg = new Color(0.05f, 0.07f, 0.12f, 0.97f), Section = new Color(0.1f, 0.13f, 0.2f, 1);
        static readonly Color Btn = new Color(0.17f, 0.22f, 0.33f), BtnOn = new Color(0.95f, 0.72f, 0.1f), Text = new Color(0.92f, 0.94f, 0.98f);
        static readonly string[] Palette = { "#0B2A6F", "#A30D1F", "#0E6B3A", "#F2B705", "#E35205", "#5B2A86", "#111111", "#FFFFFF", "#16365E", "#7A7A7A", "#00A3E0", "#C8102E" };

        GameObject panel;
        TMP_InputField nameField;
        readonly Dictionary<string, TMP_InputField> hex = new Dictionary<string, TMP_InputField>();
        readonly Dictionary<CamMode, Image> camButtons = new Dictionary<CamMode, Image>();
        readonly Dictionary<string, (RawImage preview, TMP_Text info, TMP_InputField input)> slotRows = new Dictionary<string, (RawImage, TMP_Text, TMP_InputField)>();
        TMP_Text status;
        float cornerHeld;

        public bool Open => panel && panel.activeSelf;

        void Start()
        {
            if (!FindAnyObjectByType<EventSystem>())
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Build();
            panel.SetActive(false);
            branding.Changed += _ => Refresh();
            var mode = Enum.TryParse(branding.Config.camera, out CamMode m) ? m : CamMode.Auto;
            cam.SetMode(mode);
            Refresh();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) Toggle();
            if (kb != null && kb.escapeKey.wasPressedThisFrame && Open) Toggle();
            // Mantener presionada la esquina superior derecha.
            var p = Pointer.current;
            bool corner = p != null && p.press.isPressed && p.position.ReadValue().x > Screen.width * 0.9f && p.position.ReadValue().y > Screen.height * 0.94f;
            cornerHeld = corner ? cornerHeld + Time.unscaledDeltaTime : 0;
            if (cornerHeld > 2) { cornerHeld = -10; Toggle(); }
        }

        public void Toggle() { panel.SetActive(!panel.activeSelf); if (Open) Refresh(); }

        void Say(string text) => status.text = text;

        void Changed(string message = "Guardado")
        {
            branding.Apply();
            Say(message);
        }

        // ---------- Construcción ----------
        void Build()
        {
            var canvasGo = new GameObject("Panel del operador", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = 0.5f;

            panel = Box(canvasGo.transform, "Panel", Bg);
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(1020, 1820);

            // Encabezado fijo.
            var header = Row(panel.transform, 90);
            Place((RectTransform)header.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -50), new Vector2(-40, 90));
            Label(header.transform, "PANEL DEL OPERADOR", 40, BtnOn, 700);
            Button(header.transform, "Cerrar", Toggle, 200);

            status = Label(panel.transform, "", 26, Text, 0);
            Place(status.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 30), new Vector2(-40, 50));
            status.alignment = TextAlignmentOptions.Center;

            // Contenido desplazable.
            var scrollGo = new GameObject("Desplazable", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D), typeof(Image));
            scrollGo.transform.SetParent(panel.transform, false);
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            Place((RectTransform)scrollGo.transform, Vector2.zero, Vector2.one, new Vector2(0, -15), new Vector2(-30, -200));
            var content = new GameObject("Contenido", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(scrollGo.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1); crt.sizeDelta = Vector2.zero;
            var vl = content.GetComponent<VerticalLayoutGroup>(); vl.spacing = 18; vl.padding = new RectOffset(10, 10, 10, 30);
            vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = scrollGo.GetComponent<ScrollRect>(); sr.content = crt; sr.horizontal = false; sr.scrollSensitivity = 40; sr.movementType = ScrollRect.MovementType.Clamped;

            // Estadio.
            var s1 = SectionBox(content.transform, "ESTADIO");
            var nameRow = Row(s1, 70);
            Label(nameRow.transform, "Nombre", 28, Text, 180);
            nameField = Input(nameRow.transform, "Nombre del estadio", 760);
            nameField.onEndEdit.AddListener(v => { if (string.IsNullOrWhiteSpace(v)) return; branding.Config.name = v.Trim(); Changed("Nombre actualizado"); });
            ColorRow(s1, "Principal", c => branding.Config.primary = c, () => branding.Config.primary);
            ColorRow(s1, "Secundario", c => branding.Config.secondary = c, () => branding.Config.secondary);
            ColorRow(s1, "Asientos", c => branding.Config.seats = c, () => branding.Config.seats);

            // Cámara.
            var s2 = SectionBox(content.transform, "CÁMARA");
            var modes = (CamMode[])Enum.GetValues(typeof(CamMode));
            for (int i = 0; i < modes.Length; i += 4)
            {
                var row = Row(s2, 70);
                for (int j = i; j < Mathf.Min(i + 4, modes.Length); j++)
                {
                    var m = modes[j];
                    var b = Button(row.transform, CamLabel(m), () => { cam.SetMode(m); branding.Config.camera = m.ToString(); Changed($"Cámara: {CamLabel(m)}"); }, 225);
                    camButtons[m] = b.GetComponent<Image>();
                }
            }
            Label(s2, "Libre: WASD para moverte, Q/E para bajar o subir, botón derecho del mouse para girar.", 22, new Color(1, 1, 1, 0.6f), 0, 40);

            // Publicidad.
            var s3 = SectionBox(content.transform, "PUBLICIDAD");
            foreach (var id in Slots.All) SlotRow(s3, id);

            // Extras.
            var s4 = SectionBox(content.transform, "EXTRAS");
            var r1 = Row(s4, 80);
            Button(r1.transform, "¡Hacer la ola!", () => { crowd.Wave(); Say("¡Ola en camino!"); }, 470);
            Button(r1.transform, "Restablecer", () => { branding.Replace(StadiumConfig.Default()); Say("Configuración de fábrica"); }, 470);
            var r2 = Row(s4, 80);
            Button(r2.transform, "Exportar archivo…", Export, 470);
            Button(r2.transform, "Importar archivo…", Import, 470);
            Label(s4, $"Se guarda sola en {StadiumConfig.AutoPath}", 20, new Color(1, 1, 1, 0.5f), 0, 34);
        }

        static string CamLabel(CamMode m)
        {
            switch (m)
            {
                case CamMode.Transmision: return "Transmisión";
                case CamMode.PrimeraBase: return "Primera base";
                case CamMode.Aerea: return "Aérea";
                default: return m.ToString();
            }
        }

        void ColorRow(Transform parent, string label, Action<string> set, Func<string> get)
        {
            var row = Row(parent, 64);
            Label(row.transform, label, 26, Text, 180);
            foreach (var c in Palette)
            {
                var col = StadiumConfig.Hex(c, Color.gray);
                var b = Button(row.transform, "", () => { set(c); Changed($"{label}: {c}"); }, 46);
                b.GetComponent<Image>().color = col;
            }
            var field = Input(row.transform, "#RRGGBB", 150);
            field.onEndEdit.AddListener(v =>
            {
                var c = v.Trim(); if (!c.StartsWith("#")) c = "#" + c;
                if (!ColorUtility.TryParseHtmlString(c, out _)) { Say("Color no válido: usa #RRGGBB"); return; }
                set(c.ToUpper()); Changed($"{label}: {c.ToUpper()}");
            });
            hex[label] = field;
        }

        void SlotRow(Transform parent, string id)
        {
            var box = Box(parent, Slots.Label(id), new Color(0.07f, 0.09f, 0.15f));
            var vl = box.AddComponent<VerticalLayoutGroup>(); vl.padding = new RectOffset(14, 14, 10, 12); vl.spacing = 8;
            vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            box.AddComponent<LayoutElement>().minHeight = 200;

            var top = Row(box.transform, 90);
            var prevGo = new GameObject("Vista previa", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
            prevGo.transform.SetParent(top.transform, false);
            var le = prevGo.GetComponent<LayoutElement>(); le.preferredWidth = 300; le.preferredHeight = 86;
            var info = Label(top.transform, Slots.Label(id), 26, Text, 640, 86);
            info.alignment = TextAlignmentOptions.Left;

            var bottom = Row(box.transform, 64);
            var input = Input(bottom.transform, "Texto del anuncio", 330);
            Button(bottom.transform, "+ Texto", () =>
            {
                if (string.IsNullOrWhiteSpace(input.text)) { Say("Escribe el texto del anuncio primero"); return; }
                branding.Config.Slot(id).items.Add(AdItem.Text(input.text.Trim().ToUpper()));
                input.text = ""; Changed($"{Slots.Label(id)}: anuncio agregado");
            }, 140);
            Button(bottom.transform, "+ Imagen…", () => AddImage(id), 165);
            Button(bottom.transform, "Quitar", () =>
            {
                var items = branding.Config.Slot(id).items;
                if (items.Count == 0) { Say("No hay anuncios en este espacio"); return; }
                items.RemoveAt(items.Count - 1); Changed($"{Slots.Label(id)}: anuncio quitado");
            }, 130);
            Button(bottom.transform, "s +/−", () =>
            {
                var s = branding.Config.Slot(id);
                s.seconds = s.seconds >= 20 ? 4 : s.seconds + 4;
                Changed($"{Slots.Label(id)}: rota cada {s.seconds:0} s");
            }, 130);
            slotRows[id] = (prevGo.GetComponent<RawImage>(), info, input);
        }

        void AddImage(string id)
        {
            var path = FileDialog.Open("Elige la imagen del anuncio", "Imágenes (PNG, JPG)", "*.png;*.jpg;*.jpeg");
            if (string.IsNullOrEmpty(path)) { Say("No se eligió ninguna imagen"); return; }
            try
            {
                branding.Config.Slot(id).items.Add(StadiumConfig.ImageFromFile(path));
                Changed($"{Slots.Label(id)}: imagen agregada");
            }
            catch (Exception e) { Say($"No se pudo usar la imagen: {e.Message}"); }
        }

        void Export()
        {
            var path = FileDialog.Save("Exportar configuración del estadio", "estadio.json", "Configuración (JSON)", "*.json");
            if (string.IsNullOrEmpty(path)) { Say("Exportación cancelada"); return; }
            try { branding.Config.Save(path); Say($"Exportado a {path}"); }
            catch (Exception e) { Say($"No se pudo exportar: {e.Message}"); }
        }

        void Import()
        {
            var path = FileDialog.Open("Importar configuración del estadio", "Configuración (JSON)", "*.json");
            if (string.IsNullOrEmpty(path)) { Say("Importación cancelada"); return; }
            var c = StadiumConfig.Load(path);
            if (c == null) { Say("Ese archivo no es una configuración válida"); return; }
            branding.Replace(c);
            Say($"Importado de {path}");
        }

        void Refresh()
        {
            if (!panel) return;
            var c = branding.Config;
            if (!nameField.isFocused) nameField.text = c.name;
            hex["Principal"].text = c.primary; hex["Secundario"].text = c.secondary; hex["Asientos"].text = c.seats;
            foreach (var kv in camButtons) kv.Value.color = kv.Key == cam.mode ? BtnOn : Btn;
            foreach (var id in Slots.All)
            {
                var (preview, info, _) = slotRows[id];
                preview.texture = branding.Preview(id);
                var s = c.Slot(id);
                int n = s.items.Count;
                string what = n == 0 ? (id == Slots.Pasto ? "sin anuncios: se pinta el escudo" : "sin anuncios: se muestra el nombre del estadio")
                    : n == 1 ? "1 anuncio" : $"{n} anuncios · rotan cada {s.seconds:0} s";
                info.text = $"<b>{Slots.Label(id)}</b>\n<size=22>{what}</size>";
            }
        }

        // ---------- Piezas de interfaz ----------
        static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        static GameObject Box(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        Transform SectionBox(Transform parent, string title)
        {
            var box = Box(parent, title, Section);
            var vl = box.AddComponent<VerticalLayoutGroup>(); vl.padding = new RectOffset(18, 18, 14, 18); vl.spacing = 12;
            vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            Label(box.transform, title, 30, BtnOn, 0, 44);
            return box.transform;
        }

        static GameObject Row(Transform parent, float height)
        {
            var go = new GameObject("Fila", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var hl = go.GetComponent<HorizontalLayoutGroup>(); hl.spacing = 10; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            go.GetComponent<LayoutElement>().minHeight = height;
            return go;
        }

        static TMP_Text Label(Transform parent, string text, float size, Color color, float width, float height = 0)
        {
            var go = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Left;
            t.raycastTarget = false; t.richText = true;
            var le = go.GetComponent<LayoutElement>();
            if (width > 0) le.preferredWidth = width; else le.flexibleWidth = 1;
            if (height > 0) le.preferredHeight = height;
            return t;
        }

        static GameObject Button(Transform parent, string text, Action onClick, float width)
        {
            var go = new GameObject(text.Length > 0 ? text : "Color", typeof(RectTransform), typeof(Image), typeof(UnityEngine.UI.Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Btn;
            var le = go.GetComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = 58;
            go.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() => onClick());
            if (text.Length > 0)
            {
                var t = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                t.transform.SetParent(go.transform, false);
                Place(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                t.text = text; t.fontSize = 24; t.color = Text; t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            }
            return go;
        }

        static TMP_InputField Input(Transform parent, string placeholder, float width)
        {
            var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = 58;
            go.GetComponent<Image>().color = new Color(0.9f, 0.92f, 0.96f);
            var f = go.GetComponent<TMP_InputField>();
            f.pointSize = 26;
            ((TMP_Text)f.placeholder).text = placeholder;
            return f;
        }
    }
}
