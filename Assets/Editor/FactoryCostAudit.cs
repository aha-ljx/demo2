using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryCostAudit
{
    const string Pending = "FactoryCostAudit.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static double verifyAt;

    static FactoryCostAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            verifyAt = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += Verify;
        };
    }

    [MenuItem("Tools/Factory/Run Equipment Cost Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    static void Verify()
    {
        if (EditorApplication.timeSinceStartup < verifyAt) return;
        EditorApplication.update -= Verify;
        var report = new StringBuilder();
        bool passed = false;
        Action<bool, string> check = (ok, message) =>
        {
            if (!ok) throw new Exception(message);
            report.AppendLine("PASS " + message);
        };
        try
        {
            var placement = UnityEngine.Object.FindObjectOfType<PlacementSystem>();
            placement.enabled = false;
            placement.worldCamera.GetComponent<GameController>().enabled = false;
            FactoryProduction.Instance.enabled = false;
            placement.player.position = new Vector3(-100, 2, -100);
            Physics.SyncTransforms();
            Func<string, object> field = name => typeof(PlacementSystem).GetField(name, Private).GetValue(placement);
            Action<string, object[]> call = (name, args) => typeof(PlacementSystem).GetMethod(name, Private).Invoke(placement, args);
            var placed = (List<GameObject>)field("placed");
            var cost = (TMP_Text)field("costText");
            check(placement.CurrentCost == 0 && cost.text.Contains("0 元"), "new game starts at zero cost");
            int[] prices = { 3000, 1000, 1500, 500, 50 };
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < prices.Length; i++)
            {
                var slot = placement.slotImages[i];
                var label = slot.GetComponentInChildren<TMP_Text>();
                var price = slot.transform.Find("Price").GetComponent<TMP_Text>();
                label.ForceMeshUpdate(); price.ForceMeshUpdate();
                check(price.text == prices[i] + " 元", "slot " + (i + 1) + " shows the specified price");
                check(label.textInfo.lineCount == 2 && !label.isTextTruncated, "module name remains on two complete lines");
                foreach (var text in new[] { label, price })
                    foreach (var glyph in text.textInfo.characterInfo)
                    {
                        if (!glyph.isVisible) continue;
                        check(glyph.textElement.unicode == glyph.character, "UI glyph renders correctly: " + glyph.character);
                        Vector3 min = slot.rectTransform.InverseTransformPoint(text.transform.TransformPoint(glyph.bottomLeft));
                        Vector3 max = slot.rectTransform.InverseTransformPoint(text.transform.TransformPoint(glyph.topRight));
                        check(slot.rectTransform.rect.Contains(min) && slot.rectTransform.rect.Contains(max), "UI glyph fits in slot");
                    }
                check(label.rectTransform.offsetMin.y >= price.rectTransform.anchoredPosition.y + price.rectTransform.rect.height,
                    "price is below the module label without overlap");
            }
            long expected = 0;
            Vector3 railCenter = Vector3.zero;
            foreach (int kind in new[] { 4, 3, 0, 1, 2, 4 })
            {
                call("SelectItem", new object[] { kind });
                check(placement.CurrentCost == expected, "selecting a preview does not charge money");
                check(cost.text.Contains((expected + prices[kind]).ToString("N0")), "selection shows projected total");
                var preview = (GameObject)field("groundPreview");
                Vector3 target = kind == 3 ? railCenter : new Vector3(80 + placed.Count * 40, 0, 80);
                call("AlignVisualToGround", new object[] { preview, target });
                bool valid = (bool)typeof(PlacementSystem).GetMethod("CanPlaceHere", Private).Invoke(placement, new object[] { preview, null });
                check(valid, "actual preview is valid for module " + kind);
                call("PlaceItem", null);
                expected += prices[kind];
                check(placement.CurrentCost == expected && cost.text.Contains(expected.ToString("N0")), "confirmed placement updates cost immediately");
                if (kind == 4) railCenter = placed[placed.Count - 1].GetComponent<EquipmentGrid>().Footprint.center;
                call("CancelSelection", null);
                check(placement.CurrentCost == expected && !cost.text.Contains("再放一件"), "cancelling preview keeps cost and clears estimate");
            }
            check(expected == 6100, "five module types and an extra rail total 6100");
            var machine = placed[2];
            machine.transform.position += Vector3.right * 3;
            typeof(PlacementSystem).GetField("inspectedItem", Private).SetValue(placement, machine);
            call("RotateInspectedEquipment", null);
            check(placement.CurrentCost == expected, "moving and rotating existing equipment do not add cost");
            call("RemovePlaced", new object[] { placed[0] });
            check(placement.CurrentCost == expected, "blocked withdrawal of an occupied rail does not refund cost");
            // Remove the arm before its supporting rail, as in normal editing.
            var order = new List<GameObject>(placed);
            var arm = order[1]; order.RemoveAt(1); order.Insert(0, arm);
            foreach (var item in order)
            {
                call("RemovePlaced", new object[] { item });
                check(placement.CurrentCost < expected, "withdrawal subtracts the removed module immediately");
                expected = placement.CurrentCost;
                // Normal input spans separate frames; complete deferred destruction before the next edit.
                UnityEngine.Object.DestroyImmediate(item);
            }
            check(expected == 0 && cost.text.Contains("0 元"), "withdrawing all equipment restores zero");
            passed = true;
            report.AppendLine("PASS ALL");
        }
        catch (Exception error) { report.AppendLine("FAIL " + error); }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/factory-cost-audit.txt", report.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
