// ==============================================================================
// Product : TexSlim
// File    : TexSlimEditor.EasyTab.cs
// Role    : 「かんたん」タブの描画（現在の状態 → 圧縮の実行 → 圧縮設定）
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
        // ─── かんたんモード ───────────────────────────────────────────

        private void DrawEasyMode()
        {
            // 並び順は「現状（数字）→ 実行ボタン → 圧縮設定」。
            // このタブを開いた人がまず知りたいのは今どうなっているかで、
            // 次にするのはボタンを押すこと。設定は既定値のままで妥当に動くため後ろでよい。
            // 設定はボタンより下にあるので、ボタンの上に実行内容を1行で出して
            // スクロールせずに「何が起きるか」が読めるようにする。
            DrawEasyDiagnostics();
            EditorGUILayout.Space(6f);

            DrawEasyActions();
            EditorGUILayout.Space(6f);

            DrawEasyDetailSettings();
        }

        // ─── ① 現状（診断） ──────────────────────────────────────────

        private void DrawEasyDiagnostics()
        {
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("現在の状態", "Current State"), TexSlimStyles.LabelStyle);
            GUILayout.FlexibleSpace();
            DrawRescanButton();
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);

            if (scan == null || scan.TextureCount == 0)
            {
                EditorGUILayout.HelpBox(
                    L.T("テクスチャが見つかりません。\n"
                        + "アバターの中にメッシュ（SkinnedMeshRenderer / MeshRenderer）があるか確認してください。",
                        "No textures found.\n"
                        + "Check that the avatar has SkinnedMeshRenderer / MeshRenderer components."),
                    MessageType.Warning);
            }
            else
            {
                GUILayout.BeginHorizontal();

                // 対象枚数
                GUILayout.BeginVertical(GUILayout.MinWidth(70f));
                GUILayout.Label(scan.IncludedTextureCount.ToString(),
                    TexSlimStyles.BigNumber(22, TexSlimStyles.PrimaryColor));
                GUILayout.Label(L.T("圧縮する枚数", "Textures to compress"), TexSlimStyles.StatusLabelStyle,
                    GUILayout.ExpandWidth(true));
                GUILayout.EndVertical();

                DrawVerticalDivider(44f);

                // ユーザーの実際の目標は「DL サイズ ◯MB 以下」「テクスチャメモリ ◯MB 以下」
                // という生の数字。比較したいのはアバター全体（保護・除外を含む全テクスチャ）
                // なので、合計は必ず全体で出す。対象のみの合計だと保護中の顔・髪が抜けて
                // 実際より小さく見え、「50MB 以下にできた」と誤解させてしまう。
                GUILayout.BeginVertical(GUILayout.MinWidth(140f));
                if (scan.TotalVramBytes > 0)
                {
                    // DL サイズ（テクスチャ分）。Crunch の効果はここに現れるが事前予測は
                    // 当てにならないため現在値のみ。圧縮後は再スキャンで実測値に更新される。
                    if (scan.TotalStorageBytes > 0)
                    {
                        GUILayout.Label(
                            L.F("非圧縮サイズ（テクスチャ分）: {0}", "Uncompressed (textures): {0}",
                                TextureSizeUtil.BytesToLabel(scan.TotalStorageBytes)),
                            TexSlimStyles.TintedStatus(TexSlimStyles.PrimaryColor, bold: true));
                    }

                    // 圧縮後の全体推定 = 全体 −（対象の現在）＋（対象の圧縮後推定）
                    long estimatedTotal = scan.TotalVramBytes - scan.IncludedVramBytes + scan.EstimatedVramBytes;
                    GUILayout.Label(
                        L.F("テクスチャメモリ: {0} → 約 {1}", "Texture memory: {0} → approx. {1}",
                            TextureSizeUtil.BytesToLabel(scan.TotalVramBytes),
                            TextureSizeUtil.BytesToLabel(estimatedTotal)),
                        TexSlimStyles.TintedStatus(TexSlimStyles.PrimaryColor, bold: true));

                    // 上2行が何の合計なのかを言うだけの行。設定値（最大サイズ）は
                    // 別の話なので混ぜない（1行に2つ入れると、どちらの説明か読み取れない）。
                    // VRChat のアバター情報にも同名の「非圧縮サイズ」があるので、
                    // どれと見比べればよいかをここで示す。
                    GUILayout.Label(
                        L.T("アバター全体の合計（保護・圧縮不可も含む）／VRChat の「非圧縮サイズ」と対応します",
                            "Whole-avatar totals, protected included / matches VRChat's \"Uncompressed Size\""),
                        TexSlimStyles.StatusLabelStyle, GUILayout.ExpandWidth(true));
                }
                else
                {
                    GUILayout.Label(L.T("サイズ情報を取得できませんでした", "Could not read size info"),
                        TexSlimStyles.StatusLabelStyle);
                }
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();

                // 内訳を出す。「対象 12 枚」だけだと、残りがどこへ消えたのか分からず、
                // 保護機能が働いていること自体がかんたんタブから見えない。
                int excluded = scan.TextureCount
                             - scan.IncludedTextureCount
                             - scan.ProtectedTextureCount
                             - scan.SkippedAssetCount;
                EditorGUILayout.Space(2f);
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    L.F("全 {0} 枚の内訳:", "Breakdown of {0}:", scan.TextureCount),
                    TexSlimStyles.StatusLabelStyle, GUILayout.Width(80f));
                GUILayout.Label(
                    L.F("保護 {0}", "Protected {0}", scan.ProtectedTextureCount),
                    TexSlimStyles.TintedStatus(TexSlimStyles.ProtectedColor, bold: true),
                    GUILayout.Width(70f));
                if (excluded > 0)
                    GUILayout.Label(
                        L.F("除外 {0}", "Excluded {0}", excluded),
                        TexSlimStyles.TintedStatus(TexSlimStyles.ExcludedColor, bold: true),
                        GUILayout.Width(70f));
                if (scan.SkippedAssetCount > 0)
                    GUILayout.Label(
                        L.F("圧縮不可 {0}", "Can't compress {0}", scan.SkippedAssetCount),
                        TexSlimStyles.TintedStatus(TexSlimStyles.DimTextColor, bold: true),
                        GUILayout.Width(60f));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Unity 上で出せる数字と、VRChat のアバター情報に出る数字は一致しない。
                // 実測では テクスチャメモリで約2.5%、ダウンロードサイズに至っては桁が違った
                // （VRChat の DL サイズはアセットバンドル圧縮後の値で、Editor では測れない）。
                // ここを書いておかないと「ツールの数字が嘘」と受け取られる。
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(
                    L.T("ここの数字は Unity 上での推定値です。VRChat のアバター情報に出る数字とは数%ずれます。\n"
                        + "VRChat の「ダウンロードサイズ」は、ここからさらに圧縮された値です。\n"
                        + "見比べるときは、アバター情報の「非圧縮サイズ」のほうと比べてください。",
                        "These numbers are estimates made on the Unity side, and differ from VRChat's\n"
                        + "avatar info by a few percent. VRChat's \"Download Size\" is compressed further\n"
                        + "than this, so compare against \"Uncompressed Size\" in the avatar info instead."),
                    MessageType.None);

                // Crunch は「読み込み時に CPU で DXT へ展開される」形式であって、
                // GPU 上で圧縮されたまま使われるわけではない。
                // したがって VRAM 上のサイズは非 Crunch と1バイトも変わらない
                // （実測でも両モードとも 80.66MB で完全一致した）。
                if (component.Mode == TexSlimComponent.CompressionMode.CrunchOnly
                    || component.Mode == TexSlimComponent.CompressionMode.Both)
                {
                    EditorGUILayout.HelpBox(
                        L.T("Crunch はアバターの読み込み時に CPU で展開されるため、\n"
                            + "テクスチャメモリ（VRAM）は減りません。減るのはダウンロードサイズだけです。\n"
                            + "そのぶん画質は下がり、読み込み時に一瞬のひっかかりが出ることがあります。",
                            "Crunch is decompressed on the CPU while the avatar loads, so texture memory\n"
                            + "(VRAM) does not change. Only the download size drops.\n"
                            + "In exchange you lose some quality and may get a brief hitch on load."),
                        MessageType.None);
                }

                DrawUncompressedFormatWarning();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 非圧縮フォーマットの警告と修正ボタン。
        /// 解像度をいくら下げても、圧縮形式が None のままだと VRAM は約4倍のまま。
        /// 初心者には完全に不可視の問題なので、診断としてここで拾う。
        /// </summary>
        private void DrawUncompressedFormatWarning()
        {
            if (scan == null || scan.UncompressedFixableCount == 0)
            {
                // 修正対象は無いが、保護・除外の中に非圧縮が残っている場合だけ一言添える。
                // （黙っていると「直したのに数字が大きいまま」の原因が分からない）
                if (scan != null && scan.UncompressedSkippedCount > 0)
                {
                    EditorGUILayout.HelpBox(
                        L.F("保護・除外中のテクスチャに、圧縮形式が None のものが {0} 枚あります。\n"
                            + "これらは指定どおり、そのままにしています。",
                            "{0} protected/excluded textures still use compression format None.\n"
                            + "They are left alone, as you asked."),
                        MessageType.None);
                }
                return;
            }

            EditorGUILayout.HelpBox(
                L.F("{0} 枚の圧縮形式が None（非圧縮）のままです。\n"
                    + "同じ解像度でも、圧縮済みテクスチャの約4倍の VRAM を使います。\n"
                    + "直すと約 {1} 減る見込みです。",
                    "{0} textures still use compression format None (uncompressed).\n"
                    + "At the same resolution they take about 4x the VRAM of a compressed texture.\n"
                    + "Fixing them should save about {1}.",
                    scan.UncompressedFixableCount,
                    TextureSizeUtil.BytesToLabel(scan.UncompressedFixSavings)),
                MessageType.Warning);

            if (TexSlimStyles.ColoredButton(
                    new GUIContent(
                        L.T("非圧縮フォーマットを直す", "Fix uncompressed formats"),
                        L.T("圧縮形式を None から Compressed に変えます。解像度は変えません。\n"
                            + "元の画像ファイルはそのままなので、[↩ 元に戻す] でいつでも取り消せます。",
                            "Changes the compression format from None to Compressed. Resolutions stay.\n"
                            + "Source images are untouched; [↩ Restore] undoes it at any time.")),
                    TexSlimStyles.PrimaryColor,
                    TexSlimStyles.SmallColoredBtnStyle))
            {
                RunFixUncompressed();
            }
        }

        // ─── ② アクション（実行ボタン） ──────────────────────────────

        private void DrawEasyActions()
        {
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            GUILayout.Label(L.T("圧縮の実行", "Compress"), TexSlimStyles.LabelStyle);
            EditorGUILayout.Space(4f);

            // 詳細設定を畳んだままでも「押したら何が起きるか」が分かるようにする。
            // モードと最大サイズ、保護の有無はここで文字にして見せる。
            EditorGUILayout.HelpBox(
                L.F("実行内容: {0}", "This will: {0}", CompressionPlanSummary()),
                MessageType.None);
            EditorGUILayout.Space(2f);

            bool canCompress = scan != null && scan.IncludedTextureCount > 0;
            using (new EditorGUI.DisabledScope(!canCompress))
            {
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(CompressButtonLabel, CompressButtonTooltip),
                        canCompress ? TexSlimStyles.PrimaryColor : TexSlimStyles.NeutralColor,
                        TexSlimStyles.LargeColoredBtnStyle))
                {
                    RunCompression();
                }
            }
            if (!canCompress)
                EditorGUILayout.HelpBox(
                    L.T("圧縮できるテクスチャが1枚もありません。\n"
                        + "すべて保護されているか、詳細タブでスイッチが OFF になっている可能性があります。",
                        "There is nothing to compress.\n"
                        + "Everything may be protected, or switched off on the Detail tab."),
                    MessageType.Warning);

            EditorGUILayout.Space(6f);

            bool canRevert = AvatarHasCompressed;
            using (new EditorGUI.DisabledScope(!canRevert))
            {
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(L.T("↩ 元に戻す", "↩ Restore"),
                            L.T("圧縮したテクスチャを、圧縮前の画質に戻します",
                                "Restores the import settings of compressed textures to their original state")),
                        canRevert ? TexSlimStyles.DangerColor : TexSlimStyles.NeutralColor,
                        TexSlimStyles.SmallColoredBtnStyle))
                {
                    RunRevert();
                }
            }
            EditorGUILayout.HelpBox(
                L.T("↩ 元に戻す：インポート設定を圧縮前の値へ戻します。元の画像ファイルには手を加えていないので、画質は完全に元どおりになります。",
                    "↩ Restore: reverts import settings to their pre-compression values. "
                    + "Source images are untouched, so quality is fully restored."),
                MessageType.None);

            EditorGUILayout.EndVertical();
        }

        // ─── ③ 圧縮設定 ──────────────────────────────────────────────

        /// <summary>
        /// 圧縮モード・最大サイズ・テクスチャ保護。
        /// 折りたたまず常に見せる。かんたんタブでも「今どの設定で圧縮するのか」は
        /// 隠さずその場で確認・変更できるほうが迷わないため。
        /// 実行ボタンより下に置くのは、既定値のままでも妥当に動くから。
        /// </summary>
        private void DrawEasyDetailSettings()
        {
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);

            // ── 圧縮モード
            GUILayout.Label(L.T("圧縮モード", "Compression Mode"), TexSlimStyles.LabelStyle);
            EditorGUILayout.Space(4f);

            // モード3択と最大サイズを1行に詰めると、Inspector を狭めたときに
            // 右端（最大サイズ）が押し出されて見えなくなる。詳細タブと同じく行を分ける。
            GUILayout.BeginHorizontal();
            DrawModeButtons();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("最大サイズ", "Max Size"), TexSlimStyles.LabelStyle, GUILayout.Width(68f));
            DrawMaxSizePopup();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);

            switch (component.Mode)
            {
                case TexSlimComponent.CompressionMode.ResolutionOnly:
                    EditorGUILayout.HelpBox(
                        L.T("解像度のみ（おすすめ）\n"
                            + "「最大サイズ」より大きいテクスチャを小さくします。保存形式は変えません。\n"
                            + "テクスチャメモリとダウンロードサイズの両方が減り、画質の劣化も1回で済みます。\n"
                            + "顔や髪など大事なテクスチャは、下の「テクスチャ保護」で自動的に守られます。",
                            "Resolution only (recommended)\n"
                            + "Shrinks textures larger than Max Size. The storage format is left alone.\n"
                            + "Both texture memory and download size drop, with only one pass of quality loss.\n"
                            + "Important textures like face and hair are kept safe by Texture Protection below."),
                        MessageType.None);
                    break;
                case TexSlimComponent.CompressionMode.Both:
                    EditorGUILayout.HelpBox(
                        L.T("解像度＋Crunch\n"
                            + "小さくしたうえで、さらに Crunch 圧縮もかけます。\n"
                            + "ダウンロードサイズはもう少しだけ減りますが、実測では 1MB 程度の差でした。\n"
                            + "テクスチャメモリは「解像度のみ」と変わりません。",
                            "Resolution + Crunch\n"
                            + "Shrinks textures, then applies Crunch compression on top.\n"
                            + "Download size drops a little further - about 1MB in our measurement.\n"
                            + "Texture memory is the same as Resolution only."),
                        MessageType.None);
                    break;
                case TexSlimComponent.CompressionMode.CrunchOnly:
                    EditorGUILayout.HelpBox(
                        L.T("Crunchのみ（解像度を保ちたいとき）\n"
                            + "解像度は変えず、テクスチャの保存形式だけを Crunch に変えます。\n"
                            + "解像度を下げるよりは変化が小さいものの、無劣化ではありません。\n"
                            + "テクスチャメモリ（VRAM）は減らないので、軽量化が目的なら他の2つを使ってください。",
                            "Crunch only (when you want to keep the resolution)\n"
                            + "Keeps resolution and only changes the storage format to Crunch.\n"
                            + "It changes less than shrinking does, but it is not lossless.\n"
                            + "Texture memory (VRAM) does not drop, so use one of the other two to save weight."),
                        MessageType.Warning);
                    break;
            }

            EditorGUILayout.Space(8f);

            // ── テクスチャ保護
            GUILayout.Label(L.T("テクスチャ保護", "Texture Protection"), TexSlimStyles.LabelStyle);
            EditorGUILayout.Space(4f);

            // キーワード一覧は必ず実際の配列から作る。
            // 表示文字列を手書きすると、キーワードを足したときに実挙動とズレる。
            DrawProtectionToggle(
                component.PreserveFaceAndEyes,
                L.T("顔・瞳を保護", "Protect face & eyes"),
                string.Join(" / ", AvatarTextureScanner.FaceEyeKeywords),
                "Toggle Face Eye Protection",
                value => component.PreserveFaceAndEyes = value);

            EditorGUILayout.Space(6f);

            DrawProtectionToggle(
                component.ProtectHair,
                L.T("髪を保護", "Protect hair"),
                string.Join(" / ", AvatarTextureScanner.HairKeywords),
                "Toggle Hair Protection",
                value => component.ProtectHair = value);

            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox(
                L.T("保護したテクスチャは圧縮されず、今の画質のまま残ります。\n"
                    + "メッシュ名・マテリアル名・テクスチャ名のどれかに上のキーワードが\n"
                    + "入っていれば、自動で保護されます。\n"
                    + "顔・瞳・髪 以外にも守りたいものがあれば「設定」タブで足せます。",
                    "Protected textures are never compressed and keep their current quality.\n"
                    + "A texture is protected automatically when one of the keywords above appears\n"
                    + "in its mesh, material, or texture name.\n"
                    + "You can add more on the Settings tab."),
                MessageType.None);

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 「圧縮を実行」で何が起きるかを1文にまとめる。
        /// 設定カードは実行ボタンより下にあるため、押す前に読めるのはこの1行だけになる。
        /// 記号で区切らず、そのまま読める日本語にする。
        /// </summary>
        private string CompressionPlanSummary()
        {
            string what;
            switch (component.Mode)
            {
                case TexSlimComponent.CompressionMode.CrunchOnly:
                    what = L.T("解像度はそのままで、Crunch圧縮をかけます",
                               "keep the resolution and apply Crunch compression");
                    break;
                case TexSlimComponent.CompressionMode.Both:
                    what = L.F("テクスチャを {0}px 以下まで小さくして、Crunch圧縮もかけます",
                               "shrink textures to {0}px or smaller and apply Crunch compression",
                               component.MaxTextureSize);
                    break;
                default:   // ResolutionOnly（既定）
                    what = L.F("テクスチャを {0}px 以下まで小さくします",
                               "shrink textures to {0}px or smaller", component.MaxTextureSize);
                    break;
            }

            var kept = new List<string>();
            if (component.PreserveFaceAndEyes) kept.Add(L.T("顔・瞳", "face & eyes"));
            if (component.ProtectHair)         kept.Add(L.T("髪", "hair"));
            string protection = kept.Count > 0
                ? L.F("{0}は保護されるので、そのままです。",
                      "Your {0} textures are protected and left alone.",
                      string.Join(L.T("と", " and "), kept))
                : L.T("保護は使っていないので、対象すべてに適用されます。",
                      "No category is protected, so this applies to every included texture.");

            return what + "。" + protection;
        }

        /// <summary>圧縮モードの 3 択ボタン（かんたん・詳細タブで共用）</summary>
        private void DrawModeButtons()
        {
            var modes = new[]
            {
                // 並びは「解像度を下げる → 下げて更に Crunch → Crunch だけ」。
                // 既定であり推奨でもある解像度のみを先頭に置く。
                (mode: TexSlimComponent.CompressionMode.ResolutionOnly,
                 label: L.T("解像度のみ", "Resolution only"),
                 tip:   L.T("解像度だけ下げます。テクスチャメモリが減るのはこの部分です",
                            "Only shrinks the resolution. This is what reduces texture memory")),
                (mode: TexSlimComponent.CompressionMode.Both,
                 label: L.T("解像度＋Crunch", "Resolution + Crunch"),
                 tip:   L.T("さらに Crunch もかけます。ダウンロードサイズがもう少しだけ減ります",
                            "Applies Crunch on top. Download size drops a little further")),
                (mode: TexSlimComponent.CompressionMode.CrunchOnly,
                 label: L.T("Crunchのみ", "Crunch only"),
                 tip:   L.T("解像度を保ったまま保存形式だけ変えます。テクスチャメモリは減りません",
                            "Keeps the resolution, changes only the format. Texture memory stays the same")),
            };

            foreach (var m in modes)
            {
                bool isActive = component.Mode == m.mode;
                Color bg = isActive ? TexSlimStyles.ActiveTabColor : TexSlimStyles.NeutralColor;
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(m.label, m.tip), bg, TexSlimStyles.SmallColoredBtnStyle)
                    && !isActive)
                {
                    Undo.RecordObject(component, "Change Compression Mode");
                    component.Mode = m.mode;
                    MarkDirty();
                    RefreshIncludes();
                }
            }
        }

        /// <summary>詳細タブ用の省スペースな保護トグル（スイッチ＋短いラベルのみ）</summary>
        private void DrawCompactProtectionToggle(
            bool value, string label, string undoLabel, System.Action<bool> setter)
        {
            EditorGUI.BeginChangeCheck();
            // {0} には「顔・瞳」などのカテゴリ名が入る。判定はキーワード一致なので、
            // 「その文字を含むもの」ではなく「そのカテゴリと判定されたもの」と書く。
            bool next = TexSlimGUI.DrawToggleSwitch(value,
                tooltip: L.F("{0} と判定されたテクスチャを圧縮しないようにします",
                             "Keeps textures detected as {0} uncompressed", label));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(component, undoLabel);
                setter(next);
                MarkDirty();
                RefreshIncludes();
            }
            GUILayout.Space(4f);
            GUILayout.Label(label, TexSlimStyles.TintedLabel(
                value ? TexSlimStyles.ProtectedColor : TexSlimStyles.DimTextColor),
                GUILayout.ExpandWidth(false));
        }

        /// <summary>保護カテゴリのトグル行（スイッチ＋見出し＋キーワード一覧）</summary>
        private void DrawProtectionToggle(
            bool value, string title, string keywords, string undoLabel, System.Action<bool> setter)
        {
            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool next = TexSlimGUI.DrawToggleSwitch(value);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(component, undoLabel);
                setter(next);
                MarkDirty();
                RefreshIncludes();
            }
            GUILayout.Space(6f);
            GUILayout.BeginVertical();
            GUILayout.Label(title, TexSlimStyles.TintedLabel(
                value ? TexSlimStyles.ProtectedColor : TexSlimStyles.DimTextColor));
            GUILayout.Label(keywords, TexSlimStyles.TintedMini(TexSlimStyles.DimTextColor));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 再スキャンボタン。カード見出しの右端に置く副次的な操作。
        /// <para>
        /// 階層変更時は <c>hierarchyChanged</c> で自動再スキャンされるため、
        /// これは「マテリアルのテクスチャ差し替え」など自動検知できない変更に対する保険。
        /// 主要アクション（圧縮・元に戻す）と同列に並べない。
        /// </para>
        /// </summary>
        private void DrawRescanButton()
        {
            if (TexSlimStyles.ColoredButton(
                    new GUIContent(L.T("再スキャン", "Rescan"),
                        L.T("アバターのテクスチャ情報を読み直します。\n"
                            + "オブジェクトの増減は自動で反映されるため、通常は不要です。\n"
                            + "マテリアルのテクスチャを差し替えたときなどに使ってください。",
                            "Re-reads the avatar's texture info.\n"
                            + "Object changes are picked up automatically, so this is rarely needed.\n"
                            + "Use it after swapping textures on a material.")),
                    TexSlimStyles.NeutralColor,
                    TexSlimStyles.CompactButton(BTN_RESCAN_WIDTH, 20f, 10),
                    GUILayout.Width(BTN_RESCAN_WIDTH), GUILayout.Height(20f)))
            {
                RefreshScan();
            }
        }

        /// <summary>
        /// グローバル最大サイズのドロップダウン。
        /// CrunchOnly では解像度を変えないので、効かない設定は操作させない。
        /// </summary>
        private void DrawMaxSizePopup()
        {
            bool ignored = component.Mode == TexSlimComponent.CompressionMode.CrunchOnly;
            using (new EditorGUI.DisabledScope(ignored))
            {
                EditorGUI.BeginChangeCheck();
                int next = EditorGUILayout.IntPopup(
                    component.MaxTextureSize, MaxSizeLabels, MaxSizeValues, GUILayout.Width(72f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(component, "Change Max Texture Size");
                    component.MaxTextureSize = next;
                    MarkDirty();
                    RefreshIncludes();
                }
            }

            if (ignored)
            {
                GUILayout.Label(
                    new GUIContent(L.T("（効きません）", "(no effect)"),
                        L.T("「Crunchのみ」は解像度を変えないので、この設定は効きません",
                            "Crunch-only mode does not change resolution, so this setting has no effect")),
                    TexSlimStyles.TintedStatus(TexSlimStyles.DimTextColor),
                    GUILayout.Width(48f));
            }
        }

    }
}
