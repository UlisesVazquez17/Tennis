// Pitcher con un personaje animado (Mixamo u otro con rig humanoide). La animación se
// recorre a mano, sincronizada con el juego: suelta la bola exactamente cuando la física
// la lanza (releaseAt del lanzamiento) y en el punto de salida de la física.

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Baseball.View
{
    public interface IPitcher
    {
        void Pose(float t);          // t: 0 = de pie, Pitcher.RELEASE_AT = suelta, 1 = terminó
        Vector3 HandPos { get; }
    }

    public class AnimatedPitcher : IPitcher
    {
        readonly Transform root, hand;
        readonly AnimationClip clip;
        readonly float release;        // segundo del clip en que suelta la bola
        PlayableGraph graph;
        AnimationClipPlayable playable;
        bool placed;

        public AnimatedPitcher(Transform parent, GameObject prefab, AnimationClip clip, float releaseSeconds)
        {
            this.clip = clip; release = releaseSeconds;
            var go = Object.Instantiate(prefab, parent);
            go.name = "Pitcher (animado)";
            root = go.transform;
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.applyRootMotion = false;
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand) ?? root;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;

            graph = PlayableGraph.Create("Pitcher");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(true);
            AnimationPlayableOutput.Create(graph, "Animación", animator).SetSourcePlayable(playable);

            root.SetPositionAndRotation(new Vector3(0, 0.25f, (float)BallPhysics.MOUND_Z), Quaternion.Euler(0, 180, 0));
        }

        // Mira al home sobre el montículo y se corre para que la mano, al soltar, quede donde
        // la física suelta la bola. Se mide con SampleAnimation, que mueve el esqueleto al
        // instante (el grafo escribe la pose más tarde en el cuadro).
        void Place()
        {
            placed = true;
            clip.SampleAnimation(root.gameObject, release);
            var h = hand.position;
            root.position += new Vector3(-0.55f - h.x, 0, (float)BallPhysics.RELEASE_Z - h.z);
        }

        void Sample(float seconds)
        {
            playable.SetTime(Mathf.Clamp(seconds, 0, clip.length));
            graph.Evaluate(0);
        }

        public void Pose(float t)
        {
            if (!placed) Place();
            // El juego tarda WINDUP_MS en soltar; el clip arranca esos mismos segundos antes de su suelta.
            float total = (float)(DerbyGame.WINDUP_MS / 1000.0) / Pitcher.RELEASE_AT;
            float start = release - (float)(DerbyGame.WINDUP_MS / 1000.0);
            Sample(start + Mathf.Clamp01(t) * total);
        }

        public Vector3 HandPos => hand.position + hand.up * -0.06f;

        // Colores del uniforme: el maniquí de Mixamo trae una malla para el cuerpo
        // (Beta_Surface) y otra para las articulaciones (Beta_Joints).
        public void Recolor(Color body, Color joints)
        {
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                foreach (var m in r.materials)
                    m.SetColor("_BaseColor", r.name.Contains("Joint") ? joints : body);
        }

        public void Destroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
