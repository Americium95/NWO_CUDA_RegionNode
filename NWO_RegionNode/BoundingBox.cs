using System.Numerics;

public sealed class BoundingBox
{
    public Vector3 Center;
    public Vector3 HalfSize;

    public BoundingBox(Vector3 center, Vector3 halfSize)
    {
        Center = center;
        HalfSize = halfSize;
    }
}