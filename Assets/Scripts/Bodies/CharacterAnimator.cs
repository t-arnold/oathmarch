using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// Drives the ONE shared humanoid locomotion/combat set.
    /// Preferred path: the generated AnimatorController (Resources/Characters/Locomotion.controller) with a
    /// 1D blend tree on Speed (Idle 0 / Walk 1.57 / Run 3.56 m/s = the clips' measured stride speeds), an
    /// Attack trigger and a Dead bool. Fallback: a Playables mixer whose weights always sum to 1 and whose
    /// loops are wrapped by hand, so the pose can never fall back to the bind (T) pose.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CharacterAnimator : MonoBehaviour
    {
        // measured from the clips (see Assets/Tools/make_mannequin.py output): ground speed at playback speed 1
        public const float WalkNatural = 1.57f, RunNatural = 3.56f;
        static readonly int SpeedId = Animator.StringToHash("Speed"), MoveScaleId = Animator.StringToHash("MoveScale"),
            AttackId = Animator.StringToHash("Attack"), DeadId = Animator.StringToHash("Dead");

        public AnimationClip idle, walk, run, attack, death;
        public bool UsingController { get; private set; }

        Animator animator;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        float attackT = -1f, speed, smoothSpeed, deathT;
        bool dead;

        public bool IsAttacking => attackT >= 0f;

        public void Init(RuntimeAnimatorController controller, AnimationClip i, AnimationClip w, AnimationClip r, AnimationClip a, AnimationClip d)
        {
            idle = i; walk = w; run = r; attack = a; death = d;
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
                UsingController = true;
                return;
            }
            animator.runtimeAnimatorController = null;
            graph = PlayableGraph.Create(name + "_Anim");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Out", animator);
            mixer = AnimationMixerPlayable.Create(graph, 5);
            output.SetSourcePlayable(mixer);
            AnimationClip[] clips = { idle, walk, run, attack, death };
            for (int k = 0; k < 5; k++)
            {
                if (clips[k] == null) continue;
                var p = AnimationClipPlayable.Create(graph, clips[k]);
                p.SetApplyFootIK(false);
                graph.Connect(p, 0, mixer, k);
            }
            mixer.SetInputWeight(0, 1f);
            graph.Play();
        }

        public void SetSpeed(float s) => speed = Mathf.Max(0, s);

        public void Attack()
        {
            if (dead || IsAttacking) return;
            attackT = 0f;
            if (UsingController) animator.SetTrigger(AttackId);
        }

        public void Die()
        {
            if (dead) return; dead = true; deathT = 0;
            if (UsingController) animator.SetBool(DeadId, true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            smoothSpeed = Mathf.MoveTowards(smoothSpeed, speed, 12f * dt);
            // playback scale so feet match ground speed: blend tree thresholds are the natural speeds,
            // so inside [Walk..Run] scale ~1; outside, scale proportionally.
            float scale = smoothSpeed > RunNatural ? smoothSpeed / RunNatural : smoothSpeed < WalkNatural && smoothSpeed > 0.2f ? Mathf.Lerp(0.8f, 1f, smoothSpeed / WalkNatural) : 1f;
            float attackLen = attack != null ? attack.length : 0.8f;
            if (attackT >= 0f) { attackT += dt; if (attackT >= attackLen) attackT = -1f; }

            if (UsingController)
            {
                animator.SetFloat(SpeedId, smoothSpeed);
                animator.SetFloat(MoveScaleId, scale);
                return;
            }
            if (!graph.IsValid()) return;

            // ---- fallback mixer: weights always sum to exactly 1
            float wIdle, wWalk, wRun;
            if (smoothSpeed <= WalkNatural) { float t = Mathf.Clamp01(smoothSpeed / WalkNatural); wIdle = 1 - t; wWalk = t; wRun = 0; }
            else { float t = Mathf.Clamp01((smoothSpeed - WalkNatural) / (RunNatural - WalkNatural)); wIdle = 0; wWalk = 1 - t; wRun = t; }
            float wAtk = 0, wDeath = dead ? 1f : 0f;
            if (attackT >= 0f) wAtk = Mathf.Clamp01(Mathf.Min(attackT / 0.08f, (attackLen - attackT) / 0.12f));
            float rest = 1f - Mathf.Max(wAtk, wDeath);
            float[] w = { wIdle * rest, wWalk * rest, wRun * rest, dead ? 0 : wAtk, wDeath };
            float sum = 0; for (int k = 0; k < 5; k++) { if (mixer.GetInput(k).IsValid()) sum += w[k]; else w[k] = 0; }
            if (sum < 1e-4f) { w[0] = 1; sum = 1; }
            for (int k = 0; k < 5; k++) mixer.SetInputWeight(k, w[k] / sum);

            Advance(0, dt, true); Advance(1, dt * scale, true); Advance(2, dt * scale, true);
            var ap = mixer.GetInput(3); if (ap.IsValid()) ap.SetTime(Mathf.Max(0, attackT));
            if (dead) { deathT += dt; var dp = mixer.GetInput(4); if (dp.IsValid()) dp.SetTime(Mathf.Min(deathT, death != null ? death.length - 0.01f : 1f)); }
            graph.Evaluate(0);
        }

        void Advance(int slot, float dt, bool loop)
        {
            var p = mixer.GetInput(slot);
            if (!p.IsValid()) return;
            var clip = ((AnimationClipPlayable)p).GetAnimationClip();
            double t = p.GetTime() + dt;
            if (loop && clip != null && clip.length > 0) t %= clip.length;
            p.SetTime(t);
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
