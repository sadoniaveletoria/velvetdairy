using System;
using System.Numerics;

namespace VelvetDairy.Game;

/// <summary>
/// Where a dairy is: a box on the ground anywhere in the world (open world, a city, or inside one specific house).
/// The owner stands in the middle, sizes it and turns it; the server makes sure boxes don't overlap.
/// </summary>
[Serializable]
public class DairyArea
{
    public uint Territory { get; set; }
    public uint World { get; set; }
    public string House { get; set; } = "";   // the house's id when the box is inside a house, "" outdoors
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Width { get; set; } = 16;      // side to side
    public float Length { get; set; } = 12;     // front to back
    public float Height { get; set; } = 6;      // how far above or below the floor still counts
    public float Rot { get; set; }              // which way it faces (radians, like the game)
    public uint MapId { get; set; }
    public string Zone { get; set; } = "";

    public const float MinSize = 4, MaxSize = 80;

    public Vector3 Center => new(X, Y, Z);
    private Vector3 Forward => new(MathF.Sin(Rot), 0, MathF.Cos(Rot));
    private Vector3 Right => new(MathF.Cos(Rot), 0, -MathF.Sin(Rot));

    /// <summary>The four corners on the floor: front-right, back-right, back-left, front-left.</summary>
    public Vector3[] Corners()
    {
        var f = Forward * (Length / 2);
        var r = Right * (Width / 2);
        var c = Center;
        return new[] { c + f + r, c - f + r, c - f - r, c + f - r };
    }

    public bool Contains(Vector3 p)
    {
        if (MathF.Abs(p.Y - Y) > Height) return false;
        var d = p - Center;
        var along = d.X * MathF.Sin(Rot) + d.Z * MathF.Cos(Rot);
        var across = d.X * MathF.Cos(Rot) - d.Z * MathF.Sin(Rot);
        return MathF.Abs(along) <= Length / 2 && MathF.Abs(across) <= Width / 2;
    }

    public DairyArea Copy() => (DairyArea)MemberwiseClone();
}
