using System;

namespace Tsvrc.Core
{
    /// <summary>
    /// Marks a field as declaring its intent to be available from a pool.
    /// Use this attribute on any class field to document which types you want pooled.
    /// The compiler will validate that all [WirePool] types are registered in TsvrcConfig.PooledObjects.
    ///
    /// If the pooled type is a TsvrcBehaviour subclass, the pool slots will have TsInitialize called
    /// at scene start. For other types, they are simply wired as references.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class WirePoolAttribute : Attribute
    {
        /// <summary>
        /// Optional description of what this pool is for.
        /// </summary>
        public string Description { get; set; }

        public WirePoolAttribute() { }

        public WirePoolAttribute(string description)
        {
            Description = description;
        }
    }
}
