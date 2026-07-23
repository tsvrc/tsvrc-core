using System;
using System.Collections.Generic;
using System.Linq;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// The ordered catalogue of known Play Mode testing defects and their fixes.
    /// TsPlayModeTestBase applies every active fixup's matching hook at each lifecycle
    /// point. A consuming world can disable a specific fixup (e.g. if a future VRCSDK
    /// version fixes something upstream and the workaround becomes unnecessary) via
    /// Disable&lt;TFixup&gt;() without forking the framework.
    /// </summary>
    public static class FixupRegistry
    {
        private static readonly List<IPlayModeEnvironmentFixup> AllFixups = new List<IPlayModeEnvironmentFixup>
        {
            new ClientSimPersistenceLeakFixup(),
            new BatchModeTerminationFixup(),
        };

        private static readonly HashSet<Type> DisabledTypes = new HashSet<Type>();

        public static IEnumerable<IPlayModeEnvironmentFixup> ActiveFixups =>
            AllFixups.Where(f => !DisabledTypes.Contains(f.GetType()));

        public static void Disable<TFixup>() where TFixup : IPlayModeEnvironmentFixup =>
            DisabledTypes.Add(typeof(TFixup));

        public static void EnableAll() => DisabledTypes.Clear();
    }
}
