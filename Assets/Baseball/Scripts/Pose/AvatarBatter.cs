// Bateador que copia el cuerpo de la persona real: toma los 33 puntos de BodyTracker y
// gira los huesos de un personaje humanoide (Mixamo u otro) para que apunten igual.
//
// Cada hueso guarda, en la pose de enlace (T-pose), hacia dónde apunta respecto a su hijo;
// en cada cuadro se gira lo justo para apuntar como el segmento de la persona. La cadera y
// el pecho se orientan con la línea de cadera/hombros y la columna. Lo que la cámara no ve
// (piernas, si solo se ve medio cuerpo) toma una postura de bateo.
//
// La persona mira a la cámara; el bateador mira al home desde su caja. Así, girar el torso
// hacia su izquierda frente a la pantalla es hacer el swing hacia el pitcher.

using System.Collections.Generic;
using System.Linq;
using Baseball.View;
using UnityEngine;

namespace Baseball.Pose
{
    public class AvatarBatter : IBatter
    {
        public char hand { get; private set; } = 'R';
        public bool Live => tracker && tracker.Tracking;

        // Repetición del jonrón: la pose se congela y el bate sale volando (bat flip).
        public bool Frozen;
        [Tooltip("Velocidad del bat flip (1 = tiempo real; menos = cámara lenta).")]
        public float flipTimeScale = 1;
        bool flipping; Vector3 flipVel, flipSpin;

        public Vector3 HandsPos => (B(HumanBodyBones.RightHand).position + B(HumanBodyBones.LeftHand).position) * 0.5f;
        public Vector3 HeadPos => B(HumanBodyBones.Head).position + Vector3.up * 0.06f;
        public Vector3 ChestPos => (B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest)).position;
        public Vector3 Facing => root.forward;

        readonly BodyTracker tracker;
        readonly Transform root, bat;
        readonly Animator animator;
        readonly AnimationClip restClip;

        struct Bone { public Transform t; public Transform child; public Vector3 bindDir; public Quaternion bindRot; }
        readonly Dictionary<HumanBodyBones, Bone> bones = new Dictionary<HumanBodyBones, Bone>();
        Quaternion hipsBind, chestBind, headBind, spineBind, hipsFrameBind, chestFrameBind;
        Vector3 hipsBindLocal; float legLength, footBindY;
        readonly List<Renderer> renderers = new List<Renderer>();
        Material batMat, helmetMat;
        Vector3 boxPos; Quaternion boxRot = Quaternion.identity;   // lugar en la caja de bateo

        // La cámara detecta ~15–30 veces por segundo y el juego dibuja 60: entre detecciones,
        // cada punto sigue su velocidad (predicción corta) y se acerca suave a ese objetivo.
        [Tooltip("Qué tan rápido alcanza el avatar a la persona (1/s). Más alto = menos retraso, más temblor.")]
        public float followRate = 28;
        [Tooltip("Hasta cuántos segundos se predice el movimiento entre detecciones.")]
        public float maxPredict = 0.06f;
        readonly Vector3[] smooth = new Vector3[BodyTracker.Points], prevSample = new Vector3[BodyTracker.Points], velocity = new Vector3[BodyTracker.Points];
        int seenSamples = -1; float prevSampleTime; bool smoothReady;

        public AvatarBatter(Transform parent, GameObject prefab, BodyTracker tracker, AnimationClip restClip = null)
        {
            this.tracker = tracker; this.restClip = restClip;
            var go = Object.Instantiate(prefab, parent);
            go.name = "Bateador (cuerpo real)";
            root = go.transform;
            animator = go.GetComponent<Animator>();
            animator.enabled = false;   // los huesos los mueve este código
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) { r.updateWhenOffscreen = true; renderers.Add(r); }

            // Pose de enlace (T-pose del FBX), medida en el marco de la raíz.
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Map(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm); Map(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            Map(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm); Map(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            Map(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg); Map(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            Map(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg); Map(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
            Map(HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal); Map(HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal);
            Map(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes); Map(HumanBodyBones.RightFoot, HumanBodyBones.RightToes);

            var hips = B(HumanBodyBones.Hips); var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest); var head = B(HumanBodyBones.Head);
            hipsBind = hips.rotation; chestBind = chest.rotation; headBind = head.rotation;
            var spineT = B(HumanBodyBones.Spine); spineBind = spineT ? spineT.rotation : Quaternion.identity;
            hipsBindLocal = hips.position;
            hipsFrameBind = Frame(B(HumanBodyBones.RightUpperLeg).position - B(HumanBodyBones.LeftUpperLeg).position, chest.position - hips.position);
            chestFrameBind = Frame(B(HumanBodyBones.RightUpperArm).position - B(HumanBodyBones.LeftUpperArm).position, head.position - chest.position);
            legLength = Vector3.Distance(B(HumanBodyBones.LeftUpperLeg).position, B(HumanBodyBones.LeftLowerLeg).position)
                      + Vector3.Distance(B(HumanBodyBones.LeftLowerLeg).position, B(HumanBodyBones.LeftFoot).position);
            footBindY = Mathf.Min(B(HumanBodyBones.LeftFoot).position.y, B(HumanBodyBones.RightFoot).position.y);

            bat = BuildBat();
            BuildHelmet(head);
            SetHand('R');
        }

        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);

        void Map(HumanBodyBones bone, HumanBodyBones child)
        {
            Transform t = B(bone), c = B(child);
            if (!t || !c) return;
            bones[bone] = new Bone { t = t, child = c, bindDir = (c.position - t.position).normalized, bindRot = t.rotation };
        }

        // Marco ortonormal a partir de la dirección "derecha" y "arriba" de un segmento del cuerpo.
        static Quaternion Frame(Vector3 right, Vector3 up)
        {
            var fwd = Vector3.Cross(right, up);
            return fwd.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(fwd, up);
        }

        Transform BuildBat()
        {
            var t = new GameObject("Bate").transform;
            t.SetParent(root.parent, false);
            batMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            batMat.SetColor("_BaseColor", new Color(0.74f, 0.55f, 0.33f)); batMat.SetFloat("_Smoothness", 0.7f);
            var grip = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            grip.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.09f)); grip.SetFloat("_Smoothness", 0.2f);
            // Bate de 0.86 m a lo largo de +z: perilla, mango con cinta, cono y barril redondeado.
            var go = new GameObject("Madera", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(t, false);
            go.GetComponent<MeshFilter>().sharedMesh = BatMesh();
            go.GetComponent<MeshRenderer>().sharedMaterial = batMat;
            var tape = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(tape.GetComponent<Collider>());
            tape.transform.SetParent(t, false);
            tape.transform.localPosition = new Vector3(0, 0, 0.11f); tape.transform.localRotation = Quaternion.Euler(90, 0, 0);
            tape.transform.localScale = new Vector3(0.0275f, 0.09f, 0.0275f);
            tape.GetComponent<MeshRenderer>().sharedMaterial = grip;
            return t;
        }

        // Perfil de un bate real (distancia desde la perilla, radio; en metros), girado.
        static Mesh BatMesh()
        {
            float[,] profile = { { 0, 0 }, { 0.005f, 0.021f }, { 0.018f, 0.021f }, { 0.024f, 0.012f }, { 0.25f, 0.0125f }, { 0.42f, 0.016f }, { 0.58f, 0.028f }, { 0.68f, 0.032f }, { 0.84f, 0.033f }, { 0.855f, 0.026f }, { 0.86f, 0 } };
            const int sides = 24;
            int rings = profile.GetLength(0);
            var v = new Vector3[rings * (sides + 1)]; var tri = new int[(rings - 1) * sides * 6];
            for (int r = 0; r < rings; r++)
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2 / sides;
                    v[r * (sides + 1) + s] = new Vector3(Mathf.Cos(a) * profile[r, 1], Mathf.Sin(a) * profile[r, 1], profile[r, 0]);
                }
            int k = 0;
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = r * (sides + 1) + s, b = a + sides + 1;
                    tri[k++] = a; tri[k++] = a + 1; tri[k++] = b; tri[k++] = a + 1; tri[k++] = b + 1; tri[k++] = b;
                }
            var m = new Mesh { vertices = v, triangles = tri };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // Casco de bateo del color del estadio, pegado a la cabeza. Con casco, el cabello se oculta.
        void BuildHelmet(Transform head)
        {
            var top = animator.transform.GetComponentsInChildren<Transform>().FirstOrDefault(x => x.name.EndsWith("HeadTop_End"));
            float r = top ? Vector3.Distance(head.position, top.position) * 0.5f : 0.1f;
            // Arriba y un poco atrás: cubre el cráneo y deja la cara libre.
            var center = (top ? Vector3.Lerp(head.position, top.position, 0.8f) : head.position + Vector3.up * 0.12f) - Vector3.forward * 0.18f * r;
            helmetMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            helmetMat.SetColor("_BaseColor", new Color(0.04f, 0.16f, 0.44f)); helmetMat.SetFloat("_Smoothness", 0.85f);
            var h = new GameObject("Casco").transform;
            h.SetParent(head, true);
            h.position = center; h.rotation = root.rotation;
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(shell.GetComponent<Collider>());
            shell.transform.SetParent(h, false);
            shell.transform.localScale = new Vector3(1.9f * r, 1.6f * r, 2.15f * r);
            shell.GetComponent<MeshRenderer>().sharedMaterial = helmetMat;
            var brim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(brim.GetComponent<Collider>());
            brim.transform.SetParent(h, false);
            brim.transform.localPosition = new Vector3(0, -0.3f * r, 1.05f * r);
            brim.transform.localRotation = Quaternion.Euler(-12, 0, 0);
            brim.transform.localScale = new Vector3(1.4f * r, 0.08f * r, 0.6f * r);
            brim.GetComponent<MeshRenderer>().sharedMaterial = helmetMat;
            foreach (var rend in renderers) if (rend.name.ToLower().Contains("hair")) rend.enabled = false;
        }

        public void SetHand(char h)
        {
            hand = h;
            // El derecho se para del lado de tercera (x < 0) mirando al home (+x).
            float side = h == 'L' ? 1 : -1;
            boxPos = new Vector3(side * 0.85f, 0, 0.2f); boxRot = Quaternion.Euler(0, h == 'L' ? -90 : 90, 0);
            root.SetPositionAndRotation(boxPos, boxRot);
        }

        // Maniquí de Mixamo (Beta_*): se pinta con los colores del estadio. Un personaje con
        // texturas conserva su ropa; solo el casco toma el color principal.
        public void Recolor(Color jersey, Color joints)
        {
            if (helmetMat) helmetMat.SetColor("_BaseColor", jersey);
            foreach (var r in renderers)
                if (r.name.StartsWith("Beta_"))
                    foreach (var m in r.materials) m.SetColor("_BaseColor", r.name.Contains("Joint") ? joints : jersey);
        }

        // El cuerpo lo pone la persona; estos no hacen nada.
        public void Swing(float power) { }

        // Nuevo lanzamiento: el bate regresa a las manos.
        public void Reset() { flipping = false; Frozen = false; }

        // Suelta el bate girando hacia el lado de primera, como en un bat flip.
        public void BatFlip()
        {
            flipping = true;
            flipVel = Vector3.up * 5.2f + root.forward * 1.2f + root.right * -1.6f;
            flipSpin = (root.right * 760f) + (root.up * 180f);
        }

        public void Update(float dt)
        {
            if (flipping) FlipStep(dt * flipTimeScale);
            if (Frozen) return;
            if (Live) { Follow(dt); Retarget(); }
            else { Rest(); smoothReady = false; }
        }

        void FlipStep(float dt)
        {
            if (bat.position.y <= 0.035f && flipVel.y < 0)
            {
                // En el piso: se queda acostado.
                var flat = Vector3.ProjectOnPlane(bat.forward, Vector3.up);
                if (flat.sqrMagnitude > 1e-4f) bat.rotation = Quaternion.Slerp(bat.rotation, Quaternion.LookRotation(flat), dt * 10);
                bat.position = new Vector3(bat.position.x, 0.035f, bat.position.z);
                return;
            }
            flipVel += Vector3.down * 9.81f * dt;
            bat.position += flipVel * dt;
            bat.rotation = Quaternion.Euler(flipSpin * dt) * bat.rotation;
        }

        void Follow(float dt)
        {
            var w = tracker.World;
            if (tracker.Samples != seenSamples)
            {
                float sdt = tracker.SampleTime - prevSampleTime;
                for (int i = 0; i < BodyTracker.Points; i++)
                {
                    // Velocidad suavizada: un brinco del detector no dispara la predicción.
                    var v = smoothReady && sdt > 1e-3f && sdt < 0.2f ? (w[i] - prevSample[i]) / sdt : Vector3.zero;
                    velocity[i] = Vector3.Lerp(velocity[i], v, 0.6f);
                    prevSample[i] = w[i];
                }
                prevSampleTime = tracker.SampleTime;
                seenSamples = tracker.Samples;
                if (!smoothReady) { System.Array.Copy(w, smooth, BodyTracker.Points); smoothReady = true; }
            }
            float ahead = Mathf.Clamp(Time.realtimeSinceStartup - tracker.SampleTime, 0, maxPredict);
            float k = 1 - Mathf.Exp(-followRate * dt);
            for (int i = 0; i < BodyTracker.Points; i++) smooth[i] = Vector3.Lerp(smooth[i], w[i] + velocity[i] * ahead, k);
        }

        // Sin persona frente a la cámara: postura de espera.
        void Rest()
        {
            if (restClip)
            {
                animator.enabled = true;
                restClip.SampleAnimation(root.gameObject, 0);
                animator.enabled = false;
                // Muestrear un clip humanoide mueve la raíz al origen del clip: se regresa a la caja.
                root.SetPositionAndRotation(boxPos, boxRot);
            }
            PlaceBat(B(HumanBodyBones.RightHand).position, B(HumanBodyBones.LeftHand).position, root.rotation * new Vector3(-0.3f, 1f, -0.4f));
        }

        // Punto de la persona en el mundo del juego. La pantalla es la ventana al campo: lo que
        // está hacia la cámara es hacia el pitcher (+z) y la derecha de quien juega es +x.
        // Así, pararse de lado con el hombro izquierdo hacia la pantalla es la postura de un
        // bateador derecho, y el avatar queda igual frente al home.
        Vector3 P(int i) { var p = smoothReady ? smooth[i] : tracker.World[i]; return new Vector3(-p.x, p.y, -p.z); }
        bool Seen(int i) => tracker.Visibility[i] > 0.5f;

        void Retarget()
        {
            root.SetPositionAndRotation(boxPos, boxRot);
            var hips = B(HumanBodyBones.Hips);
            var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
            var spine = B(HumanBodyBones.Spine);
            var head = B(HumanBodyBones.Head);

            Vector3 lHip = P(BodyTracker.LHip), rHip = P(BodyTracker.RHip), lSh = P(BodyTracker.LShoulder), rSh = P(BodyTracker.RShoulder);
            Vector3 midHip = (lHip + rHip) / 2, midSh = (lSh + rSh) / 2;

            // Cadera en la caja de bateo. Si se ven los pies, se corre lo mismo que la persona
            // respecto a sus pies (cargar el peso, dar el paso); al final se apoya en el piso.
            bool legs = Seen(BodyTracker.LAnkle) && Seen(BodyTracker.RAnkle) && Seen(BodyTracker.LKnee) && Seen(BodyTracker.RKnee);
            var shift = Vector3.zero;
            if (legs)
            {
                float personLeg = Vector3.Distance(lHip, P(BodyTracker.LKnee)) + Vector3.Distance(P(BodyTracker.LKnee), P(BodyTracker.LAnkle));
                var feet = (P(BodyTracker.LAnkle) + P(BodyTracker.RAnkle)) / 2 - midHip;
                shift = -new Vector3(feet.x, 0, feet.z) * (legLength / Mathf.Max(0.3f, personLeg));
                shift = Vector3.ClampMagnitude(shift, 0.6f);
            }
            hips.position = root.position + shift + Vector3.up * hipsBindLocal.y;

            // Torso: cadera con la línea de cadera, pecho con la de hombros; la columna en medio.
            var up = (midSh - midHip).normalized;
            var hipsRot = Frame(rHip - lHip, up) * Quaternion.Inverse(hipsFrameBind) * hipsBind;
            var chestRot = Frame(rSh - lSh, up) * Quaternion.Inverse(chestFrameBind) * chestBind;
            hips.rotation = hipsRot;
            if (spine && spine != chest) spine.rotation = Quaternion.Slerp(hipsRot * Quaternion.Inverse(hipsBind), chestRot * Quaternion.Inverse(chestBind), 0.5f) * spineBind;
            chest.rotation = chestRot;

            // Cabeza: hacia la nariz, con la línea de las orejas.
            if (Seen(BodyTracker.Nose))
            {
                Vector3 lEar = P(BodyTracker.LEar), rEar = P(BodyTracker.REar);
                var headUp = ((lEar + rEar) / 2 - midSh).normalized;
                head.rotation = Frame(rEar - lEar, headUp) * Quaternion.Inverse(chestFrameBind) * headBind;
            }

            // Brazos y manos.
            Aim(HumanBodyBones.LeftUpperArm, lSh, P(BodyTracker.LElbow));
            Aim(HumanBodyBones.LeftLowerArm, P(BodyTracker.LElbow), P(BodyTracker.LWrist));
            Aim(HumanBodyBones.LeftHand, P(BodyTracker.LWrist), P(BodyTracker.LIndex));
            Aim(HumanBodyBones.RightUpperArm, rSh, P(BodyTracker.RElbow));
            Aim(HumanBodyBones.RightLowerArm, P(BodyTracker.RElbow), P(BodyTracker.RWrist));
            Aim(HumanBodyBones.RightHand, P(BodyTracker.RWrist), P(BodyTracker.RIndex));

            // Piernas: las de la persona si se ven; si no, postura de bateo según hacia dónde mira la cadera.
            if (legs)
            {
                Aim(HumanBodyBones.LeftUpperLeg, lHip, P(BodyTracker.LKnee));
                Aim(HumanBodyBones.LeftLowerLeg, P(BodyTracker.LKnee), P(BodyTracker.LAnkle));
                Aim(HumanBodyBones.LeftFoot, P(BodyTracker.LAnkle), P(BodyTracker.LToe));
                Aim(HumanBodyBones.RightUpperLeg, rHip, P(BodyTracker.RKnee));
                Aim(HumanBodyBones.RightLowerLeg, P(BodyTracker.RKnee), P(BodyTracker.RAnkle));
                Aim(HumanBodyBones.RightFoot, P(BodyTracker.RAnkle), P(BodyTracker.RToe));
            }
            else
            {
                var right = (rHip - lHip); right.y = 0; right = right.sqrMagnitude > 1e-4f ? right.normalized : root.right;
                var fwd = Vector3.Cross(right, Vector3.up);
                AimDir(HumanBodyBones.LeftUpperLeg, (Vector3.down - right * 0.28f + fwd * 0.18f).normalized);
                AimDir(HumanBodyBones.LeftLowerLeg, (Vector3.down - right * 0.1f - fwd * 0.12f).normalized);
                AimDir(HumanBodyBones.RightUpperLeg, (Vector3.down + right * 0.28f + fwd * 0.18f).normalized);
                AimDir(HumanBodyBones.RightLowerLeg, (Vector3.down + right * 0.1f - fwd * 0.12f).normalized);
                AimDir(HumanBodyBones.LeftFoot, (fwd + Vector3.down * 0.3f).normalized);
                AimDir(HumanBodyBones.RightFoot, (fwd + Vector3.down * 0.3f).normalized);
            }

            // Apoyo en el piso: el pie más bajo queda a su altura de la T-pose.
            float lowest = Mathf.Min(B(HumanBodyBones.LeftFoot).position.y, B(HumanBodyBones.RightFoot).position.y);
            hips.position += Vector3.up * (footBindY - lowest + root.position.y);

            // Bate: agarrado con las dos manos, apuntando hacia donde apuntan los dedos.
            var rWrist = P(BodyTracker.RWrist); var lWrist = P(BodyTracker.LWrist);
            var dir = ((P(BodyTracker.RIndex) - rWrist) + (P(BodyTracker.LIndex) - lWrist)).normalized;
            PlaceBat(B(HumanBodyBones.RightHand).position, B(HumanBodyBones.LeftHand).position, dir);
        }

        void Aim(HumanBodyBones b, Vector3 from, Vector3 to) => AimDir(b, (to - from).normalized);

        void AimDir(HumanBodyBones b, Vector3 dir)
        {
            if (!bones.TryGetValue(b, out var bone) || dir.sqrMagnitude < 1e-6f) return;
            // La pose de enlace se midió con la raíz sin girar: se lleva al giro actual de la raíz.
            bone.t.rotation = Quaternion.FromToRotation(root.rotation * bone.bindDir, dir) * root.rotation * bone.bindRot;
        }

        // Dirección del bate medida por la IMU; sin valor, la estiman la cámara o la postura.
        public Vector3? BatDirection;

        void PlaceBat(Vector3 rightHand, Vector3 leftHand, Vector3 dir)
        {
            if (flipping) return;
            if (BatDirection.HasValue) dir = BatDirection.Value;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
            var grip = (rightHand + leftHand) * 0.5f;
            bat.SetPositionAndRotation(grip - dir * 0.08f, Quaternion.LookRotation(dir));
        }
    }
}
