#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Core;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Emits small UI helper methods onto CompiledTsvrc so every TsvrcBehaviour can call
    /// _ts.SetButtonVisible / SetButtonInteractable / SetTextVisible without duplicating the boilerplate.
    /// No scene scanning is required, the output is always the same fixed block of code.
    /// </summary>
    internal class UiUtilitiesModule : TsvrcModule
    {
        internal override void Scan(TsvrcConfig config) { }

        internal override IEnumerable<string> GetUsings()
        {
            yield return "TMPro";
            yield return "UnityEngine.UI";
        }

        internal override void WriteMethods(CsWriter w)
        {
            w.Region("UI Utilities");

            w.Summary("Shows or hides a Button's GameObject.");
            using (w.Method("public void SetButtonVisible(Button button, bool visible)"))
                w.Line("if (button != null) button.gameObject.SetActive(visible);");

            w.Summary("Shows or hides a Button and sets its interactable state in one call.");
            using (w.Method("public void SetButtonInteractable(Button button, bool interactable, bool visible)"))
            {
                w.Line("if (button == null) return;");
                w.Line("button.gameObject.SetActive(visible);");
                w.Line("button.interactable = interactable;");
            }

            w.Summary("Shows or hides a TMP_Text's GameObject.");
            using (w.Method("public void SetTextVisible(TMP_Text text, bool visible)"))
                w.Line("if (text != null) text.gameObject.SetActive(visible);");

            w.EndRegion();
        }
    }
}
#endif
