#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The option and difficulty buttons were sized by dragging them in the Scene
// view, so although they all share one sprite, no two of them ended up the same
// shape: the option buttons carry a non-uniform localScale of (2.52, 3.5, 1.29)
// that stretches the sprite's corners and its label differently in each axis,
// while the difficulty buttons sit at scale 1 and get their size from sizeDelta.
// Nothing is centred, and the vertical gaps between the difficulty buttons drift
// by a few pixels each.
//
// This pushes every button back onto one standard. It runs against the open
// scene and records undo, so the result can be looked at and reverted from the
// editor like any other change.
public static class MenuButtonNormaliser
{
    // One size for every button that is a primary choice, whichever panel it is
    // on. Sized rather than scaled, so the sprite's border is drawn at the same
    // thickness everywhere.
    private static readonly Vector2 PrimarySize = new Vector2(460f, 150f);

    // Back is deliberately a smaller, secondary control, so it keeps its own
    // size and its own corner position - it is just squared off.
    private static readonly Vector2 BackSize = new Vector2(140f, 90f);

    // The difficulty buttons form a ladder. Top position and a single gap, so
    // the spacing cannot drift again.
    private const float DifficultyTopY = 434f;
    private const float DifficultyGapY = 265f;

    private static readonly string[] DifficultyLadder =
    {
        "EasyButton", "MediumButton", "HardButton", "ExpertButton"
    };

    private static readonly string[] OptionButtons =
    {
        "SoloButton", "MultiplayerButton"
    };

    [MenuItem("Scrabby/UI/Normalise Menu Buttons")]
    public static void Normalise()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog(
                "No scene open",
                "Open the scene holding OptionPanel and DifficultyPanel first.",
                "OK");
            return;
        }

        GameObject optionPanel = FindInScene(scene, "OptionPanel");
        GameObject difficultyPanel = FindInScene(scene, "DifficultyPanel");

        if (optionPanel == null && difficultyPanel == null)
        {
            EditorUtility.DisplayDialog(
                "Panels not found",
                "Neither OptionPanel nor DifficultyPanel is in the open scene.",
                "OK");
            return;
        }

        StringBuilder report = new StringBuilder();
        int changed = 0;

        foreach (string name in OptionButtons)
        {
            RectTransform button = FindButton(optionPanel, name);
            if (button == null)
            {
                report.AppendLine("  " + name + ": not found");
                continue;
            }

            changed += Square(button, PrimarySize, report);
            changed += CentreX(button, report);

            // At scale 1 the old font size of 15 would be unreadable: it was only
            // legible because the button was stretched 3.5x vertically. Auto-sizing
            // is what the difficulty buttons already use, so the labels now fill
            // their button the same way rather than at a fixed size.
            changed += TidyLabel(button, report, enableAutoSize: true);
        }

        for (int i = 0; i < DifficultyLadder.Length; i++)
        {
            RectTransform button = FindButton(difficultyPanel, DifficultyLadder[i]);
            if (button == null)
            {
                report.AppendLine("  " + DifficultyLadder[i] + ": not found");
                continue;
            }

            changed += Square(button, PrimarySize, report);
            changed += CentreX(button, report);
            changed += SetY(button, DifficultyTopY - i * DifficultyGapY, report);
            changed += TidyLabel(button, report, enableAutoSize: true);
        }

        RectTransform back = FindButton(difficultyPanel, "BackButton");
        if (back != null)
        {
            // Left where it sits: a back control in the corner is not part of the
            // centred ladder.
            changed += Square(back, BackSize, report);
            changed += TidyLabel(back, report, enableAutoSize: true);
        }

        if (changed > 0)
            EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log(
            "[MenuButtonNormaliser] " + changed + " change(s):\n" + report);

        EditorUtility.DisplayDialog(
            "Menu buttons normalised",
            changed + " change(s) made. Look them over, then save the scene - " +
            "or Edit > Undo to put them back.\n\nDetails are in the Console.",
            "OK");
    }

    // Size, do not scale. A non-uniform scale stretches the sprite's rounded
    // corners and its border by different amounts on each axis, which is why
    // these never looked like the same button.
    private static int Square(RectTransform rt, Vector2 size, StringBuilder report)
    {
        bool scaleWrong = rt.localScale != Vector3.one;
        bool sizeWrong = rt.sizeDelta != size;

        if (!scaleWrong && !sizeWrong)
            return 0;

        Undo.RecordObject(rt, "Normalise menu button");

        if (scaleWrong)
        {
            report.AppendLine(
                "  " + rt.name + ": scale " + rt.localScale + " -> (1, 1, 1)");
            rt.localScale = Vector3.one;
        }

        if (sizeWrong)
        {
            report.AppendLine(
                "  " + rt.name + ": size " + rt.sizeDelta + " -> " + size);
            rt.sizeDelta = size;
        }

        return 1;
    }

    private static int CentreX(RectTransform rt, StringBuilder report)
    {
        Vector2 pos = rt.anchoredPosition;

        if (Mathf.Approximately(pos.x, 0f))
            return 0;

        Undo.RecordObject(rt, "Centre menu button");
        report.AppendLine("  " + rt.name + ": x " + pos.x + " -> 0");
        rt.anchoredPosition = new Vector2(0f, pos.y);
        return 1;
    }

    private static int SetY(RectTransform rt, float y, StringBuilder report)
    {
        Vector2 pos = rt.anchoredPosition;

        if (Mathf.Approximately(pos.y, y))
            return 0;

        Undo.RecordObject(rt, "Space menu button");
        report.AppendLine("  " + rt.name + ": y " + pos.y + " -> " + y);
        rt.anchoredPosition = new Vector2(pos.x, y);
        return 1;
    }

    // "SOLO " carries a trailing space and "MEDIUM" a trailing newline, so TMP
    // centres them as though the whitespace were part of the word - the newline
    // makes MEDIUM a two-line block that sits visibly high against its neighbours.
    private static int TidyLabel(
        RectTransform button, StringBuilder report, bool enableAutoSize)
    {
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);

        if (label == null)
        {
            report.AppendLine("  " + button.name + ": no label found");
            return 0;
        }

        string trimmed = label.text.Trim();

        bool textWrong = trimmed != label.text;
        bool alignWrong = label.alignment != TextAlignmentOptions.Center;
        bool autoWrong = enableAutoSize && !label.enableAutoSizing;

        if (!textWrong && !alignWrong && !autoWrong)
            return 0;

        Undo.RecordObject(label, "Tidy menu button label");

        if (textWrong)
        {
            report.AppendLine(
                "  " + button.name + ": text " + Escape(label.text) +
                " -> " + Escape(trimmed));
            label.text = trimmed;
        }

        if (alignWrong)
        {
            report.AppendLine("  " + button.name + ": alignment -> Center");
            label.alignment = TextAlignmentOptions.Center;
        }

        if (autoWrong)
        {
            report.AppendLine(
                "  " + button.name + ": auto-size on (" +
                label.fontSizeMin + "-" + label.fontSizeMax + ")");
            label.enableAutoSizing = true;
        }

        return 1;
    }

    private static string Escape(string value)
    {
        return "\"" + value.Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }

    private static GameObject FindInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            // Inactive too: both panels spend most of their life switched off.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t.gameObject;
            }
        }

        return null;
    }

    private static RectTransform FindButton(GameObject panel, string name)
    {
        if (panel == null)
            return null;

        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
        {
            if (button.name == name)
                return button.transform as RectTransform;
        }

        return null;
    }
}
#endif
