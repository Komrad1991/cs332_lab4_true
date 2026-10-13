using System;

namespace PolygonApp;

public struct Vec2
{
    public double X, Y;
    public Vec2(double x, double y) { X = x; Y = y; }

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public SixLabors.ImageSharp.PointF ToPointF() =>
        new SixLabors.ImageSharp.PointF((float)X, (float)Y);
}

/// <summary>Аффинное преобразование 3×3 (однородные координаты).</summary>
public class Mat3
{
    public double[,] M = new double[3, 3];

    public static Mat3 Identity()
    {
        var m = new Mat3();
        m.M[0, 0] = m.M[1, 1] = m.M[2, 2] = 1.0;
        return m;
    }

    public static Mat3 Translation(double dx, double dy)
    {
        var m = Identity();
        m.M[0, 2] = dx; m.M[1, 2] = dy;
        return m;
    }

    public static Mat3 Rotation(double angleRad, Vec2 pivot)
    {
        double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
        var r = Identity();
        r.M[0, 0] = c; r.M[0, 1] = -s;
        r.M[1, 0] = s; r.M[1, 1] = c;
        return Translation(pivot.X, pivot.Y) * r * Translation(-pivot.X, -pivot.Y);
    }

    public static Mat3 Scale(double kx, double ky, Vec2 pivot)
    {
        var s = Identity();
        s.M[0, 0] = kx; s.M[1, 1] = ky;
        return Translation(pivot.X, pivot.Y) * s * Translation(-pivot.X, -pivot.Y);
    }

    public static Mat3 operator *(Mat3 a, Mat3 b)
    {
        var r = new Mat3();
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double sum = 0;
                for (int k = 0; k < 3; k++) sum += a.M[i, k] * b.M[k, j];
                r.M[i, j] = sum;
            }
        return r;
    }

    public Vec2 Apply(Vec2 p) => new(
        M[0, 0] * p.X + M[0, 1] * p.Y + M[0, 2],
        M[1, 0] * p.X + M[1, 1] * p.Y + M[1, 2]);
}

public static class Geom
{
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    public static double Dist(Vec2 a, Vec2 b) => (a - b).Length;
}