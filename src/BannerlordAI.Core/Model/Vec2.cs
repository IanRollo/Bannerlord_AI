using System;
using System.Globalization;

namespace BannerlordAI.Core.Model;

/// <summary>Minimal 2D vector, independent of TaleWorlds.Library.Vec2.</summary>
public readonly struct Vec2 : IEquatable<Vec2>
{
    public readonly float X;
    public readonly float Y;

    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static readonly Vec2 Zero = new(0f, 0f);

    public float Length => (float)Math.Sqrt(X * X + Y * Y);

    public float LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        var len = Length;
        return len < 1e-5f ? Zero : new Vec2(X / len, Y / len);
    }

    public float Dot(Vec2 other) => X * other.X + Y * other.Y;

    public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(float s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, float s) => new(a.X / s, a.Y / s);
    public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
    public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

    public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object? obj) => obj is Vec2 v && Equals(v);
    public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());

    public override string ToString() =>
        "(" + X.ToString("0", CultureInfo.InvariantCulture) + "," + Y.ToString("0", CultureInfo.InvariantCulture) + ")";
}
