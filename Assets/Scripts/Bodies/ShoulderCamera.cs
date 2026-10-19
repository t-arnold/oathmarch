using UnityEngine;

namespace Oathmarch.Bodies
{
    /// <summary>Over-the-right-shoulder orbit camera with collision. Mouse orbits, wheel zooms, Esc frees the cursor, click recaptures.</summary>
    public class ShoulderCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 pivotOffset = new Vector3(0, 1.6f, 0);
        public float shoulder = 0.65f, distance = 3.4f, minDist = 1.6f, maxDist = 7f;
        public float sensitivity = 3f, minPitch = -35f, maxPitch = 70f, radius = 0.25f;
        float yaw, pitch = 12f, current;

        void Start() { current = distance; if (target) yaw = target.eulerAngles.y; Lock(true); }

        static void Lock(bool on) { Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !on; }

        void LateUpdate()
        {
            if (target == null) return;
            bool ui = CommandUI.Instance != null && CommandUI.Instance.PanelOpen;
            if (Input.GetKeyDown(KeyCode.Escape)) Lock(false);
            if (!ui && Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) Lock(true);
            if (Cursor.lockState == CursorLockMode.Locked && !ui)
            {
                yaw += Input.GetAxis("Mouse X") * sensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sensitivity, minPitch, maxPitch);
                distance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel") * 4f, minDist, maxDist);
            }
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
            Vector3 pivot = target.position + pivotOffset;
            Vector3 shoulderPos = pivot + rot * Vector3.right * shoulder;
            // keep the shoulder offset itself out of walls
            if (Physics.SphereCast(pivot, radius, rot * Vector3.right, out var sh, shoulder, ~0, QueryTriggerInteraction.Ignore) && !IsSelf(sh.collider))
                shoulderPos = pivot + rot * Vector3.right * Mathf.Max(0, sh.distance - 0.05f);
            float want = distance;
            var hits = Physics.SphereCastAll(shoulderPos, radius, rot * Vector3.back, distance, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (!IsSelf(h.collider) && h.distance < want) want = Mathf.Max(0.3f, h.distance - 0.05f);
            current = want < current ? want : Mathf.Lerp(current, want, 6f * Time.deltaTime);
            transform.SetPositionAndRotation(shoulderPos + rot * Vector3.back * current, rot);
        }

        bool IsSelf(Collider c) => c.transform.IsChildOf(target) || c.GetComponentInParent<Unit>() != null;
    }
}
