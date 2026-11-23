// Director del kiosco: une la conexión con el teléfono, el derby, la escena y la interfaz.
// Es el equivalente de app/static/js/beisbol/main.js del proyecto tenis.
//
// Ciclo: espera (la CPU batea sola y se muestra el QR) → el teléfono se conecta → calibra →
// derby de 10 lanzamientos → resultado y alias para el ranking → sala nueva.
//
// Para probar sin teléfono: P empieza un derby local y Espacio batea.

using System.Collections;
using System.Text;
using Baseball.Net;
using Baseball.View;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace Baseball
{
    public partial class BaseballKiosk : MonoBehaviour
    {
        [Header("Escena")]
        public ScreenLink link;
        public Hud hud;
        public BroadcastCamera cam;
        public GameAudio sound;
        public Branding branding;
        public Crowd crowd;

        [Header("Personajes")]
        [Tooltip("Modelo con rig humanoide para el pitcher (p. ej. Mixamo). Vacío = figura simple.")]
        public GameObject pitcherModel;
        public AnimationClip pitchClip;
        [Tooltip("Segundo del clip en que el pitcher suelta la bola.")]
        public float pitchReleaseSeconds = 2.05f;
        [Tooltip("Cuerpo del bateador (humanoide). Con un BodyTracker, copia el cuerpo de la persona frente a la cámara.")]
        public GameObject batterModel;
        public Pose.BodyTracker tracker;

        [Header("Derby")]
        [Range(3, 30)] public int pitches = 10;
        [Tooltip("0 = sin límite de outs")] public int outs = 0;
        [Range(1, 3)] public int level = 1;
        public float resultSeconds = 15, resultMaxSeconds = 30, controlGraceSeconds = 20;

        DerbyGame game, demo;
        KioskView view = KioskView.Idle;
        string phoneStage = "intro";
        bool localPlay;     // derby con el teclado, sin teléfono
        double windupAt;
        Pitch demoHandled;

        Transform ballT, landingT; TrailRenderer trail; TMP_Text landingText;
        LineRenderer zone; Transform zoneDot;
        IPitcher pitcher; IBatter batter; Fielders fielders; Battery battery;
        Coroutine lostCo, resultCo;
        float resultUntil, resultMax;
        Derby lastResult;

        static double Now => ScreenLink.Now;

        void Awake()
        {
#if !UNITY_EDITOR
            // Ejecutable del tótem: pantalla vertical de 1080 × 1920. En un monitor vertical ocupa
            // toda la pantalla; en uno horizontal abre una ventana de esa proporción.
            bool portrait = Screen.currentResolution.height >= Screen.currentResolution.width;
            if (portrait) Screen.SetResolution(1080, 1920, FullScreenMode.FullScreenWindow);
            else { int h = Screen.currentResolution.height - 80; Screen.SetResolution(h * 9 / 16, h, FullScreenMode.Windowed); }
#endif
            Application.targetFrameRate = 60;
        }

        void Start()
        {
            var field = new GameObject("Jugadores").transform;
            pitcher = pitcherModel && pitchClip ? new AnimatedPitcher(field, pitcherModel, pitchClip, pitchReleaseSeconds) : (IPitcher)new Pitcher(field); batter = batterModel && tracker ? new Pose.AvatarBatter(field, batterModel, tracker, pitchClip) : (IBatter)new Batter(field); fielders = new Fielders(field); battery = new Battery(field);
            BuildBall(); BuildZone();

            game = new DerbyGame(pitches, outs > 0 ? outs : int.MaxValue, level);
            game.OnEvent += e => OnGameEvent(e, true);
            demo = new DerbyGame(1000000000, int.MaxValue, 2);
            demo.OnEvent += e => OnGameEvent(e, false);

            branding.Changed += Uniforms;
            if (branding.Config != null) Uniforms(branding.Config);
            link.OnStatus += on => hud.Offline(!on && !testWithoutPhone);
            link.OnRoom += OnRoom;
            link.OnPeer += OnPeer;
            link.OnMessage += OnMessage;
            hud.Offline(!link.Online && !testWithoutPhone);
            Show(KioskView.Idle);
            if (tracker) bodySwing = new Pose.BodySwing(tracker);
            if (useImu) imu = GetComponent<ImuLink>() ?? gameObject.AddComponent<ImuLink>();
        }

        void OnDestroy() => (pitcher as AnimatedPitcher)?.Destroy();

        // ---------- Vistas ----------
        void Show(KioskView v)
        {
            view = v;
            hud.Show(v);
            cam.attract = v == KioskView.Idle || v == KioskView.Connected;
            if (v == KioskView.Calib) cam.Set(Shot.Calib);
            else if (v == KioskView.Play) cam.Set(Shot.Plate);
            else if (v == KioskView.Idle || v == KioskView.Connected) cam.Set(Shot.Attract);
        }

        KioskView PhoneView() => phoneStage == "calib" ? KioskView.Calib : phoneStage == "play" ? KioskView.Play : KioskView.Connected;

        void ResetPlayer()
        {
            phoneStage = "intro";
            localPlay = false;
            game.Stop();
            if (resultCo != null) StopCoroutine(resultCo);
            lastResult = null;
            hud.HideResult();
        }

        void StartLost()
        {
            Show(KioskView.Lost);
            if (lostCo != null) StopCoroutine(lostCo);
            lostCo = StartCoroutine(Lost());
        }

        IEnumerator Lost()
        {
            float until = Time.unscaledTime + controlGraceSeconds;
            while (true)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(until - Time.unscaledTime));
                hud.LostSecs(left);
                if (left == 0) { link.FreeControl(); ResetPlayer(); Show(KioskView.Idle); yield break; }
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        void StopLost() { if (lostCo != null) StopCoroutine(lostCo); lostCo = null; }

        // ---------- Conexión ----------
        void OnRoom(JObject m)
        {
            if (testWithoutPhone) return;   // modo prueba: sin sala ni QR
            string sala = (string)m["sala"];
            StartCoroutine(LoadQr(sala));
            StopLost();
            bool control = (bool?)m["control"] ?? false;
            if (!control) ResetPlayer();
            Show(control ? PhoneView() : KioskView.Idle);
        }

        IEnumerator LoadQr(string sala)
        {
            hud.Room(sala, null);
            using (var req = UnityWebRequestTexture.GetTexture(link.QrUrl(sala)))
            {
                req.certificateHandler = new KioskCertificate();
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) hud.Room(sala, DownloadHandlerTexture.GetContent(req));
                else Debug.LogWarning($"[Kiosco] QR: {req.error}");
            }
        }

        void OnPeer(JObject m)
        {
            if (testWithoutPhone) return;
            if ((bool?)m["on"] ?? false)
            {
                StopLost();
                if (!((bool?)m["again"] ?? false)) ResetPlayer();
                Show(PhoneView());
            }
            else if (view != KioskView.Idle) StartLost();
        }

        void OnMessage(JObject m)
        {
            switch ((string)m["t"])
            {
                case "st":
                    phoneStage = (string)m["v"] ?? "intro";
                    string mano = (string)m["mano"];
                    if (mano == "R" || mano == "L") SetHand(mano[0]);
                    if (phoneStage == "play")
                    {
                        link.Send(new { t = "cfg", swing = new { ROT_DPS = 600, ACC_MS2 = 25 } });
                        if (game.mode == GameMode.Idle) StartDerby();
                    }
                    if (view != KioskView.Lost && view != KioskView.Paused && view != KioskView.Idle) Show(PhoneView());
                    break;
                case "cal": hud.Toast("Calibrado", "", false, 0.9f); break;
                case "vis":
                    int v = (int?)m["v"] ?? 1;
                    if (v == 0 && view != KioskView.Idle && view != KioskView.Lost) Show(KioskView.Paused);
                    else if (v == 1 && view == KioskView.Paused) Show(PhoneView());
                    break;
                case "sw":
                {
                    if (view != KioskView.Play || game.mode == GameMode.Idle || game.mode == GameMode.Over) break;
                    double now = Now;
                    double? s = (double?)m["s"];
                    double ts = link.clock.Ready && s.HasValue ? link.clock.ToScreen(s.Value) : now - 40;
                    var input = new SwingInput { dir = (string)m["dir"], power = (double?)m["power"] ?? 0.7, lift = (double?)m["lift"] ?? 0 };
                    if (game.Swing(input, ts, now) != "ignored") batter.Swing((float)input.power);
                    break;
                }
                case "typing": ExtendResult(); break;
                case "alias": StartCoroutine(SaveScore((string)m["v"] ?? "")); break;
            }
        }

        void SetHand(char h) { batter.SetHand(h); battery.SetHand(h); game.hand = h; }

        // El bateador viste los colores del estadio; el equipo en el campo, de blanco con gorra del color principal.
        void Uniforms(Config.StadiumConfig c)
        {
            var white = new Color(0.92f, 0.92f, 0.94f);
            (batter as Batter)?.f.Recolor(c.Primary, white, c.Secondary);
            (batter as Pose.AvatarBatter)?.Recolor(c.Primary, c.Secondary);
            (pitcher as Pitcher)?.f.Recolor(white, white * 0.9f, c.Primary);
            (pitcher as AnimatedPitcher)?.Recolor(white, c.Primary);
            fielders.Recolor(white, white * 0.9f, c.Primary);
            battery.catcher.Recolor(white, white * 0.9f, c.Primary);
        }

        // ---------- Derby ----------
        void StartDerby()
        {
            if (resultCo != null) StopCoroutine(resultCo);
            lastResult = null;
            hud.HideResult();
            demo.Stop();
            ClearLanding();
            game.hand = batter.hand;
            game.Start(Now);
            branding.Board.Clear();
            cam.Set(Shot.Plate);
        }

        void OnGameEvent(GameEvent e, bool real)
        {
            switch (e.type)
            {
                case EventType.Derby: if (real) { hud.SetDerby(e.derby, game.mode == GameMode.Over); branding.Board.SetDerby(e.derby); } break;
                case EventType.Windup:
                    windupAt = Now;
                    ClearLanding(); fielders.Reset(); batter.Reset();
                    zoneDot.gameObject.SetActive(false);
                    battery.Target(e.pitch.tx, e.pitch.ty);
                    cam.Untrack();
                    cam.Set(Shot.Plate);
                    if (real)
                    {
                        hud.SetPitch(e.pitch.name, e.pitch.mph); hud.Hint("");
                        branding.Board.Status($"LANZAMIENTO {game.derby.pitch + 1} DE {game.derby.pitchesMax} · {e.pitch.name.ToUpper()} {e.pitch.mph:0} MPH");
                    }
                    break;
                case EventType.Pitch: trail.Clear(); trail.emitting = true; break;
                case EventType.Contact:
                    sound.Bat(e.r.quality, System.Math.Min(1, e.r.evMph / 110));
                    trail.Clear();
                    cam.Set(Shot.Flight);
                    if (e.outcome.kind != HitKind.Foul) fielders.Chase(e.outcome.landing.ToUnity());
                    if (!real) break;
                    link.Send(new { t = "hit", power = System.Math.Min(1, e.r.evMph / 110) });
                    hud.Statcast(e.r.evMph, e.r.launchDeg, e.outcome.distFt, e.outcome.hangS);
                    if (e.outcome.kind == HitKind.HomeRun) StartHomeRunReplay(e);
                    else StartCoroutine(Later(0.7f, () =>
                        hud.Toast(e.text, e.outcome.kind == HitKind.Foul ? "" : $"{e.outcome.distFt} pies", e.outcome.kind == HitKind.Foul, 1.8f)));
                    break;
                case EventType.HomeRun:
                    sound.Roar();
                    crowd.Cheer(5);
                    if (real) branding.Board.Flash("¡JONRÓN!", $"{e.outcome.distFt} PIES · {e.r.evMph:0} MPH");
                    cam.Set(Shot.HomeRun);
                    if (real)
                    {
                        hud.Toast("¡JONRÓN!", $"{e.outcome.distFt} pies · {e.r.evMph:0} mph", false, 3f);
                        link.Send(new { t = "point" });
                    }
                    break;
                case EventType.Landed:
                    if (e.outcome.kind != HitKind.Foul) ShowLanding(e.point.ToUnity(), e.outcome.distFt);
                    if (real) ReplayLanded(e.point.ToUnity());
                    break;
                case EventType.Call:
                    trail.emitting = false;
                    zoneDot.localPosition = new Vector3((float)e.point.x, (float)e.point.y, 0.01f);
                    zoneDot.gameObject.SetActive(true);
                    if (real)
                    {
                        hud.Toast(e.text, "", e.callKind == "whiff", 1.3f);
                        if (e.callKind == "whiff") link.Send(new { t = "miss" });
                    }
                    break;
                case EventType.Glove: sound.Glove(); break;
                case EventType.Over: if (real) ShowResult(e); break;
            }
        }

        IEnumerator Later(float s, System.Action a) { yield return new WaitForSecondsRealtime(s); a(); }

        // ---------- Resultado, alias y fin del turno ----------
        void ShowResult(GameEvent e)
        {
            var d = e.derby;
            lastResult = d;
            hud.Result(d, e.reason);
            branding.Board.Status(d.homeRuns == 1 ? "FIN DEL DERBY · 1 JONRÓN" : $"FIN DEL DERBY · {d.homeRuns} JONRONES");
            if (d.homeRuns > 0) { crowd.Cheer(3); branding.Board.Flash(d.homeRuns == 1 ? "1 JONRÓN" : $"{d.homeRuns} JONRONES", $"{d.totalFt} PIES EN TOTAL", 6); }
            string title = d.homeRuns == 1 ? "1 jonrón" : $"{d.homeRuns} jonrones";
            string lines = $"Distancia total: {d.totalFt} pies · Más largo: {d.longestFt} pies · Vel. máxima: {d.maxEvMph:0} mph";
            link.Send(new { t = "over", win = d.homeRuns > 0 ? 1 : 0, title, lines, reason = e.reason });
            link.Send(new { t = "match", win = d.homeRuns > 0 ? 1 : 0, games = new { P = d.homeRuns, A = 0 }, bestRally = d.longestFt, reason = string.IsNullOrEmpty(e.reason) ? "beisbol" : e.reason });
            resultUntil = Time.unscaledTime + resultSeconds;
            resultMax = Time.unscaledTime + resultMaxSeconds;
            if (resultCo != null) StopCoroutine(resultCo);
            resultCo = StartCoroutine(ResultTimer());
        }

        IEnumerator ResultTimer()
        {
            while (true)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(resultUntil - Time.unscaledTime));
                hud.ResultSecs(left);
                if (left == 0) { EndTurn(); yield break; }
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        void ExtendResult()
        {
            if (lastResult == null || !hud.ResultShown) return;
            resultUntil = Mathf.Min(resultMax, Mathf.Max(resultUntil, Time.unscaledTime + 12));
        }

        IEnumerator SaveScore(string alias)
        {
            if (lastResult == null) yield break;
            var d = lastResult;
            var body = new JObject { ["alias"] = alias, ["home_runs"] = d.homeRuns, ["total_ft"] = d.totalFt, ["longest_ft"] = d.longestFt, ["max_ev_mph"] = System.Math.Round(d.maxEvMph * 10) / 10 };
            using (var req = new UnityWebRequest($"{link.server.TrimEnd('/')}/api/beisbol/scores", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.certificateHandler = new KioskCertificate();
                yield return req.SendWebRequest();
                JObject res = null;
                try { res = JObject.Parse(req.downloadHandler.text); } catch { }
                if (req.result == UnityWebRequest.Result.ConnectionError || res == null)
                {
                    link.Send(new { t = "saved", ok = 0, msg = "No se pudo guardar. Intenta de nuevo." });
                    yield break;
                }
                if (req.responseCode >= 400)
                {
                    var detail = res["detail"];
                    string text = detail is JArray a ? (string)a[0]?["msg"] : (string)detail;
                    text = (text ?? "Alias no válido.").Replace("Value error, ", "");
                    bool pattern = text.ToLower().Contains("pattern") || text.ToLower().Contains("string");
                    link.Send(new { t = "saved", ok = 0, msg = pattern ? "Usa de 3 a 12 letras o números." : text });
                    yield break;
                }
                int rank = (int?)res["rank"] ?? 0;
                link.Send(new { t = "saved", ok = 1, rank });
                hud.ResultRank($"{alias}: lugar {rank} del ranking de hoy");
                resultUntil = Mathf.Min(resultUntil, Time.unscaledTime + 6);
            }
        }

        void EndTurn()
        {
            branding.Board.Clear();
            if (resultCo != null) StopCoroutine(resultCo);
            hud.HideResult();
            lastResult = null;
            bool wasLocal = localPlay;
            ResetPlayer();
            Show(KioskView.Idle);
            if (!wasLocal) link.Send(new { t = "renew" });
            if (testWithoutPhone) StartLocal();
        }

        // ---------- Atracción: la CPU batea sola ----------
        void RunDemo(float dt, double now)
        {
            if (demo.mode == GameMode.Idle || demo.mode == GameMode.Over) demo.Start(now);
            demo.lastSwingAt = now;
            var p = demo.pitch;
            if (p != null && p != demoHandled && demo.mode == GameMode.Pitch && now >= p.tContact - 60)
            {
                demoHandled = p;
                if (p.strike || Random.value < 0.3f)
                {
                    double err = Contact.Gauss(new System.Random()) * 45;
                    var s = new SwingInput { dir = "drive", power = 0.7 + Random.value * 0.3, lift = Random.value * 0.6 };
                    demo.Swing(s, p.tContact + err, now);
                    StartCoroutine(Later((float)System.Math.Max(0, (p.tContact + err - now) / 1000 - 0.12), () => batter.Swing((float)s.power)));
                }
            }
            demo.Update(dt, now);
        }

        // ---------- Bucle ----------
        void Update()
        {
            float dt = Mathf.Min(0.05f, Time.deltaTime);
            double now = Now;
            // Modo prueba: arranca en cuanto todo está listo (el videomarcador se crea en Start).
            if (testWithoutPhone && view == KioskView.Idle && branding.Board != null) StartLocal();
            DebugKeys(now);
            ImuCheck(now);
            BodySwingCheck(now);
            AutoBat(now);

            bool attract = view == KioskView.Idle || view == KioskView.Connected;
            var active = attract ? demo : game;
            if (view == KioskView.Play)
            {
                game.graceMs = System.Math.Max(DerbyGame.GRACE_MS, (link.clock.Avg ?? 0) / 2 + 120);
                if (game.mode != GameMode.Idle) game.Update(dt * ReplayTimeScale, now);
            }
            else if (attract) RunDemo(dt, now);
            else if (demo.mode != GameMode.Idle) demo.Stop();

            // Pitcher: el movimiento se sincroniza para soltar la bola en releaseAt.
            float total = (float)DerbyGame.WINDUP_MS / Pitcher.RELEASE_AT;
            float t = active.mode == GameMode.Windup || active.mode == GameMode.Pitch ? (float)((now - windupAt) / total) : active.mode == GameMode.Flight ? 1 : 0;
            pitcher.Pose(Mathf.Clamp01(t));
            if (batter is Pose.AvatarBatter imuBatter) imuBatter.BatDirection = imu && imu.Live ? imu.BatDirection : (Vector3?)null;
            batter.Update(dt);
            if (tracker)
            {
                fpsAvg = Mathf.Lerp(fpsAvg, 1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime), 0.05f);
                string info = tracker.Tracking ? $"{fpsAvg:0} fps · cámara {tracker.Fps:0}/s" : "Párate frente a la cámara";
                if (imu) info += imu.Live ? $" · IMU {imu.Hz:0}/s" : " · IMU sin datos";
                hud.Body(tracker.Camera, tracker.Tracking, view == KioskView.Play || view == KioskView.Calib || view == KioskView.Connected, info);
            }
            fielders.Update(dt);
            battery.Update();
            battery.ShowUmpire(!(view == KioskView.Play && (cam.mode == CamMode.Auto || cam.mode == CamMode.Bateo) && cam.shot == Shot.Plate));

            // Bola: en la mano del pitcher, lanzada o bateada.
            BallState b = active.mode == GameMode.Pitch ? active.ball : active.mode == GameMode.Flight ? active.batted?.ball : null;
            if (b != null)
            {
                ballT.gameObject.SetActive(true);
                ballT.position = b.p.ToUnity();
                if (active.mode == GameMode.Flight) cam.Track(ballT.position, b.v.ToUnity());
            }
            else if (active.mode == GameMode.Windup)
            {
                ballT.gameObject.SetActive(true);
                ballT.position = pitcher.HandPos;
                trail.emitting = false;
            }
            else ballT.gameObject.SetActive(false);

            // Lejos de la cámara la bola y su estela crecen para que se sigan leyendo.
            bool flying = active.mode == GameMode.Flight;
            float d = Vector3.Distance(cam.transform.position, ballT.position);
            ballT.localScale = Vector3.one * Mathf.Max(0.11f, d * 0.006f);
            trail.widthMultiplier = Mathf.Max(1, d * 0.05f) * (flying ? 1.6f : 1);
            trail.time = flying ? 2.2f : 0.35f;
            trail.startColor = flying ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(1, 1, 1, 0.6f);
            trail.endColor = new Color(trail.startColor.r, trail.startColor.g, trail.startColor.b, 0);

            UpdateReplay();

            // La zona de strike se ilumina cuando la bola llega.
            float glow = (float)active.HitZone(now);
            var zc = Color.Lerp(new Color(1, 1, 1, 0.35f), new Color(1f, 0.82f, 0.25f, 1), glow);
            zone.startColor = zone.endColor = zc;
            zone.gameObject.SetActive(view == KioskView.Play || view == KioskView.Calib);
        }

        void DebugKeys(double now)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.pKey.wasPressedThisFrame) StartLocal();
            if (kb.bKey.wasPressedThisFrame && imu && imu.Live) { imu.Center(); hud.Toast("Bate centrado", "Apunta al pitcher y presiona N", false, 2); }
            if (kb.nKey.wasPressedThisFrame && imu && imu.Live) { imu.AimAtPitcher(); hud.Toast("Bate alineado", "", false, 1.2f); }
            if (kb.cKey.wasPressedThisFrame && tracker) { var n = tracker.NextCamera(); if (n != null) hud.Toast("Cámara", n, false, 2); }
            if (kb.spaceKey.wasPressedThisFrame) LocalSwing(0.9, 0.25, now - 15);
            if (kb.escapeKey.wasPressedThisFrame && localPlay) EndTurn();
            if (kb.enterKey.wasPressedThisFrame && localPlay && game.mode == GameMode.Over) EndTurn();
        }

        // Sin teléfono: el swing lo detecta el cuerpo (velocidad de las muñecas).
        // Con IMU, el swing lo detecta el giro del bate.
        void ImuCheck(double now)
        {
            if (!imu || !imu.TakeSwing(out var hit, out var ts) || !localPlay) return;
            var r = LocalSwing(hit.power, hit.lift, ts);
            if (r != "ignored") hud.Hint($"Swing: {hit.dps:0} °/s · {(ts - (game.pitch?.tContact ?? ts)):+0;-0} ms");
        }

        void BodySwingCheck(double now)
        {
            if (bodySwing == null) return;
            var s = bodySwing.Update(batter.hand);
            if (imu && imu.Live) return;   // la IMU manda
            if (s == null || !localPlay) return;
            var r = LocalSwing(s.Value.power, s.Value.lift, now - s.Value.agoMs);
            if (r != "ignored") hud.Hint($"Swing: {s.Value.speed:0.0} m/s");
        }

        // Derby sin teléfono (teclado o pruebas).
        public void StartLocal()
        {
            if (view != KioskView.Idle && view != KioskView.Connected) return;
            localPlay = true; phoneStage = "play";
            Show(KioskView.Play);
            StartDerby();
        }

        // Swing local; ts en el reloj de la pantalla (por omisión, ahora).
        public string LocalSwing(double power = 0.9, double lift = 0.25, double? ts = null)
        {
            if (view != KioskView.Play || game.mode == GameMode.Idle) return "ignored";
            var s = new SwingInput { dir = game.hand == 'L' ? "reves" : "drive", power = power, lift = lift };
            var r = game.Swing(s, ts ?? Now, Now);
            if (r != "ignored") batter.Swing((float)power);
            return r;
        }

        [Header("Pruebas")]
        [Tooltip("Sin QR ni teléfono: el juego arranca solo y se batea con el cuerpo frente a la cámara.")]
        public bool testWithoutPhone = true;
        Pose.BodySwing bodySwing;
        [Tooltip("Lee el bate de la IMU (gateway de motion-controller-lab en ws://127.0.0.1:8765).")]
        public bool useImu = true;
        ImuLink imu;
        float fpsAvg = 60;
        [Tooltip("En un derby local, batea solo con este error de timing (ms, desviación). Negativo = apagado.")]
        public float autoBatErrorMs = -1;
        Pitch autoHandled;

        void AutoBat(double now)
        {
            var p = game.pitch;
            if (!localPlay || autoBatErrorMs < 0 || p == null || p == autoHandled || game.mode != GameMode.Pitch || now < p.tContact - 60) return;
            autoHandled = p;
            LocalSwing(0.95, 0.3, p.tContact + Contact.Gauss(new System.Random()) * autoBatErrorMs);
        }

        // ---------- Piezas de la escena ----------
        void BuildBall()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Bola";
            Destroy(ball.GetComponent<Collider>());
            ballT = ball.transform;
            // Un poco más grande que la real (7.3 cm) para que se lea en pantalla.
            ballT.localScale = Vector3.one * 0.11f;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.4f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.35f));
            ball.GetComponent<MeshRenderer>().sharedMaterial = m;
            trail = ball.AddComponent<TrailRenderer>();
            trail.time = 0.35f; trail.minVertexDistance = 0.05f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, 0.07f), new Keyframe(1, 0));
            // Sprites/Default respeta el color por vértice y la transparencia de la estela.
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, alphaKeys = new[] { new GradientAlphaKey(0.6f, 0), new GradientAlphaKey(0, 1) } };
            trail.emitting = false;
            ball.SetActive(false);

            landingT = new GameObject("Caída").transform;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(landingT, false);
            ring.transform.localScale = new Vector3(2.2f, 0.01f, 2.2f);
            var rm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            rm.SetColor("_BaseColor", new Color(1f, 0.82f, 0.25f));
            ring.GetComponent<MeshRenderer>().sharedMaterial = rm;
            var label = new GameObject("Distancia").AddComponent<TextMeshPro>();
            label.transform.SetParent(landingT, false);
            label.transform.localPosition = new Vector3(0, 3, 0);
            label.fontSize = 24; label.alignment = TextAlignmentOptions.Center; label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.25f; label.outlineColor = Color.black;
            label.rectTransform.sizeDelta = new Vector2(12, 4);
            landingText = label;
            landingT.gameObject.SetActive(false);
        }

        void BuildZone()
        {
            // Marco de la zona de strike sobre el home, visto desde la cámara del catcher.
            var go = new GameObject("Zona de strike");
            zone = go.AddComponent<LineRenderer>();
            zone.useWorldSpace = false; zone.loop = true; zone.positionCount = 4;
            float w = (float)BallPhysics.ZONE_HALF_W, lo = (float)BallPhysics.ZONE_LOW, hi = (float)BallPhysics.ZONE_HIGH;
            zone.SetPositions(new[] { new Vector3(-w, lo, 0), new Vector3(w, lo, 0), new Vector3(w, hi, 0), new Vector3(-w, hi, 0) });
            zone.widthMultiplier = 0.012f;
            zone.material = new Material(Shader.Find("Sprites/Default"));
            zoneDot = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            Destroy(zoneDot.GetComponent<Collider>());
            zoneDot.SetParent(go.transform, false);
            zoneDot.localScale = Vector3.one * 0.075f;
            var dm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            dm.SetColor("_BaseColor", new Color(1f, 0.3f, 0.2f));
            zoneDot.GetComponent<MeshRenderer>().sharedMaterial = dm;
            zoneDot.gameObject.SetActive(false);
        }

        void ShowLanding(Vector3 p, int ft)
        {
            landingT.position = new Vector3(p.x, 0.03f, p.z);
            landingText.text = $"{ft} FT";
            landingText.transform.rotation = Quaternion.LookRotation(landingT.position - cam.transform.position);
            landingT.gameObject.SetActive(true);
            cam.Landing(p);
        }

        void ClearLanding() => landingT.gameObject.SetActive(false);
    }

    // El servidor del kiosco usa un certificado autofirmado (app/certs.py).
    class KioskCertificate : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }
}
