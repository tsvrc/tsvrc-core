using System.Runtime.CompilerServices;

// Lets Tests/Editor reach the internal CodeGen classes (UdonWriter, TsModule
// helpers, module slot-count/parsing logic, etc.) directly instead of via
// reflection.
[assembly: InternalsVisibleTo("Tsvrc.Tests.Editor")]
