using System;
using System.Collections.Generic;

namespace PolygonApp;

// ВАЖНО: класс называется Poly, чтобы не конфликтовать
// с SixLabors.ImageSharp.Drawing.Polygon и любым другим Polygon.
public class Poly
{
    public List<Vec2> Vertices = new();

    public Poly() { }

    public void Apply(Mat3 m)
    {
        for (int i = 0; i < Vertices.Count; i++)
            Vertices[i] = m.Apply(Vertices[i]);
    }

    public Vec2 Centroid()
    {
        if (Vertices.Count == 0) return new Vec2(0, 0);
        double sx = 0, sy = 0;
        foreach (var v in Vertices) { sx += v.X; sy += v.Y; }
        return new Vec2(sx / Vertices.Count, sy / Vertices.Count);
    }

    public bool ContainsPoint(Vec2 p)
    {
        int n = Vertices.Count;
        if (n < 3) return false;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = Vertices[i];
            var b = Vertices[j];
            if (((a.Y > p.Y) != (b.Y > p.Y)) &&
                (p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X))
                inside = !inside;
        }
        return inside;
    }

    public bool ContainsPointConvex(Vec2 p)
    {
        int n = Vertices.Count;
        if (n < 3) return false;
        int sign = 0;
        for (int i = 0; i < n; i++)
        {
            var a = Vertices[i];
            var b = Vertices[(i + 1) % n];
            double cr = Geom.Cross(b - a, p - a);
            if (Math.Abs(cr) < 1e-9) continue;
            int s = cr > 0 ? 1 : -1;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return true;
    }

    public bool IsConvex()
    {
        int n = Vertices.Count;
        if (n < 3) return true;
        int sign = 0;
        for (int i = 0; i < n; i++)
        {
            var a = Vertices[i];
            var b = Vertices[(i + 1) % n];
            var c = Vertices[(i + 2) % n];
            double cr = Geom.Cross(b - a, c - b);
            if (Math.Abs(cr) < 1e-9) continue;
            int s = cr > 0 ? 1 : -1;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return true;
    }
}

public static class GeomAlgorithms
{
    public static Vec2? SegmentIntersection(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        Vec2 r = b - a;
        Vec2 s = d - c;
        double denom = Geom.Cross(r, s);
        if (Math.Abs(denom) < 1e-12) return null;

        Vec2 ca = c - a;
        double t = Geom.Cross(ca, s) / denom;
        double u = Geom.Cross(ca, r) / denom;

        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
            return a + r * t;
        return null;
    }

    public static int SideOfEdge(Vec2 a, Vec2 b, Vec2 p)
    {
        double cr = Geom.Cross(b - a, p - a);
        if (Math.Abs(cr) < 1e-9) return 0;
        return cr > 0 ? 1 : -1;
    }

    public static double PointSegmentDist(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-12) return (p - a).Length;
        double t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2;
        t = Math.Clamp(t, 0, 1);
        var proj = new Vec2(a.X + ab.X * t, a.Y + ab.Y * t);
        return (p - proj).Length;
    }
}