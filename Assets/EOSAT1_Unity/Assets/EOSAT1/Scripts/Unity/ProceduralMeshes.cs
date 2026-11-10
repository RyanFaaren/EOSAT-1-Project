// EOSAT-1 VR - meshes made in code: globe, star field, solar panel, cone, ring.
// The scene builder saves them as assets; they can also be made at runtime.
using System;
using System.Collections.Generic;
using System.Globalization;
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public static class ProceduralMeshes
    {
        /// <summary>
        /// Unit sphere for an equirectangular map. Vertex directions are Earth-fixed
        /// (lon 0 on Unity +X, north on +Y, lon +90 E on +Z after the sim-to-Unity swap);
        /// u = (lon + 180) / 360, v = (lat + 90) / 180, like NASA's Blue Marble.
        /// </summary>
        public static Mesh UvSphere(int lonSegments, int latSegments, string name)
        {
            lonSegments = Mathf.Max(8, lonSegments);
            latSegments = Mathf.Max(4, latSegments);
            int cols = lonSegments + 1;
            var verts = new Vector3[cols * (latSegments + 1)];
            var normals = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            for (int j = 0; j <= latSegments; j++)
            {
                double lat = -Math.PI / 2 + Math.PI * j / latSegments;
                for (int i = 0; i <= lonSegments; i++)
                {
                    double lon = -Math.PI + 2 * Math.PI * i / lonSegments;
                    var dir = new Vec3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
                    Vector3 u = SimToUnity.Vec(dir);
                    int k = j * cols + i;
                    verts[k] = u;
                    normals[k] = u;
                    uvs[k] = new Vector2((float)i / lonSegments, (float)j / latSegments);
                }
            }
            var tris = new int[lonSegments * latSegments * 6];
            int t = 0;
            for (int j = 0; j < latSegments; j++)
            {
                for (int i = 0; i < lonSegments; i++)
                {
                    int a = j * cols + i, b = a + 1, c = a + cols, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d;
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                }
            }
            var m = new Mesh { name = name };
            if (verts.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = verts;
            m.normals = normals;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Star field on a unit sphere in J2000 axes, one small quad per star facing the centre.
        /// Text lines: ra_deg dec_deg vmag b_minus_v [name]; '#' starts a comment.
        /// </summary>
        public static Mesh StarField(string catalogText, string name)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();
            if (!string.IsNullOrEmpty(catalogText))
            {
                string[] lines = catalogText.Split('\n');
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 4) continue;
                    double ra, dec, mag, bv;
                    if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out ra) ||
                        !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out dec) ||
                        !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out mag) ||
                        !double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out bv)) continue;
                    double a = ra * MathUtil.Deg2Rad, d = dec * MathUtil.Deg2Rad;
                    Vector3 dir = SimToUnity.Vec(new Vec3d(Math.Cos(d) * Math.Cos(a), Math.Cos(d) * Math.Sin(a), Math.Sin(d))).normalized;
                    float halfDeg = 0.11f + 0.045f * Mathf.Max(0f, 4.2f - (float)mag);
                    float half = Mathf.Tan(halfDeg * Mathf.Deg2Rad);
                    Vector3 t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
                    Vector3 t2 = Vector3.Cross(dir, t1).normalized;
                    float intensity = Mathf.Clamp(1.25f - 0.18f * (float)mag, 0.12f, 1.6f);
                    Color c = StarColor((float)bv) * intensity;
                    c.a = 1f;
                    int baseIndex = verts.Count;
                    verts.Add(dir + (-t1 - t2) * half);
                    verts.Add(dir + (t1 - t2) * half);
                    verts.Add(dir + (t1 + t2) * half);
                    verts.Add(dir + (-t1 + t2) * half);
                    uvs.Add(new Vector2(0, 0));
                    uvs.Add(new Vector2(1, 0));
                    uvs.Add(new Vector2(1, 1));
                    uvs.Add(new Vector2(0, 1));
                    for (int k = 0; k < 4; k++) cols.Add(c);
                    tris.Add(baseIndex); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
                    tris.Add(baseIndex); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
                }
            }
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
            return m;
        }

        static Color StarColor(float bv)
        {
            Color blue = new Color(0.72f, 0.82f, 1f), white = new Color(1f, 1f, 1f);
            Color yellow = new Color(1f, 0.94f, 0.82f), orange = new Color(1f, 0.8f, 0.58f), red = new Color(1f, 0.7f, 0.48f);
            if (bv < 0f) return Color.Lerp(white, blue, Mathf.Clamp01(-bv / 0.3f));
            if (bv < 0.6f) return Color.Lerp(white, yellow, bv / 0.6f);
            if (bv < 1.2f) return Color.Lerp(yellow, orange, (bv - 0.6f) / 0.6f);
            return Color.Lerp(orange, red, Mathf.Clamp01((bv - 1.2f) / 0.6f));
        }

        /// <summary>
        /// Thin rectangular panel centred on the origin, in the XY plane, cells on the +Z face
        /// (submesh 0) and the back and edges as submesh 1. The cell texture repeats once per
        /// cell of roughly cellSize metres.
        /// </summary>
        public static Mesh Panel(float width, float height, float thickness, float cellSize, string name)
        {
            float hx = width * 0.5f, hy = height * 0.5f, hz = thickness * 0.5f;
            float nu = Mathf.Max(1, Mathf.Round(width / cellSize));
            float nv = Mathf.Max(1, Mathf.Round(height / cellSize));
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var front = new List<int>();
            var rest = new List<int>();

            // a face given 4 corners in clockwise order as seen from outside (Unity winding)
            Action<Vector3, Vector3, Vector3, Vector3, Vector3, Vector2, List<int>> face = (p0, p1, p2, p3, normal, uvScale, list) =>
            {
                int b = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, uvScale.y));
                uv.Add(new Vector2(uvScale.x, uvScale.y));
                uv.Add(new Vector2(uvScale.x, 0));
                list.Add(b); list.Add(b + 1); list.Add(b + 2);
                list.Add(b); list.Add(b + 2); list.Add(b + 3);
            };
            Vector2 cells = new Vector2(nu, nv), one = Vector2.one;
            // +Z face, seen from +Z (looking towards -Z, +X is on the viewer's left)
            face(new Vector3(hx, -hy, hz), new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, -hy, hz), Vector3.forward, cells, front);
            // -Z face, seen from -Z
            face(new Vector3(-hx, -hy, -hz), new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, -hy, -hz), Vector3.back, one, rest);
            // edges
            face(new Vector3(hx, -hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz), new Vector3(hx, -hy, hz), Vector3.right, one, rest);
            face(new Vector3(-hx, -hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, hy, -hz), new Vector3(-hx, -hy, -hz), Vector3.left, one, rest);
            face(new Vector3(-hx, hy, -hz), new Vector3(-hx, hy, hz), new Vector3(hx, hy, hz), new Vector3(hx, hy, -hz), Vector3.up, one, rest);
            face(new Vector3(-hx, -hy, hz), new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(hx, -hy, hz), Vector3.down, one, rest);

            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.subMeshCount = 2;
            m.SetTriangles(front, 0);
            m.SetTriangles(rest, 1);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>Cone along +Y: base radius 0.5 at y = 0, tip at y = 1.</summary>
        public static Mesh Cone(int segments, string name)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a0 = 2f * Mathf.PI * i / segments, a1 = 2f * Mathf.PI * (i + 1) / segments;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
                Vector3 tip = new Vector3(0f, 1f, 0f);
                Vector3 side = Vector3.Cross(p1 - p0, tip - p0).normalized;
                if (Vector3.Dot(side, p0 + p1) < 0) side = -side;
                int b = v.Count;
                v.Add(p0); v.Add(tip); v.Add(p1);
                n.Add(side); n.Add(side); n.Add(side);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                // base
                b = v.Count;
                v.Add(p0); v.Add(p1); v.Add(Vector3.zero);
                n.Add(Vector3.down); n.Add(Vector3.down); n.Add(Vector3.down);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ spacecraft parts (EOS SAT-1 model)

        /// <summary>
        /// Collects vertices and triangles per submesh. Each triangle is wound so that it
        /// faces the way its vertex normals point (Unity: Cross(b - a, c - a) is the front),
        /// which keeps meshes right after the right-to-left-handed axis swap.
        /// </summary>
        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<List<int>> Sub = new List<List<int>>();

            public Builder(int submeshes)
            {
                for (int i = 0; i < Mathf.Max(1, submeshes); i++) Sub.Add(new List<int>());
            }

            public int Vert(Vector3 p, Vector3 n, Vector2 uv)
            {
                V.Add(p);
                N.Add(n);
                UV.Add(uv);
                return V.Count - 1;
            }

            public void Tri(int sub, int a, int b, int c)
            {
                Vector3 face = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
                if (face.sqrMagnitude < 1e-16f) return;
                if (Vector3.Dot(face, N[a] + N[b] + N[c]) < 0f)
                {
                    int t = b;
                    b = c;
                    c = t;
                }
                List<int> s = Sub[sub];
                s.Add(a);
                s.Add(b);
                s.Add(c);
            }

            public void Quad(int sub, int a, int b, int c, int d)
            {
                Tri(sub, a, b, c);
                Tri(sub, a, c, d);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetUVs(0, UV);
                m.subMeshCount = Sub.Count;
                for (int i = 0; i < Sub.Count; i++) m.SetTriangles(Sub[i], i);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        /// <summary>
        /// Surface of revolution about local +Y. profile = (radius, height) points in metres,
        /// traversed so the outside is on the right (the outward normal is (dh, -dr)).
        /// submeshOfSegment[k] picks the submesh (material) for profile segment k.
        /// </summary>
        public static Mesh Lathe(IList<Vector2> profile, int segments, IList<int> submeshOfSegment, int submeshCount, string name)
        {
            segments = Mathf.Max(6, segments);
            var b = new Builder(submeshCount);
            float total = 0f;
            for (int k = 0; k + 1 < profile.Count; k++) total += (profile[k + 1] - profile[k]).magnitude;
            float run = 0f;
            for (int k = 0; k + 1 < profile.Count; k++)
            {
                Vector2 p0 = profile[k], p1 = profile[k + 1];
                float len = (p1 - p0).magnitude;
                if (len < 1e-7f || (p0.x < 1e-7f && p1.x < 1e-7f))
                {
                    run += len;
                    continue;
                }
                Vector2 dn = new Vector2(p1.y - p0.y, -(p1.x - p0.x)).normalized;   // (dh, -dr)
                float v0 = total > 0 ? run / total : 0f, v1 = total > 0 ? (run + len) / total : 1f;
                int sub = submeshOfSegment != null && k < submeshOfSegment.Count ? Mathf.Clamp(submeshOfSegment[k], 0, submeshCount - 1) : 0;
                int ring0 = b.V.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float t = 2f * Mathf.PI * i / segments;
                    var radial = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
                    Vector3 n = (radial * dn.x + Vector3.up * dn.y).normalized;
                    b.Vert(radial * p0.x + Vector3.up * p0.y, n, new Vector2((float)i / segments, v0));
                    b.Vert(radial * p1.x + Vector3.up * p1.y, n, new Vector2((float)i / segments, v1));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = ring0 + i * 2;
                    b.Quad(sub, a, a + 2, a + 3, a + 1);
                }
                run += len;
            }
            return b.ToMesh(name);
        }

        /// <summary>
        /// Straight prism. polygon = corners in body X-Y (metres, counter-clockwise seen from +Z),
        /// extruded from body z0 to z1, converted to Unity axes. Submeshes: 0 sides, 1 the +Z cap
        /// (camera face), 2 the -Z cap. UVs are planar, one tile every uvTile metres.
        /// </summary>
        public static Mesh Prism(IList<Vector2> polygon, float z0, float z1, float uvTile, string name)
        {
            var b = new Builder(3);
            int k = polygon.Count;
            float s = Mathf.Max(1e-4f, uvTile);
            for (int i = 0; i < k; i++)
            {
                Vector2 a2 = polygon[i], c2 = polygon[(i + 1) % k];
                Vector2 e = c2 - a2;
                float len = e.magnitude;
                Vector3 nBody = new Vector3(e.y, -e.x, 0f).normalized;       // outward for a counter-clockwise polygon
                Vector3 n = SimToUnity.Vec(new Eosat1.Core.Vec3d(nBody.x, nBody.y, nBody.z));
                int p0 = b.Vert(Body(a2.x, a2.y, z0), n, new Vector2(0f, z0 / s));
                int p1 = b.Vert(Body(c2.x, c2.y, z0), n, new Vector2(len / s, z0 / s));
                int p2 = b.Vert(Body(c2.x, c2.y, z1), n, new Vector2(len / s, z1 / s));
                int p3 = b.Vert(Body(a2.x, a2.y, z1), n, new Vector2(0f, z1 / s));
                b.Quad(0, p0, p1, p2, p3);
            }
            Vector2 c = Vector2.zero;
            for (int i = 0; i < k; i++) c += polygon[i];
            c /= Mathf.Max(1, k);
            Vector3 up = SimToUnity.Vec(new Eosat1.Core.Vec3d(0, 0, 1)), down = -up;
            int cf = b.Vert(Body(c.x, c.y, z1), up, c / s);
            int cb = b.Vert(Body(c.x, c.y, z0), down, c / s);
            for (int i = 0; i < k; i++)
            {
                Vector2 a2 = polygon[i], c2 = polygon[(i + 1) % k];
                b.Tri(1, cf, b.Vert(Body(a2.x, a2.y, z1), up, a2 / s), b.Vert(Body(c2.x, c2.y, z1), up, c2 / s));
                b.Tri(2, cb, b.Vert(Body(c2.x, c2.y, z0), down, c2 / s), b.Vert(Body(a2.x, a2.y, z0), down, a2 / s));
            }
            return b.ToMesh(name);
        }

        static Vector3 Body(float x, float y, float z)
        {
            return SimToUnity.Vec(new Eosat1.Core.Vec3d(x, y, z));
        }

        /// <summary>
        /// Flat panel centred on the origin, front face (+Z local) mapped 0..1 across the whole
        /// panel: u grows towards local -X (the viewer's right when facing the front), v towards +Y.
        /// Submeshes: 0 front, 1 back, 2 edges.
        /// </summary>
        public static Mesh PanelFull(float width, float height, float thickness, string name)
        {
            float hx = width * 0.5f, hy = height * 0.5f, hz = thickness * 0.5f;
            var b = new Builder(3);
            Action<int, Vector3, Vector3, Vector3, Vector3, Vector3, bool> face = (sub, p0, p1, p2, p3, n, uv01) =>
            {
                int a = b.Vert(p0, n, uv01 ? new Vector2(0, 0) : Vector2.zero);
                int c1 = b.Vert(p1, n, uv01 ? new Vector2(0, 1) : Vector2.zero);
                int c2 = b.Vert(p2, n, uv01 ? new Vector2(1, 1) : Vector2.zero);
                int c3 = b.Vert(p3, n, uv01 ? new Vector2(1, 0) : Vector2.zero);
                b.Quad(sub, a, c1, c2, c3);
            };
            face(0, new Vector3(hx, -hy, hz), new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, -hy, hz), Vector3.forward, true);
            face(1, new Vector3(-hx, -hy, -hz), new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, -hy, -hz), Vector3.back, true);
            face(2, new Vector3(hx, -hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz), new Vector3(hx, -hy, hz), Vector3.right, false);
            face(2, new Vector3(-hx, -hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, hy, -hz), new Vector3(-hx, -hy, -hz), Vector3.left, false);
            face(2, new Vector3(-hx, hy, -hz), new Vector3(-hx, hy, hz), new Vector3(hx, hy, hz), new Vector3(hx, hy, -hz), Vector3.up, false);
            face(2, new Vector3(-hx, -hy, hz), new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(hx, -hy, hz), Vector3.down, false);
            return b.ToMesh(name);
        }

        /// <summary>Flat ring in the XZ plane (normal +Y), outer radius 0.5.</summary>
        public static Mesh Ring(int segments, float innerFraction, string name)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            float ro = 0.5f, ri = 0.5f * Mathf.Clamp01(innerFraction);
            for (int i = 0; i <= segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                v.Add(new Vector3(c * ro, 0f, s * ro));
                v.Add(new Vector3(c * ri, 0f, s * ri));
                uv.Add(new Vector2((float)i / segments, 1f));
                uv.Add(new Vector2((float)i / segments, 0f));
                if (i < segments)
                {
                    int b = i * 2;
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
                }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
