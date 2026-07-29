using Tsvrc.Testing.Framework;
namespace Tsvrc.Tests.EditMode
{
    // Extends ReadyCheckProcessTestBase (which itself extends PlayerTrackerTestBase /
    // ProcessTestBase) to reuse CreateProcess<T>/SeedAsOwner/GetTrackedPlayerIds/
    // GetReadyPlayerIds/ForceNextTickDueNow/etc. - every class in the DataTransfer chain IS a
    // ReadyCheckProcess (IS a PlayerTracker, IS a Process), so every helper built for those
    // base classes applies here unchanged. Adds helpers for the DataTransfer-chain-specific
    // private fields, named per class for readability; less-frequently-touched fields are
    // accessed directly via PrivateFieldAccess at the call site, matching the established
    // convention elsewhere in this suite.
    public abstract class DataTransferTestBase : ReadyCheckProcessTestBase
    {
        // ChunkedTransferSession
        protected static string GetInitialData(object obj) => PrivateFieldAccess.GetField<string>(obj, "_initialData");
        protected static void SetInitialData(object obj, string value) => PrivateFieldAccess.SetField(obj, "_initialData", value);
        protected static string[] GetDataChunks(object obj) => PrivateFieldAccess.GetField<string[]>(obj, "_dataChunks");
        protected static void SetDataChunks(object obj, string[] value) => PrivateFieldAccess.SetField(obj, "_dataChunks", value);
        protected static int GetCurrentChunkIndex(object obj) => PrivateFieldAccess.GetField<int>(obj, "_currentChunkIndex");
        protected static void SetCurrentChunkIndex(object obj, int value) => PrivateFieldAccess.SetField(obj, "_currentChunkIndex", value);
        protected static int GetTotalChunksField(object obj) => PrivateFieldAccess.GetField<int>(obj, "_totalChunks");
        protected static void SetTotalChunksField(object obj, int value) => PrivateFieldAccess.SetField(obj, "_totalChunks", value);
        protected static string[] GetTargetPlayerIds(object obj) => PrivateFieldAccess.GetField<string[]>(obj, "_targetPlayerIds");
        protected static void SetTargetPlayerIds(object obj, string[] value) => PrivateFieldAccess.SetField(obj, "_targetPlayerIds", value);
        protected static bool GetPendingNextChunk(object obj) => PrivateFieldAccess.GetField<bool>(obj, "_pendingNextChunk");
        protected static void SetPendingNextChunk(object obj, bool value) => PrivateFieldAccess.SetField(obj, "_pendingNextChunk", value);
        protected static bool GetCancelRequested(object obj) => PrivateFieldAccess.GetField<bool>(obj, "_cancelRequested");
        protected static void SetCancelRequested(object obj, bool value) => PrivateFieldAccess.SetField(obj, "_cancelRequested", value);

        // DataSender
        protected static bool GetPendingTransferStopped(object obj) => PrivateFieldAccess.GetField<bool>(obj, "_pendingTransferStopped");
        protected static void SetPendingTransferStopped(object obj, bool value) => PrivateFieldAccess.SetField(obj, "_pendingTransferStopped", value);
        protected static bool GetPendingTransferCompleted(object obj) => PrivateFieldAccess.GetField<bool>(obj, "_pendingTransferCompleted");
        protected static void SetPendingTransferCompleted(object obj, bool value) => PrivateFieldAccess.SetField(obj, "_pendingTransferCompleted", value);

        // DataChunkReceiver
        protected static int GetExpectedSenderPlayerId(object obj) => PrivateFieldAccess.GetField<int>(obj, "_expectedSenderPlayerId");
        protected static void SetExpectedSenderPlayerId(object obj, int value) => PrivateFieldAccess.SetField(obj, "_expectedSenderPlayerId", value);
        protected static int GetExpectedTotalChunks(object obj) => PrivateFieldAccess.GetField<int>(obj, "_expectedTotalChunks");
        protected static void SetExpectedTotalChunks(object obj, int value) => PrivateFieldAccess.SetField(obj, "_expectedTotalChunks", value);
        protected static bool GetIsSendingChunk(object obj) => PrivateFieldAccess.GetField<bool>(obj, "_isSendingChunk");
        protected static void SetIsSendingChunk(object obj, bool value) => PrivateFieldAccess.SetField(obj, "_isSendingChunk", value);
        protected static bool GetTransferActive(object obj) => PrivateFieldAccess.GetField<bool>(obj, "_transferActive");
        protected static void SetTransferActive(object obj, bool value) => PrivateFieldAccess.SetField(obj, "_transferActive", value);
        protected static string[] GetReceivedChunks(object obj) => PrivateFieldAccess.GetField<string[]>(obj, "_receivedChunks");
        protected static void SetReceivedChunks(object obj, string[] value) => PrivateFieldAccess.SetField(obj, "_receivedChunks", value);

        // DataSenderReceiver
        protected static bool GetPendingCompletion(object obj) => PrivateFieldAccess.GetField<bool>(obj, "_pendingCompletion");
        protected static void SetPendingCompletion(object obj, bool value) => PrivateFieldAccess.SetField(obj, "_pendingCompletion", value);
        protected static string GetPendingCompletionData(object obj) => PrivateFieldAccess.GetField<string>(obj, "_pendingCompletionData");
        protected static void SetPendingCompletionData(object obj, string value) => PrivateFieldAccess.SetField(obj, "_pendingCompletionData", value);
        protected static bool GetPendingStop(object obj) => PrivateFieldAccess.GetField<bool>(obj, "_pendingStop");
        protected static void SetPendingStop(object obj, bool value) => PrivateFieldAccess.SetField(obj, "_pendingStop", value);

        // Builds a string of the given length by repeating a base pattern, used throughout for
        // chunk-boundary tests instead of hand-writing very long literals.
        protected static string BuildString(int length, char c = 'x')
        {
            return new string(c, length);
        }
    }
}
