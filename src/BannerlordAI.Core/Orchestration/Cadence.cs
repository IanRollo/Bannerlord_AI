namespace BannerlordAI.Core.Orchestration;

public static class Cadence
{
    /// <summary>
    /// Lieutenant call interval for a given distance between the armies: fastest when close, slowest when far, and
    /// null (idle) at or beyond the far distance.
    /// </summary>
    public static double? LieutenantInterval(double minEnemyDistance, CadenceConfig c)
    {
        if (double.IsInfinity(minEnemyDistance) || double.IsNaN(minEnemyDistance)) return null;
        if (minEnemyDistance >= c.FarDistance) return null;
        if (minEnemyDistance <= c.NearDistance) return c.LieutenantNearInterval;
        var t = (minEnemyDistance - c.NearDistance) / (c.FarDistance - c.NearDistance);
        return c.LieutenantNearInterval + t * (c.LieutenantFarInterval - c.LieutenantNearInterval);
    }
}
