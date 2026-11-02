using System.Collections;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// Optional: a generator that can build a zone a little at a time, so the game can stream zones in while it runs.
    /// </summary>
    public interface IIncrementalZoneGenerator : IZoneGenerator
    {
        /// <summary>
        /// Builds exactly what <see cref="IZoneGenerator.Build"/> builds, filling <paramref name="result"/>, but yields
        /// between short pieces of main-thread work (aim for a few milliseconds each). Pure computation may run on
        /// worker threads while it yields. The result must not depend on how many frames the steps are spread over.
        /// </summary>
        IEnumerator BuildSteps(ZoneRequest request, Transform parent, ZoneResult result);
    }

    /// <summary>Runs any generator as steps: incremental generators yield often, the others build in one go.</summary>
    public static class ZoneBuild
    {
        public static IEnumerator Steps(IZoneGenerator generator, ZoneRequest request, Transform parent, ZoneResult result)
        {
            if (generator is IIncrementalZoneGenerator incremental) return incremental.BuildSteps(request, parent, result);
            return Whole(generator, request, parent, result);
        }

        /// <summary>Runs the steps to the end right now (nested enumerators included).</summary>
        public static void RunToEnd(IEnumerator steps)
        {
            var stack = new System.Collections.Generic.Stack<IEnumerator>();
            stack.Push(steps);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested) stack.Push(nested);
            }
        }

        private static IEnumerator Whole(IZoneGenerator generator, ZoneRequest request, Transform parent, ZoneResult result)
        {
            var built = generator.Build(request, parent);
            result.Root = built.Root;
            result.Notes = built.Notes;
            result.HeroViews.AddRange(built.HeroViews);
            yield break;
        }
    }
}
