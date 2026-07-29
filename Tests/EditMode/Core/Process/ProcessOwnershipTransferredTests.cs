using NUnit.Framework;
using System.Reflection;
using System;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // OnOwnershipTransferred's own not-Unity-owner guard cannot be exercised here:
    // Networking.IsOwner(gameObject) is unconditionally true for a bare GameObject
    // outside Play Mode (see ProcessTestBase), so that branch stays permanently
    // PlayMode-only. Its other three early returns don't depend on Networking.IsOwner
    // at all and are safe to reach directly. The path beyond all of them —
    // TakeOverAbandonedProcess via SetProcessOwner(Networking.LocalPlayer) — is not
    // covered here either: Networking.LocalPlayer is null outside Play Mode, so calling
    // it would throw. That leaves the _ownerId == "" guard as the one that matters most:
    // without it, FindPlayerByID("") returning null (GetAllPlayers() is empty here) would
    // fall through into that same crash.
    public class ProcessOwnershipTransferredTests : ProcessTestBase
    {
        // OnOwnershipTransferred(VRCPlayerApi) is invoked via reflection rather than a
        // direct call: VRCPlayerApi does not resolve as a usable type in this assembly.
        // GetMethod(name, bindingFlags) alone throws AmbiguousMatchException here (the
        // base UdonSharpBehaviour type exposes more than one member of this name), so
        // the VRCPlayerApi parameter type must be looked up by its assembly-qualified
        // name and passed explicitly to disambiguate the overload. Every guard exercised
        // below ignores the parameter's value entirely, so passing null is equivalent to
        // a real call for these cases.
        private static void InvokeOnOwnershipTransferred(Tsvrc.Core.Process process)
        {
            var playerApiType = Type.GetType("VRC.SDKBase.VRCPlayerApi, VRCSDKBase");
            Assert.IsNotNull(playerApiType, "VRCPlayerApi type not found. Assembly name changed?");
            var method = process.GetType().GetMethod("OnOwnershipTransferred",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { playerApiType }, null);
            Assert.IsNotNull(method, "OnOwnershipTransferred(VRCPlayerApi) not found. Signature changed?");
            method.Invoke(process, new object[] { null });
        }

        [Test]
        public void OnOwnershipTransferred_ProcessNotRunning_ReturnsWithoutTakingOver()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            InvokeOnOwnershipTransferred(process);

            Assert.AreEqual(0, process.OnOwnerAbandonedProcessCount);
        }

        [Test]
        public void OnOwnershipTransferred_AlreadyProcessOwner_ReturnsWithoutTakingOver()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();

            InvokeOnOwnershipTransferred(process);

            Assert.AreEqual(0, process.OnOwnerAbandonedProcessCount);
        }

        [Test]
        public void OnOwnershipTransferred_RunningNotOwnerAndOwnerIdEmpty_ReturnsWithoutTakingOver()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_localPlayerIdInt", OwnerPlayerId);
            PrivateFieldAccess.SetField(process, "_isRunning", true);

            InvokeOnOwnershipTransferred(process);

            Assert.AreEqual(0, process.OnOwnerAbandonedProcessCount);
        }
    }
}
