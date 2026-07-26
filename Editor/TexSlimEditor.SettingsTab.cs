// ==============================================================================
// Product : TexSlim
// File    : TexSlimEditor.SettingsTab.cs
// Role    : 「設定」タブの描画（言語・品質・キーワード・全体復元）
//           （TexSlimEditor の partial。フィールド・ユーティリティは本体側）
// ==============================================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TexSlimComponent = global::HoriCraft.TexSlim.TexSlim;

namespace HoriCraft.TexSlim.Editor
{
    public sealed partial class TexSlimEditor
    {
        // ─── 設定タブ ─────────────────────────────────────────────────

        private void DrawSettingsMode()
        {
            // ── 言語（最上段。英語話者が最初に見つけられる位置に置く）
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            GUILayout.BeginHorizontal();
            // このラベルだけは常に両言語で出す（今の言語が読めない人のため）
            GUILayout.Label("Language / 言語", TexSlimStyles.LabelStyle, GUILayout.Width(120f));
            EditorGUI.BeginChangeCheck();
            int nextLang = EditorGUILayout.Popup(L.English ? 1 : 0, new[] { "日本語", "English" }, GUILayout.Width(100f));
            if (EditorGUI.EndChangeCheck())
            {
                L.English = nextLang == 1;
                Repaint();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // ── その他の設定
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            GUILayout.Label(L.T("圧縮の細かい設定", "Compression Details"), TexSlimStyles.LabelStyle);
            EditorGUILayout.Space(6f);

            // 顔・瞳／髪の保護トグルは「かんたん」タブに集約し、ここには置かない。
            // 同じ設定が別名で2箇所にあると別物に見えるため。

            // Crunch 圧縮品質スライダー（ResolutionOnly では使われない）
            bool qualityIgnored = component.Mode == TexSlimComponent.CompressionMode.ResolutionOnly;
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("Crunch品質", "Crunch Quality"), TexSlimStyles.LabelStyle, GUILayout.Width(96f));
            using (new EditorGUI.DisabledScope(qualityIgnored))
            {
                EditorGUI.BeginChangeCheck();
                int nextQuality = EditorGUILayout.IntSlider(component.CompressionQuality, 1, 100);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(component, "Change Compression Quality");
                    component.CompressionQuality = nextQuality;
                    MarkDirty();
                }
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                qualityIgnored
                    ? L.T("いまの「解像度のみ」モードでは Crunch をかけないので、この設定は効きません。",
                          "The current mode (Resolution only) does not apply Crunch, so this setting has no effect.")
                    : L.T("Crunch圧縮の画質です（1〜100）。\n"
                          + "数字が大きいほどきれいですが、そのぶん軽くなりません。\n"
                          + "おすすめ: 50〜80（初期値 75）",
                          "Crunch compression quality (1-100).\n"
                          + "Higher = better quality, larger file size.\n"
                          + "Recommended: 50-80 (default: 75)"),
                qualityIgnored ? MessageType.Info : MessageType.None);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // ── 保護キーワード
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            GUILayout.Label(L.T("保護キーワードを追加", "Add Protection Keywords"), TexSlimStyles.LabelStyle);
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                L.T("顔・瞳・髪 のほかにも守りたいものがあれば、その名前の一部をここに足します。\n"
                    + "メッシュ名・マテリアル名・テクスチャ名・プロパティ名（_MainTex など）の\n"
                    + "どれかにその文字が入っていれば、そのテクスチャは圧縮されなくなります。\n"
                    + "（顔・瞳・髪の保護は「かんたん」タブで ON/OFF できます。\n"
                    + "　ここに同じ語を足すと、そちらを OFF にしても保護が外れなくなります）",
                    "If there is anything else you want to keep at full quality, add part of its name here.\n"
                    + "A texture is left uncompressed when the text appears in its mesh, material,\n"
                    + "texture, or property name (_MainTex and the like).\n"
                    + "(Face/eyes/hair protection is toggled on the Easy tab. Adding the same\n"
                    + " words here would keep them protected even when those toggles are off.)"),
                MessageType.None);
            EditorGUILayout.Space(4f);

            DrawKeywordList(component.ProtectedKeywords, "Protected Keyword",
                L.T("（追加はありません）", "(none added)"));

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // ── 保護の例外（保護の打ち消し）
            // 「除外」は一覧のバッジで「手動で圧縮対象から外した」の意味に使っている。
            // ここは逆に「保護しない＝圧縮する」なので、同じ語を使うと真逆の意味で衝突する。
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            GUILayout.Label(L.T("保護の例外", "Protection Exceptions"), TexSlimStyles.LabelStyle);
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                L.F("ここに書いた文字を含むものは、保護キーワードに当てはまっていても保護しません。\n"
                    + "キーワードは名前の一部が一致すれば効いてしまうため、思わぬものが巻き込まれます。\n"
                    + "例:「hair（髪）」は「Chair（椅子）」にも一致してしまうので、\n"
                    + "最初から chair を例外に入れてあります。\n"
                    + "（最初から入っている例外: {0}）",
                    "Anything containing these words is never protected, even when a protection\n"
                    + "keyword matches it. Keywords match on part of a name, so unrelated things\n"
                    + "get caught: \"hair\" also matches \"Chair\", for example.\n"
                    + "That is why chair is an exception out of the box.\n"
                    + "(Built-in exceptions: {0})",
                    string.Join(" / ", AvatarTextureScanner.NegativeKeywords)),
                MessageType.None);
            EditorGUILayout.Space(4f);

            DrawKeywordList(component.ExcludedKeywords, "Excluded Keyword",
                L.T("（例外はありません）", "(no exceptions)"));

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // ── プロジェクト全体の圧縮済みテクスチャ
            // 台帳（プロジェクト全体）にあるテクスチャを一括で戻す。
            // 別シーンで圧縮したものも含め、ここからまとめて復元できる。
            int registryCount = ImportSettingsRegistry.Count;
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("圧縮済みテクスチャ（プロジェクト全体）", "Compressed Textures (whole project)"),
                TexSlimStyles.LabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(L.F("{0} 枚", "{0} textures", registryCount),
                TexSlimStyles.Chip(registryCount > 0 ? TexSlimStyles.AccentTeal : TexSlimStyles.AccentGrey),
                GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                L.T("このツールで圧縮したテクスチャの合計です。\n"
                    + "このアバター分だけ戻すなら、各タブの [↩ 元に戻す] を使ってください。\n"
                    + "下のボタンは、別シーンで圧縮したものも含めてすべて戻します。",
                    "Total number of textures whose import settings this tool has changed.\n"
                    + "To restore only this avatar, use [↩ Restore] on the other tabs.\n"
                    + "The button below restores everything, including textures compressed in other scenes."),
                MessageType.None);
            using (new EditorGUI.DisabledScope(registryCount == 0))
            {
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(
                            L.F("↩ 圧縮したテクスチャをすべて元に戻す（{0} 枚）", "↩ Restore all {0} compressed textures", registryCount),
                            L.T("このツールで圧縮したテクスチャを、すべて圧縮前のインポート設定へ戻します",
                                "Restores every texture in the registry to its pre-compression import settings")),
                        registryCount > 0 ? TexSlimStyles.AccentAmber : TexSlimStyles.NeutralColor,
                        TexSlimStyles.SmallColoredBtnStyle))
                {
                    RunRevertEverything(registryCount);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // ── 詳細情報
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            GUILayout.Label(L.T("このアバターの圧縮状況", "This Avatar"), TexSlimStyles.LabelStyle);
            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    L.T("このアバターの圧縮済み枚数", "Compressed on this avatar"),
                    (scan?.CompressedTextureCount ?? 0).ToString());
                // ヘッダーと同じくローカル時刻で見せる（UTC の生文字列は読めない）
                string lastCompression = FormatLastCompression();
                if (lastCompression != null)
                {
                    int sep = lastCompression.IndexOf(": ", System.StringComparison.Ordinal);
                    string timeOnly = sep >= 0 ? lastCompression.Substring(sep + 2) : lastCompression;
                    EditorGUILayout.TextField(L.T("最終圧縮日時", "Last compressed"), timeOnly);
                }
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// キーワードの編集リスト（追加保護キーワード／除外語で共用）。
        /// 短すぎる語は巻き添えが大きいので、その場で警告を出す。
        /// </summary>
        private void DrawKeywordList(List<string> keywords, string undoLabel, string emptyMessage)
        {
            bool hasBlank = false;
            for (int ki = 0; ki < keywords.Count; ki++)
            {
                if (string.IsNullOrWhiteSpace(keywords[ki])) hasBlank = true;

                GUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                string newKw = EditorGUILayout.TextField(keywords[ki]);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(component, "Edit " + undoLabel);
                    keywords[ki] = newKw;
                    MarkDirty();
                    RefreshIncludes();
                }

                // 1文字のキーワードはほぼ全テクスチャに一致してしまう
                string trimmed = (keywords[ki] ?? string.Empty).Trim();
                if (trimmed.Length == 1)
                {
                    GUILayout.Label(
                        new GUIContent("!", L.T("1文字だと、ほとんどのテクスチャに一致してしまいます",
                                                "A single character will match most textures")),
                        TexSlimStyles.TintedStatus(TexSlimStyles.WarnColor, bold: true),
                        GUILayout.Width(12f));
                }

                if (GUILayout.Button("×", TexSlimStyles.ClearButtonStyle,
                        GUILayout.Width(22f), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                {
                    Undo.RecordObject(component, "Remove " + undoLabel);
                    keywords.RemoveAt(ki);
                    MarkDirty();
                    RefreshIncludes();
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }

            if (keywords.Count == 0)
            {
                GUILayout.Label(emptyMessage, TexSlimStyles.StatusLabelStyle);
            }

            EditorGUILayout.Space(4f);
            // 空欄が残っているうちは追加させない（空行が溜まるのを防ぐ）
            using (new EditorGUI.DisabledScope(hasBlank))
            {
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(
                            hasBlank
                                ? L.T("＋ 追加（空欄を埋めてください）", "+ Add (fill the blank first)")
                                : L.T("＋ 追加", "+ Add"),
                            L.T("入力欄をひとつ増やします", "Adds one more input row")),
                        TexSlimStyles.NeutralColor,
                        TexSlimStyles.SmallColoredBtnStyle))
                {
                    Undo.RecordObject(component, "Add " + undoLabel);
                    keywords.Add(string.Empty);
                    MarkDirty();
                }
            }
        }

    }
}
