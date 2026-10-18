namespace StormBench.Domain.Control;

public enum EfdVariant
{
    /// <summary>EFD_Control.txt as EPA SWMM executes it: only "the strictly fullest tank opens fully" acts.</summary>
    Published,

    /// <summary>Rule priorities fixed (SWMM-Astlingen PR #1): 1 m threshold and throttling of the emptiest tank.</summary>
    Intended
}

/// <summary>Station parameters of the controlled outlets, in PLC station order 1..4 (V2, V3, V4, V6).</summary>
public sealed class EfdParameters
{
    public double[] BaseSetting { get; set; } = [0.2366, 0.6508, 0.3523, 0.4303];
    public double[] LowSetting { get; set; } = [0.1075, 0.3159, 0.1894, 0.1687];
    public double ThresholdDepth { get; set; } = 1.0;
    public EfdVariant Variant { get; set; } = EfdVariant.Published;
}

/// <summary>
/// Equal Filling Degree, identical to FB_EfdCoordinator in plc/src/fb_efd_coordinator.st and
/// efd_coordinator in simulator/stormbench_sim/control.py. Only comparisons and constants, so the
/// three implementations agree bit for bit. Comparisons are strict; ties keep the Base Case setting.
/// </summary>
public static class EfdCoordinator
{
    public static double[] Propose(ReadOnlySpan<double> depth, EfdParameters parameters)
    {
        var n = depth.Length;
        if (parameters.BaseSetting.Length != n || parameters.LowSetting.Length != n)
        {
            throw new ArgumentException("one base and one low setting per controlled tank is required");
        }
        var intended = parameters.Variant is EfdVariant.Intended;

        var allBelow = true;
        for (var i = 0; i < n; i++)
        {
            if (!(depth[i] < parameters.ThresholdDepth))
            {
                allBelow = false;
            }
        }

        var proposal = new double[n];
        for (var i = 0; i < n; i++)
        {
            var fullest = true;
            var emptiest = true;
            for (var j = 0; j < n; j++)
            {
                if (j == i)
                {
                    continue;
                }
                if (!(depth[i] > depth[j]))
                {
                    fullest = false;
                }
                if (!(depth[i] < depth[j]))
                {
                    emptiest = false;
                }
            }

            if (intended && allBelow)
            {
                proposal[i] = parameters.BaseSetting[i];
            }
            else if (fullest)
            {
                proposal[i] = 1.0;
            }
            else if (intended && emptiest)
            {
                proposal[i] = parameters.LowSetting[i];
            }
            else
            {
                proposal[i] = parameters.BaseSetting[i];
            }
        }
        return proposal;
    }
}
