using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// Drives the shared humanoid locomotion/combat set on an Animator through Playables,
    /// so no AnimatorController asset is needed. Clips come from the shared mannequin FBX
    /// (or any humanoid set you assign) and retarget onto whatever humanoid Avatar is on the Animator.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CharacterAnimator : MonoBehaviour
    {
        public AnimationClip idle, walk, run, attack, death;
        public float walkSpeed = 2f, runSpeed = 5.5f;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable attackP, deathP;
        float attackT = -1f, speed;
        bool dead;

        public bool IsAttacking => attackT >= 0f;

        public void Init(AnimationClip i, AnimationClip w, AnimationClip r, AnimationClip a, AnimationClip d)
        {
            idle = i; walk = w; run = r; attack = a; death = d;
            var anim = GetComponent<Animator>();
            anim.applyRootMotion = false;
            graph = PlayableGraph.Create(name + "_Anim");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Out", anim);
            mixer = AnimationMixerPlayable.Create(graph, 5);
            output.SetSourcePlayable(mixer);
            Connect(idle, 0); Connect(walk, 1); Connect(run, 2);
            attackP = Connect(attack, 3); deathP = Connect(death, 4);
            mixer.SetInputWeight(0, 1f);
            graph.Play();
        }

        AnimationClipPlayable Connect(AnimationClip c, int slot)
        {
            if (c == null) return default;
            var p = AnimationClipPlayable.Create(graph, c);
            graph.Connect(p, 0, mixer, slot);
            return p;
        }

        public void SetSpeed(float s) => speed = s;

        public void Attack()
        {
            if (dead || attack == null || IsAttacking) return;
            attackT = 0f; attackP.SetTime(0); attackP.SetDuration(attack.length);
        }

        public void Die()
        {
            if (dead) return; dead = true;
            if (death != null) { deathP.SetTime(0); deathP.SetDuration(death.length); }
        }

        void Update()
        {
            if (!graph.IsValid()) return;
            float wIdle, wWalk, wRun;
            if (speed <= walkSpeed) { float t = Mathf.Clamp01(speed / walkSpeed); wIdle = 1 - t; wWalk = t; wRun = 0; }
            else { float t = Mathf.Clamp01((speed - walkSpeed) / (runSpeed - walkSpeed)); wIdle = 0; wWalk = 1 - t; wRun = t; }

            // keep walk/run cycles in phase with how fast the body is actually moving
            if (walk != null) mixer.GetInput(1).SetSpeed(Mathf.Max(0.6f, speed / walkSpeed));
            if (run != null) mixer.GetInput(2).SetSpeed(Mathf.Max(0.8f, speed / runSpeed));

            float wAtk = 0, wDeath = 0;
            if (attackT >= 0f)
            {
                attackT += Time.deltaTime;
                float len = attack.length;
                wAtk = Mathf.Clamp01(Mathf.Min(attackT / 0.08f, (len - attackT) / 0.12f));
                if (attackT >= len) attackT = -1f;
            }
            if (dead) wDeath = 1f;
            float rest = 1f - Mathf.Max(wAtk, wDeath);
            mixer.SetInputWeight(0, wIdle * rest);
            mixer.SetInputWeight(1, wWalk * rest);
            mixer.SetInputWeight(2, wRun * rest);
            mixer.SetInputWeight(3, dead ? 0 : wAtk);
            mixer.SetInputWeight(4, wDeath);
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
