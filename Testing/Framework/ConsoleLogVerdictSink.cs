using System.Collections.Generic;
using UnityEngine;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// Debug.LogError, unlike the Test Runner's own blocked event pipeline, is not
    /// filtered by VRCSDK's UnityEventFilter - it reliably shows up as a red Console error
    /// regardless. This sink uses it as the actual pass/fail signal for a Play Mode test
    /// suite: one PLAYMODE_TEST_RESULT line per test, then a single final
    /// "ALL N TESTS PASSED" / "N/M FAILED: ..." line from Report().
    /// </summary>
    public sealed class ConsoleLogVerdictSink : ITestVerdictSink
    {
        private readonly string _suiteName;
        private readonly List<string> _failedNames = new List<string>();

        public ConsoleLogVerdictSink(string suiteName)
        {
            _suiteName = suiteName;
        }

        public int ResultCount { get; private set; }

        public IReadOnlyList<string> FailedNames => _failedNames;

        public void Record(string testName, bool passed, string details)
        {
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);

            ResultCount++;
            if (!passed)
                _failedNames.Add(testName);
        }

        public void Report()
        {
            if (_failedNames.Count == 0)
                Debug.Log(_suiteName + ": ALL " + ResultCount + " TESTS PASSED");
            else
                Debug.LogError(_suiteName + ": " + _failedNames.Count + "/" + ResultCount +
                    " FAILED: " + string.Join(", ", _failedNames));
        }
    }
}
