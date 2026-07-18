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

        [Test]
        public void InfoEnabled_DefaultsToTrue()
        {
            Assert.IsTrue(CreateLogger().InfoEnabled);
        }

        [Test]
        public void Info_Enabled_LogsWithTagFormat()
        {
            var logger = CreateLogger();

            LogAssert.Expect(LogType.Log, "[MyTag] hello");
            logger.Info("MyTag", "hello");
        }

        [Test]
        public void Info_Disabled_DoesNotInvokeDebugLog()
        {
            var logger = CreateLogger();
            logger.InfoEnabled = false;
            int callCount = 0;
            void Handler(string condition, string stackTrace, LogType type) => callCount++;

            Application.logMessageReceived += Handler;
            try
            {
                logger.Info("MyTag", "hello");
            }
            finally
            {
                Application.logMessageReceived -= Handler;
            }

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Warning_Fires_RegardlessOfInfoEnabled()
        {
            var logger = CreateLogger();
            logger.InfoEnabled = false;

            LogAssert.Expect(LogType.Warning, "[MyTag] careful");
            logger.Warning("MyTag", "careful");
        }

        [Test]
        public void Error_Fires_RegardlessOfInfoEnabled()
        {
            var logger = CreateLogger();
            logger.InfoEnabled = false;

            LogAssert.Expect(LogType.Error, "[MyTag] broken");
            logger.Error("MyTag", "broken");
        }
    }
}
