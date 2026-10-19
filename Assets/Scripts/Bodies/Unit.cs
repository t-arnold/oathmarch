using System.Collections.Generic;
using UnityEngine;
using Oathmarch.Brain;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// A BODY. Has no idea about orders. It walks to the point its squad's brain picked,
    /// engages hostiles that come into contact range, swings, takes damage, and falls.
    /// </summary>
    public class Unit : MonoBehaviour
    {
        public static readonly List<Unit> All = new List<Unit>();
        static int nextId = 1;

        public int Id;
        public Faction faction;
        public Squad squad;            // null for the king
        public int formationIndex;
        public float maxHp = 40, hp = 40, damage = 10, attackRange = 1.7f, attackCooldown = 1.1f;
        public float walkSpeed = 2.2f, runSpeed = 5f, turnSpeed = 540f, vision = 18f;
        public bool IsAlive => hp > 0;
        public bool IsKing;

        protected CharacterAnimator anim;
        protected float cooldown;
        Unit target;
        Vector3 velocity;

        protected virtual void Awake() { Id = nextId++; All.Add(this); anim = GetComponentInChildren<CharacterAnimator>(); }
        protected virtual void OnDestroy() { All.Remove(this); }

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
            if (squad == null) return;

            // contact: pick up a hostile inside the squad's aggro radius
            if (target == null || !target.IsAlive || Dist(target) > squad.AggroRadius * 1.5f) target = FindHostile(squad.AggroRadius);

            Vector3 goal; bool run = squad.Run;
            if (target != null)
            {
                goal = target.transform.position; run = true;
                if (Dist(target) <= attackRange)
                {
                    Face(goal); anim.SetSpeed(0);
                    if (cooldown <= 0 && !anim.IsAttacking) { anim.Attack(); cooldown = attackCooldown; StartCoroutine(HitAfter(target, 0.35f)); }
                    return;
                }
            }
            else goal = Formation(new Vector3(squad.Destination.x, 0, squad.Destination.z));

            MoveTowards(goal, run ? runSpeed : walkSpeed, target != null ? attackRange * 0.8f : 0.6f);
        }

        Vector3 Formation(Vector3 center)
        {
            int i = formationIndex; if (i == 0) return center;
            float ang = i * 2.4f; float r = 1.6f + 0.5f * (i / 4);
            return center + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * r;
        }

        void MoveTowards(Vector3 goal, float speed, float stopDist)
        {
            Vector3 to = goal - transform.position; to.y = 0;
            Vector3 desired = to.magnitude > stopDist ? to.normalized * speed : Vector3.zero;
            // separation from nearby bodies so squads do not stack
            foreach (var u in All)
            {
                if (u == this || !u.IsAlive) continue;
                Vector3 d = transform.position - u.transform.position; d.y = 0;
                float m = d.magnitude;
                if (m < 1.1f && m > 1e-3f) desired += d / m * (1.1f - m) * 4f;
            }
            // avoid buildings with a short probe
            if (desired.sqrMagnitude > 0.01f && Physics.SphereCast(transform.position + Vector3.up, 0.4f, desired.normalized, out var hit, 2f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Unit>() == null && hit.collider.gameObject.layer != 2)
                desired = Vector3.Reflect(desired, hit.normal) * 0.5f + Vector3.Cross(Vector3.up, hit.normal) * speed * 0.8f;

            velocity = Vector3.MoveTowards(velocity, desired, 20f * Time.deltaTime);
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
                if (u.IsAlive && Hostile(faction, u.faction)) { float d = Dist(u); if (d < bd) { bd = d; best = u; } }
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
            if (!IsAlive) return;
            hp -= dmg;
            if (hp <= 0) Die();
        }

        protected virtual void Die()
        {
            hp = 0; anim.Die();
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 12f);
        }
    }
}
