using System;
using System.Collections.Generic;
using System.Linq;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>Finds every <see cref="IZoneGenerator"/> in the loaded assemblies.</summary>
    public static class ZoneGenerators
    {
        public static IReadOnlyList<IZoneGenerator> All()
        {
            var list = new List<IZoneGenerator>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsInterface || !typeof(IZoneGenerator).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    list.Add((IZoneGenerator)Activator.CreateInstance(t));
                }
            }
            return list.OrderBy(g => g.Name).ToList();
        }

        public static IZoneGenerator Find(string name)
        {
            var all = All();
            var g = all.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (g == null) throw new ArgumentException($"No zone generator named '{name}'. Found: {string.Join(", ", all.Select(x => x.Name))}");
            return g;
        }
    }
}
