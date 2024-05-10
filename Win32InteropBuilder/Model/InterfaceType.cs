using System.Linq;
using System.Reflection.Metadata;

namespace Win32InteropBuilder.Model
{
    public class InterfaceType(FullName fullName) : BuilderType(fullName)
    {
        public virtual bool IsIUnknownDerived { get; set; }

        protected override void CopyTo(BuilderType copy)
        {
            base.CopyTo(copy);
            if (copy is InterfaceType typed)
            {
                typed.IsIUnknownDerived = IsIUnknownDerived;
            }
        }

        protected internal override void ResolveType(BuilderContext context, TypeDefinition typeDef)
        {
            base.ResolveType(context, typeDef);
            foreach (var match in context.Configuration.Generation.InterfaceExtensions.Where(x => x.Matches(this)))
            {
                // we want to build one extension file per interface hierarchy
                var lastIFace = AllInterfaces.LastOrDefault() ?? this;
                if (!context.Extensions.TryGetValue(lastIFace.FullName, out var ext))
                {
                    ext = context.CreateTypeExtension(lastIFace);
                    if (ext != null)
                    {
                        context.Extensions[lastIFace.FullName] = ext;
                    }
                }

                if (ext == null)
                    continue;

                if (ext.RootType != this)
                {
                    ext.Types.Add(this);
                }
            }
        }
    }
}
