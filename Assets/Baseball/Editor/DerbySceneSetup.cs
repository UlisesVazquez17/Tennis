// Menú Baseball → Crear escena del derby: arma Assets/Baseball/Scenes/HomeRunDerby.unity
// con la cámara, la luz, el estadio y el kiosco ya conectados, y la deja como primera
// escena del build y con pantalla vertical de 1080 × 1920.
//
// Menú Baseball → Crear escena realista (día): la misma escena, de día, con pasto y tierra
// PBR, cielo HDRI y oclusión ambiental, en HomeRunDerby_Realista.unity. No cambia el build.

using Baseball.Net;
using Baseball.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Baseball.EditorTools
{
    public static class DerbySceneSetup
    {
        const string ScenePath = "Assets/Baseball/Scenes/HomeRunDerby.unity";
        const string RealScenePath = "Assets/Baseball/Scenes/HomeRunDerby_Realista.unity";
        const string Real = "Assets/Baseball/Realista/";

        [MenuItem("Baseball/Crear escena del derby")]
        public static void Create() => Build(false);

        [MenuItem("Baseball/Crear escena realista (día)")]
        public static void CreateRealistic() => Build(true);

        static void Build(bool realistic)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (realistic) { BuildRealistic(scene); return; }

            // Noche: la luz principal es la de las torres (alta, desde detrás del home) y otra más
            // débil desde el lado contrario rellena las sombras.
            var main = new GameObject("Luces del estadio").AddComponent<Light>();
            main.type = LightType.Directional;
            main.intensity = 1.7f; main.color = new Color(0.95f, 0.97f, 1f);
            main.shadows = LightShadows.Soft; main.shadowStrength = 0.75f;
            main.transform.rotation = Quaternion.Euler(57, 18, 0);
            var fill = new GameObject("Relleno").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.55f; fill.color = new Color(0.85f, 0.9f, 1f);
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(45, 200, 0);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.1f, 0.12f, 0.2f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.09f, 0.11f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.04f);
            AssetDatabase.DeleteAsset("Assets/Baseball/Materials/Cielo.mat");
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0); sky.SetFloat("_AtmosphereThickness", 0.35f); sky.SetFloat("_Exposure", 0.12f);
            sky.SetColor("_SkyTint", new Color(0.2f, 0.25f, 0.45f)); sky.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
            AssetDatabase.CreateAsset(sky, "Assets/Baseball/Materials/Cielo.mat");
            RenderSettings.skybox = sky;
            RenderSettings.sun = main;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.1f); RenderSettings.fogStartDistance = 300; RenderSettings.fogEndDistance = 1300;

            var camGo = new GameObject("Cámara de transmisión", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<BroadcastCamera>();

            // Post: un poco de contraste, bloom para el sol y viñeta de transmisión.
            var volGo = new GameObject("Post");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true); color.postExposure.Override(0.25f); color.contrast.Override(12); color.saturation.Override(8);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.9f); bloom.threshold.Override(1.0f); bloom.scatter.Override(0.75f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.18f);
            AssetDatabase.DeleteAsset("Assets/Baseball/Settings/DerbyPost.asset");
            AssetDatabase.CreateAsset(profile, "Assets/Baseball/Settings/DerbyPost.asset");
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            vol.sharedProfile = profile;

            Populate(cam, out _);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            // Kiosco vertical.
            PlayerSettings.defaultScreenWidth = 1080;
            PlayerSettings.defaultScreenHeight = 1920;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.productName = "Home Run Derby";
            AssetDatabase.SaveAssets();
            Debug.Log($"[Baseball] Escena creada en {ScenePath}");
        }

        // Estadio, kiosco, jugadores y cámara del cuerpo (igual en las dos escenas).
        static void Populate(Camera cam, out Stadium stadium)
        {
            var camGo = cam.gameObject;
            var stadiumGo = new GameObject("Estadio");
            stadium = stadiumGo.AddComponent<Stadium>();
            var crowd = stadiumGo.AddComponent<Crowd>();
            crowd.stadium = stadium;

            var kioskGo = new GameObject("Kiosco");
            var link = kioskGo.AddComponent<ScreenLink>();
            var hud = kioskGo.AddComponent<Hud>();
            var audio = kioskGo.AddComponent<GameAudio>();
            var branding = kioskGo.AddComponent<Branding>();
            branding.stadium = stadium; branding.crowd = crowd;
            var kiosk = kioskGo.AddComponent<BaseballKiosk>();
            kiosk.link = link; kiosk.hud = hud; kiosk.cam = camGo.GetComponent<BroadcastCamera>(); kiosk.sound = audio;
            kiosk.branding = branding; kiosk.crowd = crowd;
            // Pitcher animado de Mixamo, si está en el proyecto.
            const string pitchFbx = "Assets/Baseball/Characters/Mixamo/Baseball Pitching.fbx";
            kiosk.pitcherModel = AssetDatabase.LoadAssetAtPath<GameObject>(pitchFbx);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(pitchFbx))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) kiosk.pitchClip = c;

            // Bateador que copia el cuerpo de la persona con la webcam (BlazePose).
            const string models = "Assets/Baseball/Pose/Models/";
            var detector = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(models + "pose_detection.onnx");
            if (detector)
            {
                var tracker = new GameObject("Cámara del cuerpo").AddComponent<Baseball.Pose.BodyTracker>();
                tracker.detector = detector;
                tracker.landmarker = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(models + "pose_landmarks_detector_heavy.onnx");
                tracker.cameraName = "C920"; tracker.requestedWidth = 640; tracker.requestedHeight = 480;
                tracker.anchorsCsv = AssetDatabase.LoadAssetAtPath<TextAsset>(models + "pose_anchors.csv");
                kiosk.tracker = tracker;
                // El personaje de Mixamo con piel, si está; si no, el maniquí del pitcher.
                kiosk.batterModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Baseball/Characters/Mixamo/Bateador/Bateador_Ch38.fbx") ?? kiosk.pitcherModel;
            }
            var panel = kioskGo.AddComponent<OperatorPanel>();
            panel.branding = branding; panel.cam = kiosk.cam; panel.crowd = crowd;
        }

        // ---------- Escena realista de día ----------
        static void BuildRealistic(UnityEngine.SceneManagement.Scene scene)
        {
            PrepareImports();

            // Sol de mediodía, un poco de lado para que las sombras den volumen.
            var sun = new GameObject("Sol").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f; sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(52, -38, 0);

            // Cielo HDRI real: también da la luz ambiental y los reflejos.
            var hdri = AssetDatabase.LoadAssetAtPath<Texture>(Real + "Cielo/Cielo_Kloofendal48d.hdr");
            AssetDatabase.DeleteAsset(Real + "Cielo/Cielo.mat");
            var sky = new Material(Shader.Find("Skybox/Panoramic"));
            sky.SetTexture("_MainTex", hdri); sky.SetFloat("_Exposure", 1.05f); sky.SetFloat("_Rotation", 140);
            AssetDatabase.CreateAsset(sky, Real + "Cielo/Cielo.mat");
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.74f, 0.8f, 0.88f); RenderSettings.fogStartDistance = 250; RenderSettings.fogEndDistance = 1400;
            DynamicGI.UpdateEnvironment();

            var camGo = new GameObject("Cámara de transmisión", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<BroadcastCamera>();

            // Post de transmisión de día: ACES, contraste moderado y poco bloom.
            var vol = new GameObject("Post").AddComponent<Volume>(); vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true); color.postExposure.Override(0.35f); color.contrast.Override(10); color.saturation.Override(6);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.2f); bloom.threshold.Override(1.2f);
            profile.Add<Vignette>(true).intensity.Override(0.15f);
            AssetDatabase.DeleteAsset("Assets/Baseball/Settings/DerbyPostRealista.asset");
            AssetDatabase.CreateAsset(profile, "Assets/Baseball/Settings/DerbyPostRealista.asset");
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            vol.sharedProfile = profile;
            EnsureAmbientOcclusion();

            Populate(cam, out var stadium);
            stadium.nightLights = false;
            stadium.grassColor = AssetDatabase.LoadAssetAtPath<Texture2D>(Real + "Texturas/Pasto_Color.jpg");
            stadium.grassNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(Real + "Texturas/Pasto_Normal.jpg");
            stadium.dirtColor = AssetDatabase.LoadAssetAtPath<Texture2D>(Real + "Texturas/Tierra_Color.jpg");
            stadium.dirtNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(Real + "Texturas/Tierra_Normal.jpg");
            stadium.Build();

            EditorSceneManager.SaveScene(scene, RealScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Baseball] Escena realista creada en {RealScenePath}");
        }

        // Mapas normales como normales; el cielo HDRI sin compresión agresiva.
        static void PrepareImports()
        {
            foreach (var n in new[] { "Pasto_Normal.jpg", "Tierra_Normal.jpg" })
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(Real + "Texturas/" + n);
                if (imp && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; imp.SaveAndReimport(); }
            }
            foreach (var n in new[] { "Pasto_Color.jpg", "Tierra_Color.jpg" })
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(Real + "Texturas/" + n);
                if (imp && imp.anisoLevel < 8) { imp.anisoLevel = 8; imp.SaveAndReimport(); }
            }
            var sky = (TextureImporter)AssetImporter.GetAtPath(Real + "Cielo/Cielo_Kloofendal48d.hdr");
            if (sky && sky.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                sky.textureShape = TextureImporterShape.Texture2D; sky.mipmapEnabled = false;
                sky.textureCompression = TextureImporterCompression.CompressedHQ; sky.wrapModeU = TextureWrapMode.Repeat;
                sky.SaveAndReimport();
            }
        }

        // Oclusión ambiental (SSAO) en el renderer de URP que usa el proyecto.
        static void EnsureAmbientOcclusion()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) return;
            var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = field?.GetValue(pipeline) as ScriptableRendererData[];
            var renderer = list != null && list.Length > 0 ? list[0] : null;
            if (!renderer) return;
            foreach (var f in renderer.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion) return;
            var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "Oclusión ambiental";
            AssetDatabase.AddObjectToAsset(ssao, renderer);
            renderer.rendererFeatures.Add(ssao);
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }

    }
}
