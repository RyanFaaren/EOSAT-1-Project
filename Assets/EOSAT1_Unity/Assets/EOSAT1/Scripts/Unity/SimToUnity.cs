// EOSAT-1 VR - the ONE place where simulator axes become Unity axes (course slide 21).
//
// Simulator: right-handed, Z up.      Unity: left-handed, Y up.
//   vector      (x, y, z)    -> (x, z, y)
//   quaternion  (x, y, z, w) -> (-x, -z, -y, w)
// Swapping Y and Z is a mirror, which flips handedness; a mirrored rotation turns the
// other way, hence the minus signs on the quaternion's vector part.
//
// Check from the slide: once the spacecraft points at Earth, the camera face must look
// straight down. In the default "Earth below" view, body +Z maps to Unity -Y.
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public static class SimToUnity
    {
        public static Vector3 Vec(Vec3d v)
        {
            return new Vector3((float)v.x, (float)v.z, (float)v.y);
        }

        public static Quaternion Rot(QuatD q)
        {
            return new Quaternion((float)-q.x, (float)-q.z, (float)-q.y, (float)q.w);
        }

        public static Vec3d ToSim(Vector3 v)
        {
            return new Vec3d(v.x, v.z, v.y);
        }

        public static QuatD ToSim(Quaternion q)
        {
            return new QuatD(-q.x, -q.z, -q.y, q.w);
        }

        /// <summary>
        /// A hinge rotation described in body axes (right-handed, positive = counter-clockwise
        /// looking down the axis), as a Unity local rotation.
        /// </summary>
        public static Quaternion BodyAxisAngle(Vec3d bodyAxis, float degrees)
        {
            return Quaternion.AngleAxis(-degrees, Vec(bodyAxis));
        }
    }
}
