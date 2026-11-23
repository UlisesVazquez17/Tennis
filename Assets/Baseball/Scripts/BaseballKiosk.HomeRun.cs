// Repetición épica del jonrón, al estilo de las transmisiones de los juegos de consola:
//
// 1. Héroe (≈2.6 s): el tiempo casi se detiene. La bola sale del bate a paso de tortuga,
//    la pose del bateador se congela en el contacto, el bate sale volando (bat flip) y la
//    cámara gira alrededor de él, de las manos a la cara, con el fondo desenfocado.
// 2. Persecución: tiempo real, la cámara sigue la bola y un contador de pies sube junto a ella.
// 3. Aterrizaje: toma abierta al cruzar la barda (la del jonrón de siempre) y la distancia
//    final en grande donde cae, hasta el siguiente lanzamiento.
//
// Durante la repetición el reloj del derby se detiene (DerbyGame.Delay) y la pantalla pasa
// a modo cine: franjas negras y sin marcador.

using Baseball.View;
using UnityEngine;

namespace Baseball
{
    public partial class BaseballKiosk
    {
        [Header("Repetición del jonrón")]
        public bool homeRunReplay = true;
        [Tooltip("Segundos de la toma héroe en cámara lenta.")]
        public float heroSeconds = 2.6f;
        [Tooltip("Velocidad de la bola durante la toma héroe (1 = tiempo real).")]
        [Range(0.01f, 0.2f)] public float heroBallSpeed = 0.035f;

        enum Replay { None, Hero, Chase }
        Replay replay;
        float replayStart;
        Vector3? replayLanding;
        int replayFeet;
        Camera replayCam;

        // El juego avanza a esta velocidad (la bola casi detenida durante la toma héroe).
        float ReplayTimeScale => replay == Replay.Hero ? heroBallSpeed : 1;

        void StartHomeRunReplay(GameEvent e)
        {
            if (!homeRunReplay) return;
            replay = Replay.Hero;
            replayStart = Time.unscaledTime;
            replayLanding = null;
            replayFeet = e.outcome.distFt;
            game.Delay(heroSeconds * 1000);
            hud.SetCinematic(true);
            hud.Hint("");
            crowd.Cheer(heroSeconds + 2);

            if (batter is Pose.AvatarBatter av)
            {
                av.Frozen = true;
                av.flipTimeScale = 0.3f;
                cam.Hero(() => av.HandsPos, () => av.HeadPos, av.Facing, heroSeconds);
                StartCoroutine(Later(heroSeconds * 0.3f, () => { if (replay == Replay.Hero) av.BatFlip(); }));
            }
            else
            {
                var box = new Vector3(batter.hand == 'L' ? 0.85f : -0.85f, 0, 0.2f);
                cam.Hero(() => box + Vector3.up * 1.1f, () => box + Vector3.up * 1.65f, batter.hand == 'L' ? Vector3.left : Vector3.right, heroSeconds);
            }
        }

        void ReplayLanded(Vector3 p) { if (replay != Replay.None) replayLanding = p; }

        void UpdateReplay()
        {
            if (replay == Replay.None) return;
            if (!replayCam) replayCam = cam.GetComponent<Camera>();

            if (replay == Replay.Hero && Time.unscaledTime - replayStart >= heroSeconds)
            {
                replay = Replay.Chase;
                cam.EndHero();
                cam.Set(Shot.Flight);
                if (batter is Pose.AvatarBatter av) av.flipTimeScale = 1;
            }

            if (replay == Replay.Chase)
            {
                if (replayLanding.HasValue)
                {
                    var sp = replayCam.WorldToScreenPoint(replayLanding.Value + Vector3.up * 2);
                    hud.DistanceTag(sp.z > 0, sp, replayFeet.ToString(), true);
                }
                else if (ballT.gameObject.activeSelf)
                {
                    var p = ballT.position;
                    float ft = new Vector2(p.x, p.z).magnitude / (float)BallPhysics.FT;
                    var sp = replayCam.WorldToScreenPoint(p);
                    hud.DistanceTag(sp.z > 0, sp, $"{ft:0} FT");
                }
                // El derby pasó al siguiente lanzamiento: se acabó la repetición.
                if (game.mode != GameMode.Flight) EndReplay();
            }
        }

        void EndReplay()
        {
            if (replay == Replay.None) return;
            replay = Replay.None;
            cam.EndHero();
            hud.SetCinematic(false);
            if (batter is Pose.AvatarBatter av) { av.Frozen = false; av.flipTimeScale = 1; }
        }
    }
}
