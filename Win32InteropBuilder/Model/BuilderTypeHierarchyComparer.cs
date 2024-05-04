using System;
using System.Collections.Generic;

namespace Win32InteropBuilder.Model
{
    // presumes types are in the same hierarchy
    public class BuilderTypeHierarchyComparer : IComparer<BuilderType>
    {
        public static BuilderTypeHierarchyComparer Instance { get; } = new();

        public int Compare(BuilderType? x, BuilderType? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);

            if (x.Interfaces.Count == 0)
            {
                if (y.Interfaces.Count == 0)
                    return x.FullName.Name.CompareTo(y.FullName.Name);

                return -1;
            }
            else if (y.Interfaces.Count == 0)
                return 1;

            if (x.Interfaces.Contains(y))
                return 1;

            if (y.Interfaces.Contains(x))
                return -1;

            return x.FullName.Name.CompareTo(y.FullName.Name);
        }
    }
}
