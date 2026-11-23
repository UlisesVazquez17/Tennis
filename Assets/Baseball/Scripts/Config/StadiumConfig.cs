// Configuración del estadio que el operador cambia desde el panel: nombre, colores,
// cámara y los nueve espacios publicitarios. Se guarda sola en el equipo y se puede
// exportar a un archivo .json (con las imágenes adentro) para llevarla al tótem.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Baseball.Config
{
    public enum AdKind { Texto, Imagen }

    [Serializable]
    public class AdItem
    {
        public AdKind kind = AdKind.Texto;
        public string text = "";
        public string bg = "#0B1F4D", fg = "#FFFFFF";
        public string image;     // PNG/JPG en base64 cuando kind = Imagen
        public string name;      // nombre del archivo original, para el panel

        public static AdItem Text(string t, string bg = null, string fg = null) =>
            new AdItem { kind = AdKind.Texto, text = t, bg = bg, fg = fg ?? "#FFFFFF" };
    }

    [Serializable]
    public class AdSlot
    {
        public List<AdItem> items = new List<AdItem>();
        public float seconds = 8;    // cada cuánto rota cuando hay más de un anuncio
    }

    public static class Slots
    {
        public const string Videomarcador = "videomarcador", Espectacular = "espectacular", Barda = "barda",
            Cinta = "cinta", Home = "home", Dugouts = "dugouts", Dirigible1 = "dirigible1", Dirigible2 = "dirigible2", Pasto = "pasto";

        public static readonly string[] All = { Videomarcador, Espectacular, Barda, Cinta, Home, Dugouts, Dirigible1, Dirigible2, Pasto };

        public static string Label(string id)
        {
            switch (id)
            {
                case Videomarcador: return "Videomarcador";
                case Espectacular: return "Espectacular (jardín derecho)";
                case Barda: return "Paneles de la barda";
                case Cinta: return "Cinta LED (segundo piso)";
                case Home: return "Pantalla detrás de home";
                case Dugouts: return "Techo de dugouts";
                case Dirigible1: return "Dirigible 1";
                case Dirigible2: return "Dirigible 2";
                case Pasto: return "Logos en el pasto";
                default: return id;
            }
        }

        // Proporción ancho / alto de cada espacio, para generar sus texturas.
        public static Vector2Int Pixels(string id)
        {
            switch (id)
            {
                case Videomarcador: return new Vector2Int(1024, 260);
                case Espectacular: return new Vector2Int(1536, 480);
                case Barda: return new Vector2Int(1024, 142);
                case Cinta: return new Vector2Int(1536, 96);
                case Home: return new Vector2Int(1536, 124);
                case Dugouts: return new Vector2Int(1024, 176);
                case Dirigible1: case Dirigible2: return new Vector2Int(1024, 260);
                case Pasto: return new Vector2Int(512, 512);
                default: return new Vector2Int(1024, 256);
            }
        }
    }

    [Serializable]
    public class StadiumConfig
    {
        public string name = "Estadio del Derby";
        public string primary = "#0B2A6F", secondary = "#F2B705", seats = "#16365E";
        public string camera = "Auto";
        public Dictionary<string, AdSlot> ads = new Dictionary<string, AdSlot>();

        public AdSlot Slot(string id)
        {
            if (!ads.TryGetValue(id, out var s)) ads[id] = s = new AdSlot();
            return s;
        }

        [JsonIgnore] public Color Primary => Hex(primary, new Color(0.04f, 0.16f, 0.44f));
        [JsonIgnore] public Color Secondary => Hex(secondary, new Color(0.95f, 0.72f, 0.02f));
        [JsonIgnore] public Color Seats => Hex(seats, new Color(0.09f, 0.21f, 0.37f));

        public static Color Hex(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c) ? c : fallback;

        public static string ToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // Anuncios de ejemplo: el operador los reemplaza por los de sus patrocinadores.
        public static StadiumConfig Default()
        {
            var c = new StadiumConfig();
            c.Slot(Slots.Videomarcador).items.AddRange(new[] { AdItem.Text("TU MARCA AQUÍ"), AdItem.Text("ANÚNCIATE EN EL DERBY", "#A30D1F") });
            c.Slot(Slots.Videomarcador).seconds = 6;
            c.Slot(Slots.Espectacular).items.Add(AdItem.Text("ESPECTACULAR DISPONIBLE", "#A30D1F"));
            c.Slot(Slots.Barda).items.AddRange(new[] { AdItem.Text("TU MARCA"), AdItem.Text("PATROCINADOR", "#A30D1F"), AdItem.Text("ANÚNCIATE", "#0E6B3A") });
            c.Slot(Slots.Cinta).items.Add(AdItem.Text("HOME RUN DERBY  •  ESCANEA EL QR Y BATEA  •  TU MARCA AQUÍ  •"));
            c.Slot(Slots.Cinta).seconds = 12;
            c.Slot(Slots.Home).items.AddRange(new[] { AdItem.Text("PANTALLA DETRÁS DE HOME"), AdItem.Text("SE VE EN LA TRANSMISIÓN", "#A30D1F") });
            c.Slot(Slots.Dugouts).items.Add(AdItem.Text("TU MARCA EN EL DUGOUT"));
            c.Slot(Slots.Dirigible1).items.Add(AdItem.Text("TU MARCA", "#FFFFFF", "#0B2A6F"));
            c.Slot(Slots.Dirigible2).items.Add(AdItem.Text("PATROCINADOR", "#FFFFFF", "#A30D1F"));
            c.Slot(Slots.Pasto).items.Clear();   // sin anuncios: se pinta el escudo del estadio
            return c;
        }

        // ---------- Archivos ----------
        public static string AutoPath => Path.Combine(Application.persistentDataPath, "estadio.json");

        static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Json);

        public static StadiumConfig FromJson(string json)
        {
            var c = JsonConvert.DeserializeObject<StadiumConfig>(json, Json) ?? Default();
            c.ads ??= new Dictionary<string, AdSlot>();
            foreach (var id in Slots.All) c.Slot(id);
            return c;
        }

        public void Save(string path) => File.WriteAllText(path, ToJson());

        public static StadiumConfig Load(string path)
        {
            try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null; }
            catch (Exception e) { Debug.LogWarning($"[Estadio] No se pudo leer {path}: {e.Message}"); return null; }
        }

        public static StadiumConfig LoadOrDefault() => Load(AutoPath) ?? Default();

        // Imagen elegida por el operador: se reduce a 1024 px y se guarda dentro de la configuración.
        public static AdItem ImageFromFile(string path)
        {
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(File.ReadAllBytes(path))) throw new Exception("No es una imagen PNG o JPG");
            int max = Mathf.Max(tex.width, tex.height);
            if (max > 1024)
            {
                float k = 1024f / max;
                var rt = RenderTexture.GetTemporary(Mathf.RoundToInt(tex.width * k), Mathf.RoundToInt(tex.height * k));
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var small = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                small.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); small.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(tex); tex = small;
            }
            bool png = path.ToLower().EndsWith(".png");
            var bytes = png ? tex.EncodeToPNG() : tex.EncodeToJPG(88);
            UnityEngine.Object.Destroy(tex);
            return new AdItem { kind = AdKind.Imagen, image = Convert.ToBase64String(bytes), name = Path.GetFileName(path), bg = "#000000" };
        }

        public static Texture2D DecodeImage(AdItem item)
        {
            if (item.kind != AdKind.Imagen || string.IsNullOrEmpty(item.image)) return null;
            var tex = new Texture2D(2, 2) { hideFlags = HideFlags.DontSave };
            try { tex.LoadImage(Convert.FromBase64String(item.image)); return tex; }
            catch { return null; }
        }
    }
}
