using System;

namespace Tsvrc.Core
{
    /// <summary>
    /// Marks a Tsvrc framework class that world scripts are meant to subclass directly
    /// (for example <c>MolInstance : TsInstance</c>). ScaffoldModule reflects over this
    /// attribute to generate a shadow class named <see cref="GeneratedName"/>, the one that
    /// hides the inherited <c>_ts</c> field with a same-named property retyped to the
    /// project's concrete generated root.
    /// </summary>
    /// <remarks>
    /// <see cref="GeneratedName"/> is explicit rather than derived from the tagged class's
    /// own name because the internal and generated names deliberately diverge (for example
    /// <see cref="TsvrcBehaviour"/> generates <c>TsBehaviour</c>): the internal class keeps a
    /// distinguishing prefix or suffix where a bare name would collide with something else
    /// (<c>UnityEngine.Behaviour</c> for <see cref="TsvrcBehaviour"/>), while the generated
    /// shadow gets the clean name world scripts actually extend. Adding a new Tsvrc base
    /// class that world scripts should extend directly only requires tagging it with this
    /// attribute; no other file needs to change.
    /// <c>Inherited</c> is deliberately <c>false</c>: tagging <see cref="TsvrcBehaviour"/>
    /// must not implicitly tag every framework-internal subclass (for example <c>TsvrcLogger</c>,
    /// <c>TsvrcMemory</c>) that isn't itself meant to be extended by world code. Each attachment
    /// point opts in explicitly.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TsWorldExtensionPointAttribute : Attribute
    {
        /// <summary>The clean class name ScaffoldModule generates for world scripts to extend.</summary>
        public string GeneratedName { get; }

        public TsWorldExtensionPointAttribute(string generatedName)
        {
            GeneratedName = generatedName;
        }
    }
}
