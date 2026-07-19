using NUnit.Framework;
using Tsvrc.Utils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    public class TsLoggerTests
    {
        private System.Collections.Generic.List<GameObject> _spawned = new System.Collections.Generic.List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private TsLogger CreateLogger()
        {
            var go = new GameObject(nameof(TsLogger));
            _spawned.Add(go);
            return go.AddComponent<TsLogger>();
        }

        private static int CountLogMessages(System.Action act)
        {
            int callCount = 0;
            void Handler(string condition, string stackTrace, LogType type) => callCount++;

            Application.logMessageReceived += Handler;
            try { act(); }
            finally { Application.logMessageReceived -= Handler; }

            return callCount;
        }

        [Test]
        public void AllSixLevelToggles_DefaultToTrue()
        {
            var logger = CreateLogger();

            Assert.IsTrue(logger.InternalInfoEnabled);
            Assert.IsTrue(logger.InternalWarningEnabled);
            Assert.IsTrue(logger.InternalErrorEnabled);
            Assert.IsTrue(logger.WorldInfoEnabled);
            Assert.IsTrue(logger.WorldWarningEnabled);
            Assert.IsTrue(logger.WorldErrorEnabled);
        }

        [Test]
        public void Prefix_DefaultsToEmpty()
        {
            Assert.AreEqual("", CreateLogger().Prefix);
        }

        [Test]
        public void Info_Enabled_DefaultPrefix_LogsWithFrameworkTagAndTagFormatOnly()
        {
            var logger = CreateLogger();

            // No project Prefix configured: the framework tag [TsVRC] still always appears,
            // but the optional project tag is omitted entirely (no empty [] pair).
            LogAssert.Expect(LogType.Log, "[TsVRC] [MyTag] hello");
            logger.Info("MyTag", "hello");
        }

        [Test]
        public void Info_CustomPrefix_LogsWithFrameworkTagThenCustomPrefix()
        {
            var logger = CreateLogger();
            logger.Prefix = "MyGame";

            // The hardcoded [TsVRC] framework tag always leads, even when a project sets
            // its own Prefix - a world author's prefix never replaces or hides it.
            LogAssert.Expect(LogType.Log, "[TsVRC] [MyGame] [MyTag] hello");
            logger.Info("MyTag", "hello");
        }

        [Test]
        public void Info_IsInternalDefaultsToFalse_GatedByWorldToggleNotInternal()
        {
            var logger = CreateLogger();
            logger.WorldInfoEnabled = false;
            logger.InternalInfoEnabled = true;

            // A direct call with no isInternal argument is "world" by default, so disabling
            // WorldInfoEnabled (while leaving InternalInfoEnabled on) must suppress it.
            Assert.AreEqual(0, CountLogMessages(() => logger.Info("MyTag", "hello")));
        }

        [Test]
        public void Info_WorldDisabled_InternalCallStillLogs()
        {
            var logger = CreateLogger();
            logger.WorldInfoEnabled = false;
            logger.InternalInfoEnabled = true;

            LogAssert.Expect(LogType.Log, "[TsVRC] [MyTag] hello");
            logger.Info("MyTag", "hello", isInternal: true);
        }

        [Test]
        public void Info_InternalDisabled_WorldCallStillLogs()
        {
            var logger = CreateLogger();
            logger.InternalInfoEnabled = false;
            logger.WorldInfoEnabled = true;

            LogAssert.Expect(LogType.Log, "[TsVRC] [MyTag] hello");
            logger.Info("MyTag", "hello", isInternal: false);
        }

        [Test]
        public void Info_BothInternalAndWorldDisabled_NeitherLogs()
        {
            var logger = CreateLogger();
            logger.InternalInfoEnabled = false;
            logger.WorldInfoEnabled = false;

            Assert.AreEqual(0, CountLogMessages(() => logger.Info("MyTag", "internal", isInternal: true)));
            Assert.AreEqual(0, CountLogMessages(() => logger.Info("MyTag", "world", isInternal: false)));
        }

        [Test]
        public void Warning_InternalDisabled_InternalCallSuppressed_WorldCallStillLogs()
        {
            var logger = CreateLogger();
            logger.InternalWarningEnabled = false;

            Assert.AreEqual(0, CountLogMessages(() => logger.Warning("MyTag", "careful", isInternal: true)));

            LogAssert.Expect(LogType.Warning, "[TsVRC] [MyTag] careful");
            logger.Warning("MyTag", "careful", isInternal: false);
        }

        [Test]
        public void Error_WorldDisabled_WorldCallSuppressed_InternalCallStillLogs()
        {
            var logger = CreateLogger();
            logger.WorldErrorEnabled = false;

            Assert.AreEqual(0, CountLogMessages(() => logger.Error("MyTag", "broken", isInternal: false)));

            LogAssert.Expect(LogType.Error, "[TsVRC] [MyTag] broken");
            logger.Error("MyTag", "broken", isInternal: true);
        }

        [Test]
        public void Warning_Enabled_LogsRegardlessOfInfoToggle()
        {
            var logger = CreateLogger();
            logger.InternalInfoEnabled = false;
            logger.WorldInfoEnabled = false;

            LogAssert.Expect(LogType.Warning, "[TsVRC] [MyTag] careful");
            logger.Warning("MyTag", "careful");
        }

        [Test]
        public void Error_Enabled_LogsRegardlessOfInfoToggle()
        {
            var logger = CreateLogger();
            logger.InternalInfoEnabled = false;
            logger.WorldInfoEnabled = false;

            LogAssert.Expect(LogType.Error, "[TsVRC] [MyTag] broken");
            logger.Error("MyTag", "broken");
        }
    }
}
