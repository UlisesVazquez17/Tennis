// Identidad y publicidad del estadio. Toma la configuración (nombre, colores, anuncios),
// la aplica al estadio, al público y a los uniformes, genera las texturas de los nueve
// espacios publicitarios y los rota. La guarda sola cada vez que cambia.

using System;
using System.Collections.Generic;
using Baseball.Config;
using UnityEngine;

namespace Baseball.View
{
    public class Branding : MonoBehaviour
    {
        public Stadium stadium;
        public Crowd crowd;

        public StadiumConfig Config { get; private set; }
        public Texture2D Crest { get; private set; }
        public Videoboard Board { get; private set; }
        public Blimps Blimps { get; private set; }
        public event Action<StadiumConfig> Changed;

        readonly Dictionary<string, List<Texture2D>> textures = new Dictionary<string, List<Texture2D>>();
        readonly Dictionary<string, float> rotateAt = new Dictionary<string, float>();
        readonly Dictionary<string, int> shown = new Dictionary<string, int>();

        void Awake()
        {
            Config = StadiumConfig.LoadOrDefault();
            if (!stadium) stadium = FindAnyObjectByType<Stadium>();
            if (!crowd) crowd = FindAnyObjectByType<Crowd>();
        }

        void Start()
        {
            Board = new GameObject("Videomarcador (pantalla)").AddComponent<Videoboard>();
            Board.Attach(stadium.VideoboardAnchor);
            Blimps = new GameObject("Dirigibles").AddComponent<Blimps>();
            Apply();
        }

        // Llamar después de cambiar Config (lo hace el panel).
        public void Apply(bool save = true)
        {
            stadium.Apply(Config);
            if (crowd) crowd.Apply(Config);
            if (Crest) Destroy(Crest);
            Crest = Baker.Crest(Config.name, Config.Primary, Config.Secondary);
            Board.SetIdentity(Config.name, Crest, Config.Primary, Config.Secondary);
            RebuildAds();
            if (save) { try { Config.Save(StadiumConfig.AutoPath); } catch (Exception e) { Debug.LogWarning($"[Estadio] No se pudo guardar: {e.Message}"); } }
            Changed?.Invoke(Config);
        }

        public void Replace(StadiumConfig c) { Config = c; Apply(); }

        void RebuildAds()
        {
            foreach (var list in textures.Values) foreach (var t in list) if (t) Destroy(t);
            textures.Clear(); shown.Clear(); rotateAt.Clear();
            foreach (var id in Slots.All)
            {
                var slot = Config.Slot(id);
                var px = Slots.Pixels(id);
                var list = new List<Texture2D>();
                foreach (var item in slot.items)
                {
                    var tex = Render(item, id, px);
                    if (tex) list.Add(tex);
                }
                // Sin anuncios: el escudo en el pasto y el nombre del estadio en lo demás.
                if (list.Count == 0)
                    list.Add(id == Slots.Pasto ? Crest : Baker.TextAd(Config.name.ToUpper(), Config.Primary, Color.white, Config.Secondary, px.x, px.y));
                if (id == Slots.Cinta) foreach (var t in list) t.wrapMode = TextureWrapMode.Repeat;
                textures[id] = list;
                shown[id] = 0;
                rotateAt[id] = Time.time + Mathf.Max(2, slot.seconds);
                Show(id);
            }
        }

        Texture2D Render(AdItem item, string id, Vector2Int px)
        {
            var bg = StadiumConfig.Hex(item.bg, Config.Primary);
            if (item.kind == AdKind.Imagen)
            {
                var img = StadiumConfig.DecodeImage(item);
                if (!img) return null;
                // En el pasto, la imagen conserva su transparencia.
                var tex = Baker.FitImage(img, id == Slots.Pasto ? Color.clear : bg, px.x, px.y);
                Destroy(img);
                return tex;
            }
            if (string.IsNullOrWhiteSpace(item.text)) return null;
            return Baker.TextAd(item.text, bg, StadiumConfig.Hex(item.fg, Color.white), Config.Secondary, px.x, px.y);
        }

        public Texture2D Preview(string id) => textures.TryGetValue(id, out var l) && l.Count > 0 ? l[shown.TryGetValue(id, out var i) ? i % l.Count : 0] : null;

        void Show(string id)
        {
            var list = textures[id];
            int k = shown[id];
            if (stadium.AdSurfaces.TryGetValue(id, out var surfaces))
                for (int i = 0; i < surfaces.Count; i++)
                {
                    // Los paneles de la barda alternan anuncios entre sí.
                    var tex = list[(k + (id == Slots.Barda || id == Slots.Pasto ? i : 0)) % list.Count];
                    surfaces[i].sharedMaterial.SetTexture("_BaseMap", tex);
                }
            if (id == Slots.Cinta && stadium.Ribbon) stadium.Ribbon.sharedMaterial.SetTexture("_BaseMap", list[k % list.Count]);
            if (id == Slots.Videomarcador) Board.SetAd(list[k % list.Count]);
            if (id == Slots.Dirigible1) Blimps.SetAd(0, list[k % list.Count]);
            if (id == Slots.Dirigible2) Blimps.SetAd(1, list[k % list.Count]);
        }

        void Update()
        {
            foreach (var id in Slots.All)
            {
                if (!textures.TryGetValue(id, out var list) || list.Count < 2 || Time.time < rotateAt[id]) continue;
                shown[id]++;
                rotateAt[id] = Time.time + Mathf.Max(2, Config.Slot(id).seconds);
                Show(id);
            }
            // La cinta LED corre de derecha a izquierda.
            if (stadium.Ribbon)
            {
                var m = stadium.Ribbon.sharedMaterial;
                // La U de la fachada crece en sentido contrario a la lectura: escala −1 la endereza.
                m.SetTextureScale("_BaseMap", new Vector2(-1, 1));
                m.SetTextureOffset("_BaseMap", new Vector2((Time.time * 0.12f) % 1, 0));
            }
        }
    }
}
