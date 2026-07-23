using System;

namespace Tsvrc.Core
{
    /// <summary>
    /// Marks a class as intentionally not a real codegen scaffold candidate - e.g. a test
    /// double subclass used to unit-test a consumer's behavior. TsGenerator's
    /// bootstrap-detection scan and InstanceModule's scaffold-instance scan both skip any
    /// type carrying this attribute, so it's never mistaken for the user's real scaffold and
    /// never gets an unwanted (and failing, since it has no matching generated program
    /// asset) Wire() attempt.
    ///
    /// Deliberately a plain, inert marker with no members and no runtime behavior - it exists
    /// purely so codegen can tell "this type happens to satisfy the scan's type check, but
    /// isn't what the scan is looking for" precisely, per-type, regardless of which assembly
    /// the type lives in or how that assembly's asmdef is configured.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TsCodegenIgnoreAttribute : Attribute
    {
    }
}
