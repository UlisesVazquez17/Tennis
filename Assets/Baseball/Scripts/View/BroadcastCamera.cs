// Cámaras de transmisión.
//
// El juego pide tomas (Shot): bateo, seguir la bola, jonrón, calibrar, atracción. El
// operador elige además un modo (CamMode) desde el panel:
//   Auto         el juego decide (bateo al batear; Recorrido mientras nadie juega)
//   Bateo        detrás de home, la vista del tótem
//   Transmisión  teleobjetivo desde el jardín central
//   PrimeraBase  desde arriba de la grada de primera
//   Aérea        alta, sobre el cuadro
//   Dirigible    desde la góndola del primer dirigible
//   Libre        se mueve con WASD/QE y el botón derecho del mouse
//   Recorrido    va alternando las anteriores (modo de atracción)
// La bola bateada siempre se sigue, salvo en Libre.

using UnityEngine;
using UnityEngine.InputSystem;

namespace Baseball.View
{
    public enum Shot { Attract, Plate, Flight, HomeRun, Calib }
    public enum CamMode { Auto, Bateo, Transmision, PrimeraBase, Aerea, Dirigible, Libre, Recorrido }

    [RequireComponent(typeof(Camera))]
    public class BroadcastCamera : MonoBehaviour
    {
        public Shot shot = Shot.Attract;
        public CamMode mode = CamMode.Auto;
        public Blimps blimps;
        public float blendSeconds = 0.9f;
        public float tourSeconds = 7;
        [Tooltip("Lo pone el kiosco mientras nadie juega: con una cámara fija elegida, la demo no la interrumpe.")]
        public bool attract;

        static readonly CamMode[] Tour = { CamMode.Transmision, CamMode.Aerea, CamMode.PrimeraBase, CamMode.Dirigible, CamMode.Bateo };

        Camera cam;
        Vector3 pos, look; float fov;
        Vector3 fromPos, fromLook; float fromFov, blend = 1;
        string current;
        Vector3 ballPos, ballVel, landing;
        bool hasBall;
        float tourStart;
        Vector3 freePos; float freeYaw, freePitch;

        // Toma héroe: órbita lenta y cerrada alrededor del bateador, de las manos a la cara.
        System.Func<Vector3> heroFrom, heroTo; Vector3 heroFacing; float heroStart, heroSeconds; bool hero, cutNext;
        UnityEngine.Rendering.Universal.DepthOfField dof;
        public bool InHero => hero;

        void Awake()
        {
            cam = GetComponent<Camera>();
            Target(Effective(), out pos, out look, out fov);
            fromPos = pos; fromLook = look; fromFov = fov;
        }

        public void Set(Shot s) => shot = s;

        public void SetMode(CamMode m)
        {
            if (m == CamMode.Libre && mode != CamMode.Libre)
            {
                freePos = transform.position;
                var e = transform.rotation.eulerAngles; freeYaw = e.y; freePitch = e.x > 180 ? e.x - 360 : e.x;
            }
            if (m == CamMode.Recorrido) tourStart = Time.unscaledTime;
            mode = m;
        }

        // Cámara libre en una posición dada (panel, pruebas).
        public void Teleport(Vector3 p, Vector3 lookAt, float fieldOfView = 50)
        {
            SetMode(CamMode.Libre);
            freePos = p;
            var e = Quaternion.LookRotation(lookAt - p).eulerAngles; freeYaw = e.y; freePitch = e.x > 180 ? e.x - 360 : e.x;
            cam.fieldOfView = fieldOfView;
        }

        public void Hero(System.Func<Vector3> from, System.Func<Vector3> to, Vector3 facing, float seconds)
        {
            heroFrom = from; heroTo = to; heroFacing = facing; heroSeconds = seconds;
            heroStart = Time.unscaledTime; hero = true; cutNext = true;
            SetFocus(true);
        }

        public void EndHero() { hero = false; cutNext = true; SetFocus(false); }

        // Profundidad de campo solo durante la toma héroe: el fondo se desenfoca.
        void SetFocus(bool on)
        {
            if (dof == null)
            {
                var vol = FindAnyObjectByType<UnityEngine.Rendering.Volume>();
                if (!vol) return;
                var profile = vol.profile;   // copia de la escena, no el archivo
                if (!profile.TryGet(out dof)) dof = profile.Add<UnityEngine.Rendering.Universal.DepthOfField>(true);
                dof.mode.Override(UnityEngine.Rendering.Universal.DepthOfFieldMode.Bokeh);
                dof.focalLength.Override(85); dof.aperture.Override(2.2f);
            }
            dof.active = on;
        }

        public void Track(Vector3 p, Vector3 v) { ballPos = p; ballVel = v; hasBall = true; }
        public void Untrack() => hasBall = false;
        public void Landing(Vector3 p) => landing = p;

        // Qué toma se ve de verdad, según la toma que pide el juego y el modo del operador.
        string Effective()
        {
            if (hero) return "Hero";
            if ((shot == Shot.Flight || shot == Shot.HomeRun) && mode != CamMode.Libre && (!attract || mode == CamMode.Auto)) return shot.ToString();
            switch (mode)
            {
                case CamMode.Auto:
                    if (shot == Shot.Attract) return TourPick();
                    return shot == Shot.Plate ? nameof(CamMode.Bateo) : shot.ToString();
                case CamMode.Recorrido: return TourPick();
                default: return mode.ToString();
            }
        }

        string TourPick()
        {
            int i = Mathf.FloorToInt((Time.unscaledTime - tourStart) / tourSeconds) % Tour.Length;
            return Tour[i].ToString();
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var key = Effective();
            if (key != current)
            {
                fromPos = transform.position; fromLook = transform.position + transform.forward * 20; fromFov = cam.fieldOfView;
                blend = current == null || cutNext ? 1 : 0;
                cutNext = false;
                // Las tomas fijas entran con un corte suave; seguir la bola, de inmediato.
                Target(key, out pos, out look, out fov);
                current = key;
            }
            if (key == nameof(CamMode.Libre)) { FreeFly(dt); return; }

            Target(key, out var tp, out var tl, out var tf);
            bool moving = key == nameof(Shot.Flight) || key == nameof(Shot.HomeRun);
            if (key == "Hero") { pos = tp; look = tl; fov = tf; blend = 1; if (dof != null) dof.focusDistance.Override(Vector3.Distance(tp, tl)); }
            float follow = moving ? 1 - Mathf.Exp(-dt * 4) : 1;
            pos = Vector3.Lerp(pos, tp, follow); look = Vector3.Lerp(look, tl, follow); fov = Mathf.Lerp(fov, tf, follow);
            blend = Mathf.Min(1, blend + dt / blendSeconds);
            float k = Mathf.SmoothStep(0, 1, blend);
            var p = Vector3.Lerp(fromPos, pos, k); var l = Vector3.Lerp(fromLook, look, k);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(l - p));
            cam.fieldOfView = Mathf.Lerp(fromFov, fov, k);
        }

        void FreeFly(float dt)
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                var d = mouse.delta.ReadValue() * 0.15f;
                freeYaw += d.x; freePitch = Mathf.Clamp(freePitch - d.y, -85, 85);
            }
            var rot = Quaternion.Euler(freePitch, freeYaw, 0);
            if (kb != null)
            {
                var move = Vector3.zero;
                if (kb.wKey.isPressed) move += Vector3.forward; if (kb.sKey.isPressed) move += Vector3.back;
                if (kb.aKey.isPressed) move += Vector3.left; if (kb.dKey.isPressed) move += Vector3.right;
                if (kb.eKey.isPressed) move += Vector3.up; if (kb.qKey.isPressed) move += Vector3.down;
                float speed = kb.leftShiftKey.isPressed ? 40 : 12;
                freePos += rot * move * speed * dt;
            }
            if (mouse != null) cam.fieldOfView = Mathf.Clamp(cam.fieldOfView - mouse.scroll.ReadValue().y * 0.02f, 10, 80);
            transform.SetPositionAndRotation(freePos, rot);
        }

        void Target(string key, out Vector3 p, out Vector3 l, out float f)
        {
            switch (key)
            {
                case nameof(CamMode.Bateo):
                    // Como en los juegos de consola: detrás del home, por encima del catcher, con
                    // el bateador grande a un lado y el pitcher al fondo.
                    p = new Vector3(0.1f, 1.3f, -4.0f); l = new Vector3(-0.45f, 1.05f, 10f); f = 52; return;
                case nameof(CamMode.Transmision):
                    // La clásica del central: teleobjetivo, un poco a la izquierda del pitcher.
                    p = new Vector3(-2.6f, 9.5f, 128); l = new Vector3(0.1f, 1.0f, 0); f = 4.2f; return;
                case nameof(CamMode.PrimeraBase):
                    p = new Vector3(34, 13, 10); l = new Vector3(-2, 0.8f, 8); f = 30; return;
                case nameof(CamMode.Aerea):
                {
                    float a = Time.unscaledTime * 0.03f;
                    p = new Vector3(Mathf.Sin(a) * 25, 95, -35 + Mathf.Cos(a) * 10); l = new Vector3(0, 0, 42); f = 55; return;
                }
                case nameof(CamMode.Dirigible):
                {
                    if (!blimps) blimps = FindAnyObjectByType<Blimps>();
                    var ship = blimps ? blimps.Ship(0) : null;
                    if (ship) { p = ship.position + Vector3.down * 4.5f; l = new Vector3(0, 0, 40); f = 32; return; }
                    goto case nameof(CamMode.Aerea);
                }
                case "Hero":
                {
                    float t = Mathf.Clamp01((Time.unscaledTime - heroStart) / Mathf.Max(0.1f, heroSeconds));
                    float k = Mathf.SmoothStep(0, 1, t);
                    var focus = Vector3.Lerp(heroFrom(), heroTo(), Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.15f, 0.85f, t)));
                    // Órbita por el frente del bateador: de abajo y del lado del catcher hacia el del pitcher.
                    var fwd = heroFacing.sqrMagnitude > 0.01f ? heroFacing.normalized : Vector3.right;
                    var side = Vector3.Cross(Vector3.up, fwd);
                    float a = Mathf.Lerp(-55, 70, k) * Mathf.Deg2Rad;
                    var dir = fwd * Mathf.Cos(a) + side * Mathf.Sin(a);
                    p = focus + dir * Mathf.Lerp(0.95f, 2.0f, k) + Vector3.up * Mathf.Lerp(-0.3f, 0.12f, k);
                    l = focus;
                    f = Mathf.Lerp(40, 30, k); return;
                }
                case nameof(Shot.Calib):
                    p = new Vector3(2.8f, 1.5f, 3.2f); l = new Vector3(-0.6f, 1.1f, 0); f = 40; return;
                case nameof(Shot.Flight):
                case nameof(Shot.HomeRun):
                {
                    // Corre detrás de la bola, a medio camino y por encima, con la bola al centro.
                    // En el jonrón se abre para que entren la barda y las gradas.
                    var b = hasBall ? ballPos : landing;
                    var ground = new Vector3(b.x, 0, b.z);
                    var dir = (ground + new Vector3(ballVel.x, 0, ballVel.z) * 0.3f).normalized;
                    if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                    bool hr = key == nameof(Shot.HomeRun);
                    p = ground * (hr ? 0.5f : 0.55f) - dir * (hr ? 16 : 11) + Vector3.up * (5 + b.y * (hr ? 0.65f : 0.5f));
                    l = b + Vector3.down * (hr ? 3 : 1);
                    f = hr ? 55 : 46; return;
                }
                default:
                    p = new Vector3(0.3f, 3.5f, -8.5f); l = new Vector3(0, 0.9f, 8.5f); f = 27; return;
            }
        }
    }
}
