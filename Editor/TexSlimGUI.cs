// ==============================================================================
// Product : TexSlim
// File    : TexSlimGUI.cs
// Role    : トグルスイッチ・検索バーなど Inspector 専用カスタムウィジェットの描画。
//
// 重要 : OnGUI の内側で GUIStyle を new しないこと（TexSlimStyles の注意書き参照）。
// ==============================================================================

using UnityEditor;
using UnityEngine;

namespace HoriCraft.TexSlim.Editor
{
    /// <summary>
    /// Inspector 上で使うカスタムウィジェット群。
    /// スタイルは TexSlimStyles から取得し、かえポン！と同パターンで統一する。
    /// </summary>
    internal static class TexSlimGUI
    {
        // ─── トグルスイッチ ───────────────────────────────────────────
        private const float ToggleW  = 40f;
        private const float ToggleH  = 22f;
        private const float ThumbPad = 3f;

        // トグル用テクスチャキャッシュ（毎フレーム生成を防ぐ）
        private static Texture2D _trackOnTex;
        private static Texture2D _trackOffTex;
        private static Texture2D _thumbTex;

        // レイアウト確保用の不可視スタイル。毎フレーム new しないよう静的に保持する。
        private static readonly GUIStyle InvisibleToggleStyle = new GUIStyle(GUIStyle.none)
        {
            fixedWidth  = ToggleW,
            fixedHeight = ToggleH,
            margin      = new RectOffset(2, 2, 2, 2)
        };

        private static readonly GUIStyle SearchIconStyle = new GUIStyle(GUIStyle.none)
        {
            alignment   = TextAnchor.MiddleLeft,
            fontSize    = 11,
            fixedHeight = 24f
        };

        /// <summary>
        /// スマホ風トグルスイッチを GUILayout で描画し、クリック後の値を返す。
        /// <para>
        /// 実装方針：レイアウト領域だけ確保し、Repaint パスで角丸テクスチャをカスタム描画する。
        /// これにより EditorGUI.BeginChangeCheck / EndChangeCheck が正しく機能し、
        /// DisabledScope 内でも動作する。
        /// </para>
        /// </summary>
        /// <param name="tooltip">
        /// スイッチにマウスを乗せたときに画面下部のツールチップバーへ出す説明。
        /// 行にラベルを置かない代わりに、ここで役割を説明する。
        /// </param>
        public static bool DrawToggleSwitch(bool value, bool disabled = false, string tooltip = null)
        {
            using (new EditorGUI.DisabledScope(disabled))
            {
                Rect rect = GUILayoutUtility.GetRect(ToggleW, ToggleH, InvisibleToggleStyle);

                if (!string.IsNullOrEmpty(tooltip)
                    && Event.current.type == EventType.Repaint
                    && rect.Contains(Event.current.mousePosition))
                {
                    GUI.tooltip = tooltip;
                }

                // クリック判定（disabled でなく、MouseDown が rect 内）
                bool nextValue = value;
                if (!disabled
                    && Event.current.type == EventType.MouseDown
                    && rect.Contains(Event.current.mousePosition))
                {
                    nextValue = !value;
                    GUI.changed = true;   // BeginChangeCheck に伝える
                    Event.current.Use();
                }

                // Repaint：角丸トラック + サムをカスタム描画
                if (Event.current.type == EventType.Repaint)
                {
                    float alpha = disabled ? 0.35f : 1f;

                    EnsureToggleTextures();

                    Color prev = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, alpha);

                    // トラック背景
                    GUI.DrawTexture(rect, nextValue ? _trackOnTex : _trackOffTex, ScaleMode.StretchToFill);

                    // サム（白い丸）
                    float thumbSize = ToggleH - ThumbPad * 2f;
                    float thumbX    = nextValue
                        ? rect.x + rect.width - thumbSize - ThumbPad
                        : rect.x + ThumbPad;
                    Rect thumbRect  = new Rect(thumbX, rect.y + ThumbPad, thumbSize, thumbSize);
                    GUI.DrawTexture(thumbRect, _thumbTex, ScaleMode.StretchToFill);

                    GUI.color = prev;
                }

                return nextValue;
            }
        }

        /// <summary>トグル用テクスチャを一度だけ生成してキャッシュする</summary>
        private static void EnsureToggleTextures()
        {
            if (_trackOnTex != null && _trackOffTex != null && _thumbTex != null) return;

            // Material Switch の配色：ON はプライマリ、OFF は surface variant
            _trackOnTex  = TexSlimStyles.MakeRoundedTex(
                (int)ToggleW, (int)ToggleH, TexSlimStyles.PrimaryColor, (int)(ToggleH / 2));
            _trackOffTex = TexSlimStyles.MakeRoundedTex(
                (int)ToggleW, (int)ToggleH, TexSlimStyles.OutlineColor, (int)(ToggleH / 2));
            // サム（白い円）
            int ts = (int)(ToggleH - ThumbPad * 2f);
            _thumbTex    = TexSlimStyles.MakeRoundedTex(ts, ts, Color.white, ts / 2);
        }

        /// <summary>
        /// テクスチャ破棄時にキャッシュ参照をリセットする。
        /// テクスチャ本体の DestroyImmediate は TexSlimStyles 側で行われる。
        /// </summary>
        public static void ClearToggleCache()
        {
            _trackOnTex  = null;
            _trackOffTex = null;
            _thumbTex    = null;
        }

        // ─── 検索バー ─────────────────────────────────────────────────

        /// <summary>
        /// クリアボタン（×）付きの検索フィールドを描画する。
        /// 変更があった場合は true を返す。
        /// <para>
        /// アイコンに絵文字（🔍 など）は使わない。Unity の IMGUI で使われる
        /// エディタフォントに絵文字のグリフが無く、環境によっては何も描かれずに
        /// 空白だけが残る。ここでは語（「検索」）で示す。
        /// </para>
        /// </summary>
        /// <param name="labelWidth">
        /// 左ラベルの幅。同じカード内の他の行（表示・圧縮モード…）と揃えて
        /// ラベル列を作るため、呼び出し側から指定できるようにしている。
        /// </param>
        public static bool DrawSearchBar(ref string query, float labelWidth)
        {
            EditorGUILayout.BeginHorizontal();

            SearchIconStyle.normal.textColor = TexSlimStyles.DimTextColor;
            GUILayout.Label(L.T("検索", "Find"), SearchIconStyle, GUILayout.Width(labelWidth));

            // テキストフィールド
            EditorGUI.BeginChangeCheck();
            string next = EditorGUILayout.TextField(query ?? string.Empty,
                TexSlimStyles.SearchFieldStyle, GUILayout.Height(24f));
            bool changed = EditorGUI.EndChangeCheck();

            // × クリアボタン（U+00D7。✕ U+2715 はエディタフォントに無い場合がある）
            if (!string.IsNullOrEmpty(query))
            {
                if (GUILayout.Button("×", TexSlimStyles.ClearButtonStyle,
                    GUILayout.Width(22f), GUILayout.Height(24f)))
                {
                    next    = string.Empty;
                    changed = true;
                    GUI.FocusControl(null);
                }
            }
            else
            {
                GUILayout.Space(22f);
            }

            EditorGUILayout.EndHorizontal();

            if (changed) { query = next; return true; }
            return false;
        }

        // ─── メトリクスカード ─────────────────────────────────────────

        /// <summary>数値メトリクスを縦並びで描画する</summary>
        public static void DrawMetric(string label, int value, Color valueColor)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(52f)))
            {
                EditorGUILayout.LabelField(value.ToString(), TexSlimStyles.BigNumber(18, valueColor));
                EditorGUILayout.LabelField(label, TexSlimStyles.StatusLabelStyle,
                    GUILayout.ExpandWidth(true));
            }
        }
    }
}
