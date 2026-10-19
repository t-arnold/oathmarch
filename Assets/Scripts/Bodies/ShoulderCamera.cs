using UnityEngine;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// Over-the-right-shoulder orbit camera. Collision: one SphereCast from the pivot (inside the king's head,
    /// on the Ignore Raycast layer so it never hits him) to the shoulder, then from the shoulder back to the
    /// desired spot, against the environment layer only. If the result still overlaps geometry, it walks in.
    /// </summary>
    public class ShoulderCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 pivotOffset = new Vector3(0, 1.62f, 0);
        public float shoulder = 0.6f, distance = 3.4f, minDist = 1.4f, maxDist = 7f;
        public float sensitivity = 3f, minPitch = -35f, maxPitch = 70f, radius = 0.28f;
        float yaw, pitch = 12f, current;
        Camera cam;

        void Start()
        {
            current = distance; if (target) yaw = target.eulerAngles.y; Lock(true);
            cam = GetComponent<Camera>(); if (cam) cam.nearClipPlane = 0.05f;
        }

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
            int mask = Unit.EnvMask;

            Vector3 side = rot * Vector3.right;
            float sh = shoulder;
            if (Physics.SphereCast(pivot, radius, side, out var hs, shoulder, mask, QueryTriggerInteraction.Ignore)) sh = Mathf.Max(0f, hs.distance - 0.02f);
            Vector3 shoulderPos = pivot + side * sh;

            Vector3 back = rot * Vector3.back;
            float want = distance;
            if (Physics.SphereCast(shoulderPos, radius, back, out var hb, distance, mask, QueryTriggerInteraction.Ignore)) want = Mathf.Max(0.2f, hb.distance - 0.02f);
            current = want < current ? want : Mathf.Lerp(current, want, 5f * Time.deltaTime);

            Vector3 pos = shoulderPos + back * current;
            // last guard: never end inside a collider
            for (int i = 0; i < 8 && Physics.CheckSphere(pos, radius * 0.6f, mask, QueryTriggerInteraction.Ignore); i++)
                pos = Vector3.Lerp(pos, pivot, 0.3f);
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
