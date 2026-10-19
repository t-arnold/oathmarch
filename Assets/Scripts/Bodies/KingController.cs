using UnityEngine;

namespace Oathmarch.Bodies
{
    /// <summary>The king: the only body the player drives directly. WASD relative to the camera, Shift sprints, LMB swings.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class KingController : Unit
    {
        public Transform cam;
        CharacterController cc;
        float vy;
        Vector3 planar;

        protected override void Awake()
        {
            base.Awake(); IsKing = true;
            cc = GetComponent<CharacterController>();
            maxHp = hp = 150; damage = 20; attackRange = 2.1f; attackCooldown = 0.7f; walkSpeed = 2.6f; runSpeed = 6f; vision = 24f;
        }

        protected override void Update()
        {
            if (!IsAlive) return;
            cooldown -= Time.deltaTime;
            bool uiOpen = CommandUI.Instance != null && CommandUI.Instance.PanelOpen;
            float h = uiOpen ? 0 : Input.GetAxisRaw("Horizontal"), v = uiOpen ? 0 : Input.GetAxisRaw("Vertical");
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 input = Vector3.ClampMagnitude(fwd * v + right * h, 1f);
            bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float speed = (sprint ? runSpeed : walkSpeed) * input.magnitude;
            if (anim.IsAttacking) speed *= 0.3f;
            planar = Vector3.MoveTowards(planar, input.normalized * speed, 25f * Time.deltaTime);
            if (input.sqrMagnitude > 0.01f) Face(transform.position + input);

            vy = cc.isGrounded ? -1f : vy - 20f * Time.deltaTime;
            cc.Move((planar + Vector3.up * vy) * Time.deltaTime);
            anim.SetSpeed(new Vector3(planar.x, 0, planar.z).magnitude);

            if (!uiOpen && Input.GetMouseButtonDown(0) && cooldown <= 0 && !anim.IsAttacking && Cursor.lockState == CursorLockMode.Locked)
            {
                anim.Attack(); cooldown = attackCooldown;
                StartCoroutine(Swing());
            }
        }

        System.Collections.IEnumerator Swing()
        {
            yield return new WaitForSeconds(0.3f);
            foreach (var u in Unit.All.ToArray())
            {
                if (!u.IsAlive || !Hostile(faction, u.faction)) continue;
                Vector3 d = u.transform.position - transform.position; d.y = 0;
                if (d.magnitude <= attackRange + 0.4f && Vector3.Angle(transform.forward, d) < 70f) u.TakeDamage(damage, this);
            }
        }

        protected override void Die()
        {
            base.Die();
            CommandUI.Instance?.Log("The king has fallen. (Press R to restart the slice.)");
        }
    }
}
