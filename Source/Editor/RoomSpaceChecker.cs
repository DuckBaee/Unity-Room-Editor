using System.Collections.Generic;
using UnityEngine;

// Convex subtraction, not a finite set of witness points.
internal static class RoomSpaceChecker
{
    private const float Epsilon = 1e-5f;
    private const int MaxPieces = 4096;
    private const int MaxCuts = 200000;
    public struct CheckResult
    {
        public List<string> violations;
        public List<string> skipped;
        public List<string> unverified;
        public bool HasViolations => violations != null && violations.Count > 0;
        public bool IsUnverified => unverified != null && unverified.Count > 0;
    }

    public static CheckResult Run(GameObject roomRoot, List<Vector3Int> cells, RoomPortalAuthoring[] portals)
    {
        var result = new CheckResult { violations = new List<string>(), skipped = new List<string>(), unverified = new List<string>() };
        if (roomRoot == null) { result.unverified.Add("방 루트 없음"); return result; }
        var regions = new List<Poly>();
        if (cells != null)
            foreach (var cell in new HashSet<Vector3Int>(cells))
                regions.Add(Box(roomRoot.transform.localToWorldMatrix, cell + Vector3.one * .5f, Vector3.one));
        if (portals != null)
            foreach (var p in portals)
                if (p != null && p.reservationDepth > 0)
                    regions.Add(Box(Matrix4x4.TRS(p.transform.position, p.transform.rotation, Vector3.one),
                        Vector3.back * p.reservationDepth * .5f,
                        new Vector3(p.openingSize.x, p.openingSize.y, p.reservationDepth)));

        foreach (var t in roomRoot.GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<Renderer>() != null && t.GetComponentInParent<RoomPortalAuthoring>() == null &&
                t.GetComponentsInChildren<Collider>(true).Length == 0 && t.GetComponentInParent<Collider>() == null)
                result.skipped.Add("Collider 없음: " + t.name);
        foreach (var col in roomRoot.GetComponentsInChildren<Collider>(true))
        {
            if (col.GetComponentInParent<RoomPortalAuthoring>() != null) { result.skipped.Add("포털 하위: " + col.name); continue; }
            if (col.isTrigger) { result.skipped.Add("Trigger: " + col.name); continue; }
            // PhysX primitive shapes do not reproduce a sheared Transform matrix.
            // Do not certify the rendered parallelepiped as the physics collider.
            if (!(col is MeshCollider) && HasShear(col.transform.localToWorldMatrix))
            { result.unverified.Add("전단 변환 Collider: " + col.name); continue; }
            var shapes = new List<Poly>();
            bool round = false;
            Vector3 endA = Vector3.zero, endB = Vector3.zero;
            float radius = 0;
            if (col is BoxCollider box)
                shapes.Add(Box(box.transform.localToWorldMatrix, box.center, box.size));
            else if (col is SphereCollider sphere)
            {
                round = true;
                endA = endB = sphere.transform.TransformPoint(sphere.center);
                var scale = sphere.transform.lossyScale;
                radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            }
            else if (col is CapsuleCollider capsule)
            {
                round = true;
                var scale = capsule.transform.lossyScale;
                float along = Mathf.Abs(scale[capsule.direction]);
                float across = Mathf.Max(Mathf.Abs(scale[(capsule.direction+1)%3]), Mathf.Abs(scale[(capsule.direction+2)%3]));
                radius = capsule.radius * across;
                float halfSegment = Mathf.Max(0, capsule.height * along * .5f - radius);
                var axis = capsule.transform.rotation * (capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward);
                var center = capsule.transform.TransformPoint(capsule.center);
                endA = center + axis * halfSegment; endB = center - axis * halfSegment;
            }
            else if (col is MeshCollider mesh)
            {
                if (mesh.convex || mesh.sharedMesh == null)
                {
                    result.unverified.Add("Convex cooked hull 또는 누락된 Mesh: " + col.name); continue;
                }
                try
                {
                    var vertices = mesh.sharedMesh.vertices; var triangles = mesh.sharedMesh.triangles;
                    if (triangles.Length == 0 || triangles.Length > 60000)
                    { result.unverified.Add("Mesh 삼각형 없음/검사 한도 초과: " + col.name); continue; }
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        var triangle = new Poly { surface = true };
                        triangle.faces.Add(new List<Vector3> {
                            mesh.transform.TransformPoint(vertices[triangles[i]]),
                            mesh.transform.TransformPoint(vertices[triangles[i+1]]),
                            mesh.transform.TransformPoint(vertices[triangles[i+2]]) });
                        if (triangle.HasMeasure()) shapes.Add(triangle);
                    }
                }
                catch (System.Exception) { result.unverified.Add("Mesh geometry 읽기 실패: " + col.name); continue; }
            }
            else { result.unverified.Add("지원 제한: " + col.name + " [" + col.GetType().Name + "]"); continue; }
            if (round)
            {
                if (radius <= Epsilon) { result.unverified.Add("퇴화한 Collider: " + col.name); continue; }
                var min = Vector3.Min(endA,endB)-Vector3.one*radius;
                var max = Vector3.Max(endA,endB)+Vector3.one*radius;
                shapes.Add(Box(Matrix4x4.identity,(min+max)*.5f,max-min));
            }
            bool outside = false, limited = false;
            int cuts = 0;
            if (shapes.Count == 0) { result.unverified.Add("유효한 geometry 없음: " + col.name); continue; }
            foreach (var shape in shapes)
            {
                if (!shape.HasMeasure()) { limited = true; break; }
                var pieces = Subtract(shape, regions, ref cuts, out limited);
                if (limited) break;
                foreach (var piece in pieces)
                    if (!round || piece.DistanceSquaredToSegment(endA,endB) < (radius-Epsilon)*(radius-Epsilon))
                    { outside = true; break; }
                if (outside) break;
            }
            if (limited) result.unverified.Add("퇴화 형상/검사 연산 한도 초과: " + col.name);
            else if (outside) result.violations.Add(col.name + " [" + col.GetType().Name + "] @ " + roomRoot.transform.InverseTransformPoint(col.transform.position));
        }
        return result;
    }

    private static bool HasShear(Matrix4x4 matrix)
    {
        Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
        if (x.sqrMagnitude < 1e-12f || y.sqrMagnitude < 1e-12f || z.sqrMagnitude < 1e-12f) return true;
        return Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > Epsilon ||
               Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > Epsilon ||
               Mathf.Abs(Vector3.Dot(z.normalized, x.normalized)) > Epsilon;
    }

    private static List<Poly> Subtract(Poly shape, List<Poly> regions, ref int cuts, out bool limited)
    {
        limited = false;
        var pieces = new List<Poly> { shape };
        foreach (var region in regions)
        {
            var next = new List<Poly>();
            foreach (var piece in pieces)
            {
                if (!piece.Bounds.Intersects(region.Bounds)) { next.Add(piece); continue; }
                var inside = piece;
                foreach (var plane in region.Planes())
                {
                    if (++cuts > MaxCuts) { limited = true; return pieces; }
                    // A coplanar surface belongs to the closed allowed region.
                    bool crossesOutside = false;
                    foreach (var f in inside.faces) foreach (var v in f)
                        if (plane.GetDistanceToPoint(v) > Epsilon) crossesOutside = true;
                    if (crossesOutside)
                    {
                        var outside = inside.Clip(new Plane(-plane.normal,-plane.distance));
                        if (outside.HasMeasure()) next.Add(outside);
                    }
                    inside = inside.Clip(plane);
                    if (!inside.HasMeasure()) break;
                }
                if (next.Count > MaxPieces) { limited = true; return pieces; }
            }
            pieces = next;
            if (pieces.Count == 0) break;
        }
        return pieces;
    }

    private static Poly Box(Matrix4x4 matrix, Vector3 center, Vector3 size)
    {
        var v = new Vector3[8];
        for (int i = 0; i < 8; i++) v[i] = matrix.MultiplyPoint3x4(center + Vector3.Scale(size * .5f,
            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
        var p = new Poly();
        foreach (var indices in new[] {new[]{0,2,3,1},new[]{4,5,7,6},new[]{0,1,5,4},new[]{2,6,7,3},new[]{0,4,6,2},new[]{1,3,7,5}})
        {
            var face = new List<Vector3>(); foreach (int i in indices) face.Add(v[i]); p.faces.Add(face);
        }
        return p;
    }

    private sealed class Poly
    {
        public bool surface;
        public readonly List<List<Vector3>> faces = new List<List<Vector3>>();
        public Bounds Bounds
        {
            get { var b = new Bounds(faces[0][0], Vector3.zero); foreach (var f in faces) foreach (var v in f) b.Encapsulate(v); return b; }
        }
        private Vector3 Center()
        {
            Vector3 c = Vector3.zero; int n = 0;
            foreach (var f in faces) foreach (var v in f) { c += v; n++; }
            return n == 0 ? c : c / n;
        }
        public bool HasMeasure()
        {
            if (!surface) return HasVolume();
            double area = 0;
            foreach (var f in faces) for (int i=1; i+1<f.Count; i++) area += Vector3.Cross(f[i]-f[0],f[i+1]-f[0]).magnitude*.5;
            return area > 1e-10;
        }
        public bool HasVolume()
        {
            if (faces.Count < 4) return false;
            var c = Center(); double volume = 0;
            foreach (var f in faces)
                for (int i = 1; i + 1 < f.Count; i++)
                    volume += System.Math.Abs(Vector3.Dot(f[0] - c, Vector3.Cross(f[i] - c, f[i+1] - c))) / 6.0;
            return volume > 1e-10;
        }
        public IEnumerable<Plane> Planes()
        {
            var c = Center();
            foreach (var f in faces)
            {
                Vector3 normal = Vector3.zero;
                for (int i=1; i+1<f.Count && normal.sqrMagnitude<1e-16f; i++) normal=Vector3.Cross(f[i]-f[0],f[i+1]-f[0]);
                if (normal.sqrMagnitude < 1e-16f) continue;
                var p = new Plane(normal.normalized, f[0]);
                if (p.GetDistanceToPoint(c) > 0) p = new Plane(-p.normal, -p.distance);
                yield return p;
            }
        }
        public Poly Clip(Plane p)
        {
            var result = new Poly { surface = surface }; var cap = new List<Vector3>();
            foreach (var face in faces)
            {
                var output = new List<Vector3>();
                for (int i = 0; i < face.Count; i++)
                {
                    var a = face[i]; var b = face[(i + 1) % face.Count];
                    float da = p.GetDistanceToPoint(a), db = p.GetDistanceToPoint(b);
                    // Snap near-plane points consistently to prevent cracks/slivers.
                    if (Mathf.Abs(da) <= Epsilon) { a -= p.normal * da; da = 0; }
                    if (Mathf.Abs(db) <= Epsilon) { b -= p.normal * db; db = 0; }
                    if (da <= 0) AddUnique(output, a);
                    if ((da < 0 && db > 0) || (da > 0 && db < 0))
                    {
                        var q = a + (b - a) * (da / (da - db)); AddUnique(output, q); AddUnique(cap, q);
                    }
                    if (da == 0) AddUnique(cap, a);
                }
                if (output.Count >= 3) result.faces.Add(output);
            }
            if (!surface && cap.Count >= 3)
            {
                Vector3 c = Vector3.zero; foreach (var v in cap) c += v; c /= cap.Count;
                var u = Vector3.Cross(p.normal, Mathf.Abs(p.normal.y) < .9f ? Vector3.up : Vector3.right).normalized;
                var vAxis = Vector3.Cross(p.normal, u);
                cap.Sort((a,b) => Mathf.Atan2(Vector3.Dot(a-c,vAxis),Vector3.Dot(a-c,u)).CompareTo(Mathf.Atan2(Vector3.Dot(b-c,vAxis),Vector3.Dot(b-c,u))));
                result.faces.Add(cap);
            }
            return result;
        }
        // Capsule = segment swept by a sphere. Intersecting a remaining convex
        // piece is equivalent to segment-to-piece distance smaller than radius.
        public float DistanceSquaredToSegment(Vector3 a, Vector3 b)
        {
            float enter=0, exit=1; bool intersects=true;
            foreach (var p in Planes())
            {
                float da=p.GetDistanceToPoint(a), delta=Vector3.Dot(p.normal,b-a);
                if (Mathf.Abs(delta)<1e-10f) { if(da>0) { intersects=false; break; } }
                else { float t=-da/delta; if(delta>0) exit=Mathf.Min(exit,t); else enter=Mathf.Max(enter,t); }
                if(enter>exit) { intersects=false; break; }
            }
            if(intersects) return 0;
            float best=float.PositiveInfinity;
            foreach(var f in faces) for(int i=1;i+1<f.Count;i++)
                best=Mathf.Min(best, SegmentTriangle(a,b,f[0],f[i],f[i+1]));
            return best;
        }
        private static float SegmentTriangle(Vector3 a,Vector3 b,Vector3 x,Vector3 y,Vector3 z)
        {
            var n=Vector3.Cross(y-x,z-x); float den=Vector3.Dot(n,b-a);
            if(Mathf.Abs(den)>1e-12f)
            {
                float t=Vector3.Dot(n,x-a)/den;
                if(t>=0&&t<=1&&PointTriangle(a+(b-a)*t,x,y,z)<1e-12f) return 0;
            }
            return Mathf.Min(PointTriangle(a,x,y,z),PointTriangle(b,x,y,z),
                SegmentSegment(a,b,x,y),SegmentSegment(a,b,y,z),SegmentSegment(a,b,z,x));
        }
        private static float SegmentSegment(Vector3 p,Vector3 q,Vector3 a,Vector3 b)
        {
            var d=q-p; var e=b-a; var r=p-a;
            float dd=Vector3.Dot(d,d), ee=Vector3.Dot(e,e), de=Vector3.Dot(d,e), dr=Vector3.Dot(d,r), er=Vector3.Dot(e,r);
            float s=0,t=0;
            if(dd<=1e-12f) t=ee<=1e-12f?0:Mathf.Clamp01(er/ee);
            else if(ee<=1e-12f) s=Mathf.Clamp01(-dr/dd);
            else
            {
                float denom=dd*ee-de*de;
                s=denom>1e-12f?Mathf.Clamp01((de*er-dr*ee)/denom):0;
                t=(de*s+er)/ee;
                if(t<0) {t=0;s=Mathf.Clamp01(-dr/dd);}
                else if(t>1) {t=1;s=Mathf.Clamp01((de-dr)/dd);}
            }
            return (r+d*s-e*t).sqrMagnitude;
        }
        private static float PointTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0&&d2<=0)return ap.sqrMagnitude;
            var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0&&d4<=d3)return bp.sqrMagnitude;
            float vc=d1*d4-d3*d2;
            if(vc<=0&&d1>=0&&d3<=0)return (p-(a+ab*(d1/(d1-d3)))).sqrMagnitude;
            var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0&&d5<=d6)return cp.sqrMagnitude;
            float vb=d5*d2-d1*d6;
            if(vb<=0&&d2>=0&&d6<=0)return (p-(a+ac*(d2/(d2-d6)))).sqrMagnitude;
            float va=d3*d6-d5*d4;
            if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0)return (p-(b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6))))).sqrMagnitude;
            float sum=va+vb+vc;
            if(Mathf.Abs(sum)<1e-20f)return Mathf.Min((p-a).sqrMagnitude,(p-b).sqrMagnitude,(p-c).sqrMagnitude);
            return (p-(a+ab*(vb/sum)+ac*(vc/sum))).sqrMagnitude;
        }

        private static void AddUnique(List<Vector3> list, Vector3 p)
        {
            foreach (var q in list) if ((p-q).sqrMagnitude < 1e-12f) return;
            list.Add(p);
        }
    }
}
