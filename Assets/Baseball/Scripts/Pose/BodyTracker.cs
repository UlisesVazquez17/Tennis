// Detección del cuerpo con la webcam: BlazePose (Google, Apache 2.0) corriendo en la
// tarjeta de video con Inference Engine. Adaptado del ejemplo BlazeDetectionSample/Pose de
// Unity (github.com/Unity-Technologies/sentis-samples).
//
// Dos redes por cuadro: el detector encuentra a la persona y el landmarker saca 33 puntos
// del cuerpo en 3D, en metros y con origen en la cadera. Aquí se pasan al marco de Unity
// visto desde la cámara (x a la derecha de la imagen, y arriba, z alejándose de la cámara)
// y se suavizan con un filtro One Euro.

using System;
using System.Linq;
using Unity.InferenceEngine;
using Unity.Mathematics;
using UnityEngine;

namespace Baseball.Pose
{
    public class BodyTracker : MonoBehaviour
    {
        public ModelAsset detector;
        public ModelAsset landmarker;
        public TextAsset anchorsCsv;
        [Tooltip("Parte del nombre de la cámara a usar. Vacío = la primera que no sea virtual.")]
        public string cameraName = "";
        public int requestedWidth = 640, requestedHeight = 480;   // el modelo usa 256 px; más resolución solo baja los cuadros por segundo de la cámara
        [Range(0, 1)] public float scoreThreshold = 0.5f;

        public const int Points = 33;
        // Índices de BlazePose.
        public const int Nose = 0, LEar = 7, REar = 8, LShoulder = 11, RShoulder = 12, LElbow = 13, RElbow = 14, LWrist = 15, RWrist = 16,
            LIndex = 19, RIndex = 20, LHip = 23, RHip = 24, LKnee = 25, RKnee = 26, LAnkle = 27, RAnkle = 28, LToe = 31, RToe = 32;

        public bool Tracking { get; private set; }
        public int Samples { get; private set; }        // detecciones completas desde el inicio
        public float Fps { get; private set; }           // detecciones por segundo (promedio)
        public float SampleTime { get; private set; }    // realtimeSinceStartup de la última
        public float LastSeen { get; private set; } = -100;
        public readonly Vector3[] World = new Vector3[Points];      // metros, marco de la cámara
        public readonly float[] Visibility = new float[Points];
        public readonly Vector2[] Image = new Vector2[Points];      // 0…1 en la imagen (y hacia arriba)
        public WebCamTexture Camera { get; private set; }
        public string Status { get; private set; } = "Sin iniciar";
        public float LastScore { get; private set; }   // confianza de la última detección (diagnóstico)
        [Tooltip("Pruebas: ignora la cámara y usa la pose de Inject.")]
        public bool manual;

        // Pruebas: fija una pose (marco de la cámara, metros, origen en la cadera).
        public void Inject(Vector3[] pose)
        {
            manual = true;
            for (int i = 0; i < Points; i++) { World[i] = pose[i]; Visibility[i] = 1; }
            Tracking = true; LastSeen = Time.realtimeSinceStartup;
        }

        const int NumAnchors = 2254, DetectorSize = 224, LandmarkerSize = 256, Outputs = 39;
        float[,] anchors;
        Worker detectorWorker, landmarkerWorker;
        Tensor<float> detectorInput, landmarkerInput;
        Awaitable loop;
        // Recorte del cuerpo para el siguiente cuadro (centro de cadera y punto de escala, en
        // píxeles). Mientras se sigue a la persona no hace falta correr el detector.
        bool haveRoi; float2 roi1, roi2;
        readonly OneEuro[] filters = Enumerable.Range(0, Points * 3).Select(_ => new OneEuro(1.4f, 0.6f)).ToArray();

        static ComputeShader imageShader;
        static int kernel;

        async void Start()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0) { Status = "No hay cámara"; return; }
            var dev = devices.FirstOrDefault(d => !string.IsNullOrEmpty(cameraName) && d.name.Contains(cameraName));
            if (string.IsNullOrEmpty(dev.name)) dev = devices.FirstOrDefault(d => !d.name.ToLower().Contains("virtual"));
            if (string.IsNullOrEmpty(dev.name)) dev = devices[0];
            Open(dev.name);

            imageShader = Resources.Load<ComputeShader>("ComputeShaders/ImageTransform");
            kernel = imageShader.FindKernel("ImageSample");
            anchors = LoadAnchors(anchorsCsv.text);

            // El detector se compila con el filtro que elige la mejor detección dentro del modelo.
            var det = ModelLoader.Load(detector);
            var graph = new FunctionalGraph();
            var input = graph.AddInput(det, 0);
            var outs = Functional.Forward(det, input);
            var scores = Functional.Sigmoid(Functional.Clamp(outs[1], -100, 100));
            var best = Functional.ArgMax(outs[1], 1).Squeeze();
            var box = Functional.IndexSelect(outs[0], 1, best).Unsqueeze(0);
            var score = Functional.IndexSelect(scores, 1, best).Unsqueeze(0);
            detectorWorker = new Worker(graph.Compile(best, score, box), BackendType.GPUCompute);
            landmarkerWorker = new Worker(ModelLoader.Load(landmarker), BackendType.GPUCompute);
            detectorInput = new Tensor<float>(new TensorShape(1, DetectorSize, DetectorSize, 3));
            landmarkerInput = new Tensor<float>(new TensorShape(1, LandmarkerSize, LandmarkerSize, 3));

            while (this)
            {
                try
                {
                    if (!manual && Camera.width > 16 && Camera.didUpdateThisFrame) { loop = Detect(Camera); await loop; }
                    else await Awaitable.NextFrameAsync();
                }
                catch (OperationCanceledException) { break; }
            }
        }

        async Awaitable Detect(Texture tex)
        {
            float w = tex.width, h = tex.height, size = Mathf.Max(w, h);
            float2 kp1, kp2;
            if (haveRoi) { kp1 = roi1; kp2 = roi2; }
            else
            {
                var found = await FindPerson(tex, w, h, size);
                if (!found.HasValue) { Lost(); return; }
                (kp1, kp2) = found.Value;
            }
            await Landmarks(tex, w, h, kp1, kp2);
        }

        async Awaitable<(float2, float2)?> FindPerson(Texture tex, float w, float h, float size)
        {
            // Tensor → imagen: centra la imagen en un cuadro de size × size.
            float scale = size / DetectorSize;
            var M = Mul(Translation(0.5f * (new float2(w, h) + new float2(-size, size))), Scale(new float2(scale, -scale)));
            Sample(tex, detectorInput, M);
            detectorWorker.Schedule(detectorInput);

            var idxA = (detectorWorker.PeekOutput(0) as Tensor<int>).ReadbackAndCloneAsync();
            var scoreA = (detectorWorker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
            var boxA = (detectorWorker.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync();
            using var idxT = await idxA;
            using var scoreT = await scoreA;
            using var boxT = await boxA;

            LastScore = scoreT[0];
            if (scoreT[0] < scoreThreshold) return null;
            int idx = idxT[0];
            var anchor = DetectorSize * new float2(anchors[idx, 0], anchors[idx, 1]);
            // Dos puntos del detector: centro de la cadera y un punto que da el tamaño y el giro del cuerpo.
            return (Mul(M, anchor + new float2(boxT[0, 0, 4], boxT[0, 0, 5])), Mul(M, anchor + new float2(boxT[0, 0, 6], boxT[0, 0, 7])));
        }

        async Awaitable Landmarks(Texture tex, float w, float h, float2 kp1, float2 kp2)
        {
            var delta = kp2 - kp1;
            float radius = 1.25f * math.length(delta);
            float theta = math.atan2(delta.y, delta.x);
            var origin = new float2(0.5f * LandmarkerSize, 0.5f * LandmarkerSize);
            float s2 = radius / (0.5f * LandmarkerSize);
            var M2 = Mul(Mul(Mul(Translation(kp1), Scale(new float2(s2, -s2))), Rotation(0.5f * Mathf.PI - theta)), Translation(-origin));
            Sample(tex, landmarkerInput, M2);
            landmarkerWorker.Schedule(landmarkerInput);

            var imgA = (landmarkerWorker.PeekOutput("Identity") as Tensor<float>).ReadbackAndCloneAsync();
            var scoreA = (landmarkerWorker.PeekOutput("Identity_1") as Tensor<float>).ReadbackAndCloneAsync();
            var worldA = (landmarkerWorker.PeekOutput("Identity_4") as Tensor<float>).ReadbackAndCloneAsync();
            using var img = await imgA;
            using var present = await scoreA;
            using var world = await worldA;

            // Si el landmarker ya no ve a una persona en el recorte, se vuelve a usar el detector.
            LastScore = present[0];
            if (present[0] < scoreThreshold) { haveRoi = false; Lost(); return; }
            // Recorte del siguiente cuadro: puntos auxiliares 33 (centro de cadera) y 34 (escala).
            roi1 = Mul(M2, new float2(img[5 * 33], img[5 * 33 + 1]));
            roi2 = Mul(M2, new float2(img[5 * 34], img[5 * 34 + 1]));
            haveRoi = true;

            float t = Time.realtimeSinceStartup;
            var raw = new Vector3[Points];
            for (int i = 0; i < Points; i++)
            {
                var p = Mul(M2, new float2(img[5 * i], img[5 * i + 1]));
                Image[i] = new Vector2(p.x / w, p.y / h);
                Visibility[i] = Sigmoid(Mathf.Min(img[5 * i + 3], img[5 * i + 4]));
                // Mundo: y hacia abajo en BlazePose → hacia arriba en Unity; z crece alejándose de la cámara.
                raw[i] = new Vector3(world[3 * i], -world[3 * i + 1], world[3 * i + 2]);
            }

            // El landmarker trabaja sobre el recorte girado: se alinea la línea cadera→hombros
            // de los puntos 3D con la de la imagen para deshacer ese giro.
            Vector2 midImg = (Image[LShoulder] + Image[RShoulder]) * 0.5f - (Image[LHip] + Image[RHip]) * 0.5f;
            Vector3 midW = (raw[LShoulder] + raw[RShoulder]) * 0.5f - (raw[LHip] + raw[RHip]) * 0.5f;
            float fix = Mathf.Atan2(midImg.y * h, midImg.x * w) - Mathf.Atan2(midW.y, midW.x);
            var rot = Quaternion.AngleAxis(fix * Mathf.Rad2Deg, Vector3.forward);
            for (int i = 0; i < Points; i++)
            {
                var p = rot * raw[i];
                World[i] = new Vector3(filters[3 * i].Filter(p.x, t), filters[3 * i + 1].Filter(p.y, t), filters[3 * i + 2].Filter(p.z, t));
            }
            Tracking = true;
            if (Samples > 0) Fps = Mathf.Lerp(Fps <= 0 ? 1 / Mathf.Max(1e-3f, t - SampleTime) : Fps, 1 / Mathf.Max(1e-3f, t - SampleTime), 0.1f);
            LastSeen = t; SampleTime = t; Samples++;
        }

        void Open(string device)
        {
            if (Camera) Camera.Stop();
            Camera = new WebCamTexture(device, requestedWidth, requestedHeight, 30);
            Camera.Play();
            Status = $"Cámara: {device}";
            haveRoi = false; Tracking = false;
        }

        // Pasa a la siguiente cámara conectada (sin las virtuales). Devuelve su nombre.
        public string NextCamera()
        {
            var real = WebCamTexture.devices.Where(d => !d.name.ToLower().Contains("virtual")).Select(d => d.name).ToList();
            if (real.Count == 0) return null;
            int i = Camera ? real.IndexOf(Camera.deviceName) : -1;
            var next = real[(i + 1) % real.Count];
            Open(next);
            return next;
        }

        void Lost()
        {
            // Medio segundo de gracia antes de soltar el cuerpo (parpadeos del detector).
            if (Time.realtimeSinceStartup - LastSeen > 0.5f) Tracking = false;
        }

        void OnDestroy()
        {
            try { loop?.Cancel(); } catch (InvalidOperationException) { }   // ya había terminado
            detectorWorker?.Dispose(); landmarkerWorker?.Dispose();
            detectorInput?.Dispose(); landmarkerInput?.Dispose();
            if (Camera) Camera.Stop();
        }

        static float Sigmoid(float x) => 1f / (1f + Mathf.Exp(-x));

        // ---------- Utilidades del ejemplo de Unity (BlazeUtils) ----------
        static float2x3 Mul(float2x3 a, float2x3 b) => new float2x3(
            a[0][0] * b[0][0] + a[1][0] * b[0][1], a[0][0] * b[1][0] + a[1][0] * b[1][1], a[0][0] * b[2][0] + a[1][0] * b[2][1] + a[2][0],
            a[0][1] * b[0][0] + a[1][1] * b[0][1], a[0][1] * b[1][0] + a[1][1] * b[1][1], a[0][1] * b[2][0] + a[1][1] * b[2][1] + a[2][1]);
        static float2 Mul(float2x3 a, float2 b) => new float2(a[0][0] * b.x + a[1][0] * b.y + a[2][0], a[0][1] * b.x + a[1][1] * b.y + a[2][1]);
        static float2x3 Rotation(float th) { float s = math.sin(th), c = math.cos(th); return new float2x3(c, -s, 0, s, c, 0); }
        static float2x3 Translation(float2 d) => new float2x3(1, 0, d.x, 0, 1, d.y);
        static float2x3 Scale(float2 s) => new float2x3(s.x, 0, 0, 0, s.y, 0);

        static void Sample(Texture src, Tensor<float> dst, float2x3 M)
        {
            var data = ComputeTensorData.Pin(dst, false);
            imageShader.SetTexture(kernel, "X_tex2D", src);
            imageShader.SetBuffer(kernel, "Optr", data.buffer);
            imageShader.SetInt("O_height", dst.shape[1]); imageShader.SetInt("O_width", dst.shape[2]); imageShader.SetInt("O_channels", dst.shape[3]);
            imageShader.SetInt("X_height", src.height); imageShader.SetInt("X_width", src.width);
            imageShader.SetMatrix("affineMatrix", new Matrix4x4(new Vector4(M[0][0], M[0][1]), new Vector4(M[1][0], M[1][1]), new Vector4(M[2][0], M[2][1]), Vector4.zero));
            int g = (dst.shape[1] + 7) / 8;
            imageShader.Dispatch(kernel, g, g, 1);
        }

        static float[,] LoadAnchors(string csv)
        {
            var a = new float[NumAnchors, 4];
            var lines = csv.Split('\n');
            for (int i = 0; i < NumAnchors; i++)
            {
                var v = lines[i].Split(',');
                for (int j = 0; j < 4; j++) a[i, j] = float.Parse(v[j], System.Globalization.CultureInfo.InvariantCulture);
            }
            return a;
        }
    }

    // Filtro One Euro (Casiez et al.): suaviza mucho cuando el punto está quieto y poco cuando
    // se mueve rápido, para que el swing no se retrase.
    public class OneEuro
    {
        readonly float minCutoff, beta, dCutoff = 1;
        float x, dx, t = -1;
        public OneEuro(float minCutoff, float beta) { this.minCutoff = minCutoff; this.beta = beta; }

        static float Alpha(float cutoff, float dt) { float tau = 1 / (2 * Mathf.PI * cutoff); return 1 / (1 + tau / dt); }

        public float Filter(float value, float time)
        {
            if (t < 0) { t = time; x = value; return value; }
            float dt = Mathf.Max(1e-3f, time - t); t = time;
            float d = (value - x) / dt;
            dx += Alpha(dCutoff, dt) * (d - dx);
            float cutoff = minCutoff + beta * Mathf.Abs(dx);
            x += Alpha(cutoff, dt) * (value - x);
            return x;
        }
    }
}
