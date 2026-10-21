using System;
using TMPro;
using UnityEngine;

namespace ObenseuerRUS
{
    /// <summary>
    /// Версия русификатора в главном меню — копия игровой надписи "V. 0.5.08" (ShowVersion) в левом нижнем углу:
    /// тот же машинописный шрифт, цвет и размер, что у версии игры.
    /// </summary>
    internal static class VersionStamp
    {
        public const string Scene = "Main Menu";
        private const string Name = "ObenseuerRUS Version";

        public static void Add(string text)
        {
            ShowVersion game = UnityEngine.Object.FindAnyObjectByType<ShowVersion>();
            if (game == null || game.Versiontext == null) return;
            TextMeshProUGUI original = game.Versiontext;
            if (original.transform.parent != null && original.transform.parent.Find(Name) != null) return;
            TextMeshProUGUI stamp = UnityEngine.Object.Instantiate(original, original.transform.parent);
            stamp.name = Name;
            // Иначе скопированный ShowVersion в Start вернёт игровой текст.
            foreach (ShowVersion copy in stamp.GetComponentsInChildren<ShowVersion>(true)) UnityEngine.Object.DestroyImmediate(copy);
            RectTransform from = original.rectTransform, to = stamp.rectTransform;
            to.anchorMin = new Vector2(1f - from.anchorMax.x, from.anchorMin.y);
            to.anchorMax = new Vector2(1f - from.anchorMin.x, from.anchorMax.y);
            to.pivot = new Vector2(1f - from.pivot.x, from.pivot.y);
            to.anchoredPosition = new Vector2(-from.anchoredPosition.x, from.anchoredPosition.y);
            string side = original.alignment.ToString();
            side = side.Contains("Right") ? side.Replace("Right", "Left") : side.Replace("Left", "Right");
            TextAlignmentOptions mirrored;
            if (Enum.TryParse(side, out mirrored)) stamp.alignment = mirrored;
            stamp.textWrappingMode = TextWrappingModes.NoWrap;
            stamp.overflowMode = TextOverflowModes.Overflow;
            stamp.text = text;
        }
    }
}
