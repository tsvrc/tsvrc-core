using System.Runtime.CompilerServices;

// BatchModeTerminationFixupTests needs to override its internal exit/isPlaying seams to
// verify OnAfterAllTests' branching without actually exiting the process or Play Mode.
[assembly: InternalsVisibleTo("Tsvrc.Tests.EditMode")]
