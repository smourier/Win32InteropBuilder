using System.Collections.Generic;

namespace Win32InteropBuilder
{
    public class BuilderPatches
    {
        public IList<BuilderPatchType> Types { get; set; } = [];
        public IList<BuilderPatchMember> Methods { get; set; } = [];
    }
}
