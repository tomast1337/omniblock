namespace OmniBlock.Client.Rendering.PathMovers;

public static class PathMoverUpdater
{
    /// <summary>
    ///     Advances every mover's position along its straight-line path. Pure math — no
    ///     <c>IWorldContext</c>, no collision, no AI — the deliberate "no physics needed" case the
    ///     scripting-readiness roadmap's path-mover step calls for.
    /// </summary>
    public static void Tick(PathMoverBuffer buf)
    {
        var count = buf.Count;
        if (count == 0)
        {
            return;
        }

        Array.Copy(buf.X, buf.PrevX, count);
        Array.Copy(buf.Y, buf.PrevY, count);
        Array.Copy(buf.Z, buf.PrevZ, count);

        for (var i = 0; i < count; i++)
        {
            buf.Progress[i] += buf.ProgressPerTick[i];
            if (buf.Progress[i] >= 1f)
            {
                buf.Dead[i] = true;
                continue;
            }

            var t = buf.Progress[i];
            buf.X[i] = buf.StartX[i] + (buf.EndX[i] - buf.StartX[i]) * t;
            buf.Y[i] = buf.StartY[i] + (buf.EndY[i] - buf.StartY[i]) * t;
            buf.Z[i] = buf.StartZ[i] + (buf.EndZ[i] - buf.StartZ[i]) * t;
        }

        for (var i = buf.Count - 1; i >= 0; i--)
        {
            if (buf.Dead[i]) buf.SwapRemove(i);
        }
    }
}
