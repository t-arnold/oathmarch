using System.Collections.Generic;
using UnityEngine;
using Oathmarch.Brain;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// A BODY. Knows nothing about orders. Walks to its slot around the squad anchor the brain picked,
    /// engages hostiles that come into contact range, swings, takes damage, and falls.
    /// Everything it actually does to others is reported to the brain as a BrainEvent.
    /// </summary>
    public class Unit : MonoBehaviour
    {
        public static readonly List<Unit> All = new List<Unit>();
        static int nextId = 1;
        public const int EnvMask = 1 << 0;     // buildings, trees, rocks, ground (Default layer)
        public const int BodyLayer = 2;        // characters live on Ignore Raycast so camera/steering casts skip them

        public int Id;
        public Faction faction;
        public Squad squad;            // null for the king and the commander
        public int formationIndex;
        public float maxHp = 40, hp = 40, damage = 10, attackRange = 1.7f, attackCooldown = 1.1f;
        public float walkSpeed = 1.6f, runSpeed = 3.8f, turnSpeed = 540f, vision = 22f;
        public bool invulnerable;
        public bool IsAlive => hp > 0;
        public bool IsKing;
        public Unit lastAttacker;

        protected CharacterAnimator anim;
        protected float cooldown;
        Unit target;
        Vector3 velocity;
        Vector2 jitter;
        float torchSwing;

        protected virtual void Awake()
        {
            Id = nextId++; All.Add(this);
            anim = GetComponentInChildren<CharacterAnimator>();
            var r = new System.Random(Id * 7919);
            jitter = new Vector2((float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f) * 0.9f;
            SetLayerRecursive(gameObject, BodyLayer);
        }
        protected virtual void OnDestroy() { All.Remove(this); }

        static void SetLayerRecursive(GameObject g, int layer) { g.layer = layer; foreach (Transform c in g.transform) SetLayerRecursive(c.gameObject, layer); }

        public UnitInfo Info => new UnitInfo
        {
            Id = Id, Faction = faction, SquadId = squad != null ? squad.Id : -1,
            Pos = new V2(transform.position.x, transform.position.z), Alive = IsAlive
        };

        public static bool Hostile(Faction a, Faction b) => a != b && a != Faction.Neutral && b != Faction.Neutral;

        protected virtual void Update()
        {
            if (!IsAlive) return;
            cooldown -= Time.deltaTime;
            if (squad == null) { anim.SetSpeed(0); return; }

            // contact: pick up a hostile inside the squad's aggro radius (fleeing squads have radius 0)
            float aggro = squad.AggroRadius;
            if (lastAttacker != null && lastAttacker.IsAlive && Dist(lastAttacker) < 4f) aggro = Mathf.Max(aggro, 4f); // always answer a blow
            if (target == null || !target.IsAlive || Dist(target) > aggro * 1.5f) target = FindHostile(aggro);

            Vector3 goal; bool run = squad.Run;
            if (target != null)
            {
                goal = target.transform.position; run = true;
                if (Dist(target) <= attackRange)
                {
                    Face(goal); anim.SetSpeed(0); velocity = Vector3.zero;
                    if (cooldown <= 0 && !anim.IsAttacking) { anim.Attack(); cooldown = attackCooldown; StartCoroutine(HitAfter(target, 0.35f)); }
                    return;
                }
            }
            else goal = Slot();

            // raiders at their target building: swing torches at it
            if (target == null && squad.State == SquadState.Torch && squad.TargetBuilding != null)
            {
                var b = squad.TargetBuilding; var bp = new Vector3(b.Pos.x, 0, b.Pos.z);
                float reach = Mathf.Max(b.SizeX, b.SizeZ) * 0.5f + 1.8f;
                if (Vector3.Distance(Flat(transform.position), bp) < reach)
                {
                    Face(bp); anim.SetSpeed(0); velocity = Vector3.zero;
                    torchSwing -= Time.deltaTime;
                    if (torchSwing <= 0) { anim.Attack(); torchSwing = 1.6f; }
                    return;
                }
                goal = bp;
            }

            MoveTowards(goal, run ? runSpeed : walkSpeed, target != null ? attackRange * 0.8f : 0.5f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// <summary>Loose wedge around the anchor, facing the squad heading, with per-man jitter.</summary>
        Vector3 Slot()
        {
            var a = new Vector3(squad.Destination.x, 0, squad.Destination.z);
            var f = new Vector3(squad.Heading.x, 0, squad.Heading.z);
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            var right = new Vector3(f.z, 0, -f.x);
            int i = formationIndex; int k = (i + 1) / 2; float side = i == 0 ? 0 : (i % 2 == 1 ? -1 : 1);
            float spacing = squad.State == SquadState.Hold || squad.State == SquadState.Regroup ? 1.6f : 2.2f;
            return a + right * (side * k * spacing + jitter.x) + f * (-k * spacing * 0.8f + jitter.y);
        }

        void MoveTowards(Vector3 goal, float speed, float stopDist)
        {
            Vector3 to = goal - transform.position; to.y = 0;
            float dist = to.magnitude;
            // arrive smoothly instead of overshooting/sliding
            float want = dist > stopDist ? Mathf.Min(speed, (dist - stopDist) * 2.5f) : 0f;
            Vector3 desired = dist > 1e-3f ? to / dist * want : Vector3.zero;
            foreach (var u in All)
            {
                if (u == this || !u.IsAlive) continue;
                Vector3 d = transform.position - u.transform.position; d.y = 0;
                float m = d.magnitude;
                if (m < 1.0f && m > 1e-3f) desired += d / m * (1.0f - m) * 3f;
            }
            if (desired.sqrMagnitude > 0.01f && Physics.SphereCast(transform.position + Vector3.up, 0.4f, desired.normalized, out var hit, 1.8f, EnvMask, QueryTriggerInteraction.Ignore))
            {
                var tangent = Vector3.Cross(Vector3.up, hit.normal);
                if (Vector3.Dot(tangent, to) < 0) tangent = -tangent;
                desired = tangent * desired.magnitude + hit.normal * 0.5f;
            }
            velocity = Vector3.MoveTowards(velocity, desired, 12f * Time.deltaTime);
            transform.position += velocity * Time.deltaTime;
            var p = transform.position; p.y = 0; transform.position = p;
            if (velocity.sqrMagnitude > 0.05f) Face(transform.position + velocity);
            anim.SetSpeed(velocity.magnitude);
        }

        protected void Face(Vector3 p)
        {
            Vector3 d = p - transform.position; d.y = 0;
            if (d.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), turnSpeed * Time.deltaTime);
        }

        protected Unit FindHostile(float radius)
        {
            Unit best = null; float bd = radius;
            foreach (var u in All)
                if (u.IsAlive && Hostile(faction, u.faction) && !u.invulnerable) { float d = Dist(u); if (d < bd) { bd = d; best = u; } }
            return best;
        }

        protected float Dist(Unit u) { var d = u.transform.position - transform.position; d.y = 0; return d.magnitude; }

        System.Collections.IEnumerator HitAfter(Unit t, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (IsAlive && t != null && t.IsAlive && Dist(t) <= attackRange + 0.6f) t.TakeDamage(damage, this);
        }

        public virtual void TakeDamage(float dmg, Unit from)
        {
            if (!IsAlive || invulnerable) return;
            hp -= dmg; lastAttacker = from;
            bool killed = hp <= 0;
            GameRoot.Instance?.PushEvent(new BrainEvent
            {
                Kind = killed ? EventKind.Kill : EventKind.Hit, Time = Time.time,
                ActorId = from != null ? from.Id : -1, ActorSquad = from != null && from.squad != null ? from.squad.Id : -1, ActorFaction = from != null ? from.faction : Faction.Neutral,
                VictimId = Id, VictimSquad = squad != null ? squad.Id : -1, VictimFaction = faction,
                Pos = new V2(transform.position.x, transform.position.z)
            });
            if (killed) Die();
        }

        protected virtual void Die()
        {
            hp = 0; anim.Die();
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 10f);
        }
    }
}
