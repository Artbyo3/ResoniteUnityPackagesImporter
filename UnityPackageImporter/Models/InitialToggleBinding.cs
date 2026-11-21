#nullable enable
using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

internal enum InitialToggleBindingResult { Bound, NoTargets, MixedStates, AlreadyDriven }






internal static class InitialToggleBinding
{
    public static InitialToggleBindingResult Bind<T>(IReadOnlyList<T> targets,
        Func<T, bool> currentState, Func<T, bool> alreadyDriven,
        Action<bool> initializeControl, Action<T> attachTarget)
    {
        if (targets.Count == 0) return InitialToggleBindingResult.NoTargets;
        bool initial = currentState(targets[0]);
        foreach (var target in targets)
        {
            if (alreadyDriven(target)) return InitialToggleBindingResult.AlreadyDriven;
            if (currentState(target) != initial) return InitialToggleBindingResult.MixedStates;
        }
        initializeControl(initial);
        foreach (var target in targets) attachTarget(target);
        return InitialToggleBindingResult.Bound;
    }
}
