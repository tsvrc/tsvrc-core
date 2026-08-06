using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// Shared base for real, ClientSim-backed Play Mode tests. Applies every active
    /// IPlayModeEnvironmentFixup at the matching lifecycle point. Extend this for any new Play
    /// Mode test file instead of re-deriving the same ClientSim setup/teardown by hand.
    /// </summary>
    public abstract class TsPlayModeTestBase
    {
        protected readonly ClientSimPlayerEnvironment Players = new ClientSimPlayerEnvironment();

        private readonly List<Action> _rootBuilderTeardowns = new List<Action>();

        [OneTimeSetUp]
        public void TsPlayModeTestBase_OneTimeSetUp()
        {
            foreach (var fixup in FixupRegistry.ActiveFixups)
                fixup.OnBeforeAnyTests();
        }

        [UnityTearDown]
        public IEnumerator TsPlayModeTestBase_UnityTearDown()
        {
            Players.Teardown();

            foreach (var teardown in _rootBuilderTeardowns)
                teardown();
            _rootBuilderTeardowns.Clear();

            foreach (var fixup in FixupRegistry.ActiveFixups)
                fixup.OnUnityTearDown();

            yield return null;
        }

        protected IEnumerator StartClientSim(bool localPlayerIsMaster = true)
        {
            return Players.Start(localPlayerIsMaster);
        }

        /// <summary>Starts composing a project's generated composition root from code - see
        /// TsRootBuilder's own doc comment for why this replaces loading a saved scene. The
        /// builder's spawned GameObjects are torn down automatically alongside ClientSim.</summary>
        protected TsRootBuilder<TRoot> BuildTsRoot<TRoot>() where TRoot : Component
        {
            var builder = new TsRootBuilder<TRoot>();
            _rootBuilderTeardowns.Add(builder.Teardown);
            return builder;
        }
    }
}
