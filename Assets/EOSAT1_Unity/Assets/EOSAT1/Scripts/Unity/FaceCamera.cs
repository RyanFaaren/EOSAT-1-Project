// EOSAT-1 VR - keeps a label turned towards the viewer and upright.
using UnityEngine;

namespace Eosat1
{
    public sealed class FaceCamera : MonoBehaviour
    {
        public Camera target;

        void LateUpdate()
        {
            Camera cam = target != null ? target : Camera.main;
            if (cam == null) return;
            Vector3 away = transform.position - cam.transform.position;
            if (away.sqrMagnitude < 1e-8f) return;
            transform.rotation = Quaternion.LookRotation(away, cam.transform.up);
        }
    }
}
