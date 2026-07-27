// ==============================================================================
// Product : TexSlim
// File    : TexSlimEditor.DetailTab.cs
// Role    : 「詳細」タブの描画（操作と設定 / 現在の状態 / テクスチャ一覧・各行）
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
        // ─── 詳細モード ───────────────────────────────────────────────

        private void DrawAdvancedMode()
        {
            // ツールバー
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            // 「ツール」では中身が伝わらない。このカードが何を集めたものかを見出しで言う。
            // 検索と状態フィルタはここには置かない。あれは「一覧の絞り込み」であって
            // 圧縮の設定ではないため、絞り込まれる対象＝テクスチャ一覧のカードに置く。
            GUILayout.Label(L.T("操作と設定", "Actions & Settings"), TexSlimStyles.LabelStyle);
            EditorGUILayout.Space(4f);

            // 再スキャンはここには置かない。
            // 階層変更時は自動で走るため手動実行は保険であり、
            // 主要アクションと同じ並び・同じ重みにすると圧縮ボタンが埋もれる。
            // 実際に更新される対象であるテクスチャツリーのカード見出しへ置く。
            GUILayout.BeginHorizontal();
            bool canCompress = scan != null && scan.IncludedTextureCount > 0;
            using (new EditorGUI.DisabledScope(!canCompress))
            {
                // かんたんタブのボタンと同じ処理なので、名前も揃える。
                // 「一括圧縮」「選択分を圧縮」と呼び分けると、
                // 前者は個別の ON/OFF を無視すると誤解される。
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(CompressButtonLabel, CompressButtonTooltip),
                        canCompress ? TexSlimStyles.PrimaryColor : TexSlimStyles.NeutralColor,
                        TexSlimStyles.SmallColoredBtnStyle))
                    RunCompression();
            }

            bool canRevert = AvatarHasCompressed;
            using (new EditorGUI.DisabledScope(!canRevert))
            {
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(L.T("↩ 元に戻す", "↩ Restore"),
                            L.T("圧縮したテクスチャを、圧縮前の画質に戻します",
                                "Restores compressed textures to their original state")),
                        canRevert ? TexSlimStyles.DangerColor : TexSlimStyles.NeutralColor,
                        TexSlimStyles.SmallColoredBtnStyle))
                    RunRevert();
            }

            GUILayout.EndHorizontal();

            // 以降の3行は「左に何の設定か、右にその操作」で列を揃える。
            // ボタンやトグルだけを並べると、押す前に何を変えるものなのか分からない。
            // 圧縮モード（かんたんタブと同じ 3 ボタン。ドロップダウンより現在値が一目で分かる）
            EditorGUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(L.T("圧縮モード", "Mode"),
                    L.T("解像度を下げるか、Crunch圧縮をかけるか、その両方かを選びます",
                        "Chooses whether to shrink resolution, apply Crunch, or both")),
                TexSlimStyles.LabelStyle, GUILayout.Width(LABEL_COL_WIDTH));
            DrawModeButtons();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 最大サイズ（モード行とは別の設定なので、間隔は他の行と同じ 4px を空ける）
            EditorGUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(L.T("最大サイズ", "Max Size"),
                    L.T("これより大きいテクスチャを、このサイズまで小さくします",
                        "Textures larger than this are shrunk down to it")),
                TexSlimStyles.LabelStyle, GUILayout.Width(LABEL_COL_WIDTH));
            DrawMaxSizePopup();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 保護トグル（かんたんタブへ戻らず、詳細タブからも切り替えられるように）
            EditorGUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(L.T("保護", "Protect"),
                    L.T("ONにしたカテゴリのテクスチャは圧縮せず、今の画質のまま残します",
                        "Textures in the enabled categories are left uncompressed")),
                TexSlimStyles.LabelStyle, GUILayout.Width(LABEL_COL_WIDTH));
            DrawCompactProtectionToggle(
                component.PreserveFaceAndEyes, L.T("顔・瞳", "Face/Eyes"), "Toggle Face Eye Protection",
                v => component.PreserveFaceAndEyes = v);
            GUILayout.Space(12f);
            DrawCompactProtectionToggle(
                component.ProtectHair, L.T("髪", "Hair"), "Toggle Hair Protection",
                v => component.ProtectHair = v);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);
            DrawSummaryCard();
            EditorGUILayout.Space(6f);
            DrawTree();
        }


        // ─── サマリーカード ───────────────────────────────────────────

        private void DrawSummaryCard()
        {
            if (scan == null) RefreshScan();

            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);
            GUILayout.Label(L.T("現在の状態", "Current State"), TexSlimStyles.LabelStyle);
            EditorGUILayout.Space(4f);

            // ─ 枚数行
            GUILayout.BeginHorizontal();
            // 数値の色はテクスチャ行のバッジと同じ色相に揃え、
            // サマリーと一覧を目で対応づけられるようにする
            TexSlimGUI.DrawMetric(L.T("対象", "Included"),  scan.IncludedTextureCount,  TexSlimStyles.TargetColor);
            DrawVerticalDivider(38f);
            TexSlimGUI.DrawMetric(L.T("全体", "Total"),     scan.TextureCount,          TexSlimStyles.OnSurfaceColor);
            DrawVerticalDivider(38f);
            TexSlimGUI.DrawMetric(L.T("保護", "Protected"), scan.ProtectedTextureCount, TexSlimStyles.ProtectedColor);
            DrawVerticalDivider(38f);
            TexSlimGUI.DrawMetric(L.T("圧縮不可", "Can't compress"),     scan.SkippedAssetCount,     TexSlimStyles.DimTextColor);
            GUILayout.EndHorizontal();

            // ─ サイズ行（アバター全体の合計：現在 → 圧縮後）
            if (scan.TotalVramBytes > 0)
            {
                EditorGUILayout.Space(4f);
                Color accent = TexSlimStyles.CompressedColor;

                // 合計はアバター全体（保護・除外含む）。目標の MB と比較する数字はこちら。
                if (scan.TotalStorageBytes > 0)
                {
                    GUILayout.Label(
                        L.F("非圧縮サイズ（テクスチャ分）: {0}", "Uncompressed (textures): {0}",
                            TextureSizeUtil.BytesToLabel(scan.TotalStorageBytes)),
                        TexSlimStyles.TintedStatus(accent));
                }

                long estimatedTotal = scan.TotalVramBytes - scan.IncludedVramBytes + scan.EstimatedVramBytes;
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    L.F("テクスチャメモリ: {0} → {1}", "Texture memory: {0} → {1}",
                        TextureSizeUtil.BytesToLabel(scan.TotalVramBytes),
                        TextureSizeUtil.BytesToLabel(estimatedTotal)),
                    TexSlimStyles.TintedStatus(accent));
                GUILayout.FlexibleSpace();
                GUILayout.Label(L.T("（アバター全体の推定値。VRChat の表示とは数%ずれます）", "(estimate for the whole avatar; differs from VRChat by a few percent)"),
                    TexSlimStyles.TintedMini(TexSlimStyles.DimTextColor));
                GUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawVerticalDivider(float height)
        {
            Rect r = GUILayoutUtility.GetRect(1f, height, GUILayout.Width(1f));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(r, TexSlimStyles.OutlineColor);
        }

        // ─── テクスチャツリー ─────────────────────────────────────────

        private void DrawTree()
        {
            if (scan == null) RefreshScan();

            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);

            // ── 見出し（副次的な操作である再スキャンはここの右端）
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("テクスチャ一覧", "Textures"), TexSlimStyles.LabelStyle);
            GUILayout.FlexibleSpace();
            DrawRescanButton();
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);

            // ── 絞り込み（検索・状態フィルタ）
            // 絞り込む対象は下の一覧なので、この2つは一覧と同じカードに置く。
            // ラベル列の幅は「操作と設定」カードと同じにして、縦の位置を揃える。
            if (TexSlimGUI.DrawSearchBar(ref searchQuery, LABEL_COL_WIDTH)) Repaint();

            EditorGUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(L.T("絞り込み", "Filter"),
                    L.T("状態でしぼって表示します（保護されているものだけを見る、など）",
                        "Filters the list by state (e.g. show only protected rows)")),
                TexSlimStyles.LabelStyle, GUILayout.Width(LABEL_COL_WIDTH));
            string[] filterLabels = StatusFilterLabels;
            EditorGUI.BeginChangeCheck();
            int nextFilter = EditorGUILayout.Popup(statusFilter, filterLabels, GUILayout.Width(90f));
            if (EditorGUI.EndChangeCheck()) { statusFilter = nextFilter; Repaint(); }
            if (statusFilter != 0)
                GUILayout.Label(
                    L.F("「{0}」のみ表示中", "Showing only \"{0}\"", filterLabels[statusFilter]),
                    TexSlimStyles.TintedStatus(TexSlimStyles.WarnColor));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ── 表示形式（ツリー / リスト。リストのときだけ並び順も選べる）
            EditorGUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(L.T("表示形式", "Layout"),
                    L.T("メッシュごとの階層で見るか、テクスチャだけを並べて見るかを切り替えます",
                        "Switches between the hierarchical tree and a flat texture list")),
                TexSlimStyles.LabelStyle, GUILayout.Width(LABEL_COL_WIDTH));

            bool wantList = GUILayout.Toolbar(
                listView ? 1 : 0,
                new[]
                {
                    new GUIContent(L.T("ツリー", "Tree"), L.T("メッシュ → マテリアル → テクスチャの階層で表示", "Hierarchical view")),
                    new GUIContent(L.T("リスト", "List"), L.T("テクスチャだけを一覧表示", "Flat texture list")),
                },
                GUILayout.Width(120f), GUILayout.Height(20f)) == 1;
            if (wantList != listView)
            {
                listView = wantList;
                EditorPrefs.SetBool(ViewModePrefKey, listView);
            }

            // リスト表示のときだけ並び順を選べる（大きい順＝重い犯人から潰す使い方）
            if (listView)
            {
                GUILayout.Space(6f);
                string[] sortLabels = L.English
                    ? new[] { "By material", "Largest first" }
                    : new[] { "マテリアル順", "大きい順" };
                EditorGUI.BeginChangeCheck();
                int nextSort = EditorGUILayout.Popup(listSortBySize ? 1 : 0, sortLabels, GUILayout.Width(96f));
                if (EditorGUI.EndChangeCheck())
                {
                    listSortBySize = nextSort == 1;
                    EditorPrefs.SetBool(ListSortPrefKey, listSortBySize);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ── 一覧への一括操作。設定の行とは役割が違うのでラベル列に載せず、右へ寄せる。
            EditorGUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // 全 ON / OFF（Renderer 単位でまとめて切り替え）。
            // 検索・状態フィルタで絞り込んでいる間は、いま一覧に出ている Renderer だけを対象にする。
            // 隠れている行まで巻き込むと、絞り込んだつもりの操作で見えない場所の設定が変わり、
            // 何を変えたのか本人にも分からなくなる。
            {
                bool  filtered = HasSearch;
                float onWidth  = filtered ? 66f : 46f;
                float offWidth = filtered ? 70f : 50f;

                if (TexSlimStyles.ColoredButton(
                        new GUIContent(
                            filtered ? L.T("表示中をON", "Listed ON") : L.T("全ON", "All ON"),
                            filtered
                                ? L.T("いま一覧に出ているメッシュだけを圧縮対象にする",
                                      "Include only the renderers currently listed")
                                : L.T("一覧のメッシュをすべて圧縮対象にする", "Include every renderer")),
                        TexSlimStyles.NeutralColor,
                        TexSlimStyles.CompactButton(onWidth, 20f, 10),
                        GUILayout.Width(onWidth), GUILayout.Height(20f)))
                    SetAllObjectsIncluded(true);
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(
                            filtered ? L.T("表示中をOFF", "Listed OFF") : L.T("全OFF", "All OFF"),
                            filtered
                                ? L.T("いま一覧に出ているメッシュだけを圧縮対象から外す",
                                      "Exclude only the renderers currently listed")
                                : L.T("一覧のメッシュをすべて圧縮対象から外す", "Exclude every renderer")),
                        TexSlimStyles.NeutralColor,
                        TexSlimStyles.CompactButton(offWidth, 20f, 10),
                        GUILayout.Width(offWidth), GUILayout.Height(20f)))
                    SetAllObjectsIncluded(false);
            }
            GUILayout.Space(6f);

            if (!listView)
            {
                bool anyExpanded = expandedObjects.Count > 0 || expandedMaterials.Count > 0;
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(
                            anyExpanded ? L.T("すべて閉じる", "Collapse all") : L.T("すべて開く", "Expand all"),
                            L.T("メッシュとマテリアルの開閉をまとめて切り替えます", "Expand / collapse everything")),
                        TexSlimStyles.NeutralColor,
                        TexSlimStyles.CompactButton(BTN_EXPAND_WIDTH, 20f, 10),
                        GUILayout.Width(BTN_EXPAND_WIDTH), GUILayout.Height(20f)))
                {
                    if (anyExpanded) CollapseAll();
                    else             ExpandAll();
                }
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);

            if (AvatarHasCompressed)
            {
                EditorGUILayout.HelpBox(
                    L.F("「圧縮済」のテクスチャが {0} 枚あります。\n"
                        + "各行の [↩ 戻す]、または上部の [↩ 元に戻す] で圧縮前の状態へ復元できます。",
                        "{0} textures are compressed.\n"
                        + "Use [↩ Restore] on each row, or the [↩ Restore] button above, to revert.",
                        scan.CompressedTextureCount),
                    MessageType.Info);
                EditorGUILayout.Space(4f);
            }

            List<AvatarObjectNode> visible = scan.Objects
                .Where(ObjectVisible)
                .ToList();

            if (visible.Count == 0)
            {
                if (!HasSearch)
                {
                    if (scan.TextureCount == 0)
                    {
                        EditorGUILayout.HelpBox(
                            L.T("テクスチャが見つかりませんでした。\n"
                                + "アバターの中にメッシュ（SkinnedMeshRenderer 等）があるか確認してください。\n"
                                + "反映されない場合は右上の [再スキャン] を押してみてください。",
                                "No textures found.\n"
                                + "Check that the avatar has renderers (SkinnedMeshRenderer etc).\n"
                                + "If it still looks stale, try [Rescan] in the top-right corner."),
                            MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            L.T("このアバターのメッシュにテクスチャがありません。\n"
                                + "マテリアルにテクスチャが設定されているか確認してください。",
                                "No textures on these renderers.\n"
                                + "Check that the materials actually have textures assigned."),
                            MessageType.Warning);
                    }
                }
                else if (statusFilter != 0 && string.IsNullOrWhiteSpace(searchQuery))
                {
                    EditorGUILayout.HelpBox(
                        L.F("「{0}」のテクスチャはありません。", "No \"{0}\" textures.", StatusFilterLabels[statusFilter]),
                        MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        L.T("条件に一致するテクスチャがありません。\n検索語やフィルタを変えてください。",
                            "No textures match.\nTry a different search or filter."),
                        MessageType.None);
                }
            }
            else if (listView)
            {
                DrawFlatList(visible);
            }
            else
            {
                // Prefab別にグループ化（非 Prefab を先頭に）
                var groups = visible
                    .GroupBy(n => n.PrefabName)
                    .OrderBy(g => !string.IsNullOrEmpty(g.Key)) // 空文字列（非 Prefab）を先頭に
                    .ThenBy(g => g.Key)
                    .ToList();

                bool hasMultipleGroups = groups.Count > 1
                    || (groups.Count == 1 && !string.IsNullOrEmpty(groups[0].Key));

                foreach (var group in groups)
                {
                    if (hasMultipleGroups)
                    {
                        DrawPrefabGroupHeader(group.Key);
                        GUILayout.Space(2f);
                    }
                    foreach (AvatarObjectNode node in group)
                    {
                        DrawObjectRow(node);
                        GUILayout.Space(2f);
                    }
                    if (hasMultipleGroups) GUILayout.Space(4f);
                }
            }

            EditorGUILayout.EndVertical();
        }

        // ─── リスト表示（階層なし・テクスチャのみ） ────────────────────────
        //
        // 「どのマテリアルに付いているか」ではなく「どのテクスチャが重いか」を
        // 眺めながら1枚ずつ処理したいとき用。共有テクスチャは代表1行に畳む。
        // 行だけを並べると所属が分からなくなるため、
        // マテリアルの変わり目にパンくず（Prefab ▸ Renderer ▸ Material）付きの区切り線を挟む。
        private void DrawFlatList(List<AvatarObjectNode> visible)
        {
            // ── 大きい順：グループを崩して VRAM 降順に並べる。
            // 「50MB に収めたい」人が重いテクスチャから順に潰していくためのモード。
            // 並びがマテリアルをまたぐため、区切り線は出さない。
            if (listSortBySize)
            {
                var rows = new List<(AvatarObjectNode obj, AvatarMaterialNode mat, AvatarTextureNode tex)>();
                foreach (AvatarObjectNode objNode in visible)
                    foreach (AvatarMaterialNode matNode in objNode.Materials)
                        foreach (AvatarTextureNode texNode in matNode.Textures)
                            if (TextureVisible(objNode, matNode, texNode))
                                rows.Add((objNode, matNode, texNode));

                rows.Sort((a, b) =>
                    (b.tex.OriginalInfo?.RuntimeBytes ?? 0L)
                        .CompareTo(a.tex.OriginalInfo?.RuntimeBytes ?? 0L));

                foreach (var row in rows)
                {
                    DrawTextureRow(row.obj, row.mat, row.tex, indent: false);
                    GUILayout.Space(2f);
                }
                return;
            }

            // ── マテリアル順（既定）：所属ごとにパンくず付き区切り線を挟む
            Material lastMaterial = null;
            bool first = true;

            foreach (AvatarObjectNode objNode in visible)
            {
                foreach (AvatarMaterialNode matNode in objNode.Materials)
                {
                    foreach (AvatarTextureNode texNode in matNode.Textures)
                    {
                        // TextureVisible が代表ノード以外（共有の重複行）を弾く
                        if (!TextureVisible(objNode, matNode, texNode)) continue;

                        // 区切りは「見える行が実際にあるマテリアル」にだけ出す。
                        // 先に出してしまうと、フィルタで空になったグループの見出しが残る。
                        if (matNode.Material != lastMaterial)
                        {
                            if (!first) GUILayout.Space(6f);
                            DrawListGroupHeader(objNode, matNode);
                            lastMaterial = matNode.Material;
                        }
                        first = false;

                        DrawTextureRow(objNode, matNode, texNode, indent: false);
                        GUILayout.Space(2f);
                    }
                }
            }
        }

        /// <summary>リスト表示のグループ区切り（パンくずラベル＋右へ伸びる水平線）</summary>
        private void DrawListGroupHeader(AvatarObjectNode objNode, AvatarMaterialNode matNode)
        {
            string crumb =
                (string.IsNullOrEmpty(objNode.PrefabName) ? string.Empty : $"{objNode.PrefabName} ▸ ")
                + (objNode.Renderer != null ? objNode.Renderer.name : "?")
                + " ▸ " + (matNode.Material != null ? matNode.Material.name : "?");

            GUILayout.BeginHorizontal();
            GUILayout.Label(crumb,
                TexSlimStyles.TintedMini(TexSlimStyles.DimTextColor, bold: true),
                GUILayout.ExpandWidth(false));

            GUILayout.Space(6f);

            // 残り幅いっぱいの水平線
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            Rect line = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(line, TexSlimStyles.OutlineColor);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        // ─── Prefabグループヘッダー ──────────────────────────────────────

        private void DrawPrefabGroupHeader(string prefabName)
        {
            bool isAvatarBody = string.IsNullOrEmpty(prefabName);
            string label = isAvatarBody ? L.T("アバター本体", "Avatar Body") : $"{prefabName}";
            // グループ見出しもチップにして、区切り線だけの地味な見出しから色のある区切りにする
            Color accent = isAvatarBody ? TexSlimStyles.AccentGrey : TexSlimStyles.AccentOrange;
            Color lineColor = TexSlimStyles.OutlineColor;

            GUILayout.BeginHorizontal();

            // 左線
            Rect lineRect = GUILayoutUtility.GetRect(12f, 1f, GUILayout.Width(12f), GUILayout.Height(1f));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(lineRect.x, lineRect.center.y, lineRect.width, 1f), lineColor);

            GUILayout.Space(4f);

            GUILayout.Label(label, TexSlimStyles.Chip(accent), GUILayout.ExpandWidth(false));

            GUILayout.Space(4f);

            // 右線
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            Rect rightLine = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rightLine, lineColor);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        // ─── オブジェクト行 ──────────────────────────────────────────

        private void DrawObjectRow(AvatarObjectNode node)
        {
            if (node.Renderer == null) return;

            string key      = node.ObjectPath;
            bool   expanded = IsExpanded(expandedObjects, key);
            bool   included = component.GetObjectIncluded(node.ObjectPath);

            // ── ヘッダー行（ROW_HEIGHT 固定）
            GUILayout.BeginHorizontal(TexSlimStyles.NestedCardStyle, GUILayout.Height(ROW_HEIGHT));

            // ▶▼ 開閉ボタン
            if (TexSlimStyles.ColoredButton(
                    expanded ? "▼" : "▶",
                    included ? TexSlimStyles.ActiveTabColor : TexSlimStyles.NeutralColor,
                    TexSlimStyles.Arrow(24f, THUMB_SIZE, 11),
                    GUILayout.Width(24f), GUILayout.Height(THUMB_SIZE)))
            {
                SetExpanded(expandedObjects, key, !expanded);
            }
            // ▶ボタン領域をキャプチャ（行全体クリックとの二重発火防止）
            Rect expandBtnRect = GUILayoutUtility.GetLastRect();

            GUILayout.Space(4f);

            // Renderer アイコン（Unityビルトインアイコン）。クリックで Hierarchy に Ping。
            Texture2D rendIcon = EditorGUIUtility.ObjectContent(null, typeof(SkinnedMeshRenderer)).image as Texture2D;
            if (rendIcon != null)
                GUILayout.Label(rendIcon, GUILayout.Width(THUMB_SIZE), GUILayout.Height(THUMB_SIZE));
            else
                GUILayout.Box("", GUILayout.Width(THUMB_SIZE), GUILayout.Height(THUMB_SIZE));
            Rect rendIconRect = GUILayoutUtility.GetLastRect();
            HandleIconPing(rendIconRect, node.Renderer != null ? node.Renderer.gameObject : null);

            GUILayout.Space(6f);

            // オブジェクト名・情報
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            GUILayout.Label(node.Renderer.name,
                TexSlimStyles.RowName(12, included
                    ? (EditorGUIUtility.isProSkin ? Color.white : Color.black)
                    : TexSlimStyles.DimTextColor));

            int textureCount = node.Materials.Sum(m => m.Textures.Count);
            string subText = string.IsNullOrEmpty(node.PrefabName)
                ? $"{node.Materials.Count} materials  |  {textureCount} textures"
                : $"{node.PrefabName}  |  {node.Materials.Count} materials  |  {textureCount} textures";
            GUILayout.Label(subText, TexSlimStyles.StatusLabelStyle);

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // 含めるトグル（役割の説明はツールチップ側に持たせ、行幅を節約する）
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            bool nextInclude = TexSlimGUI.DrawToggleSwitch(
                included, tooltip: L.T("このメッシュのテクスチャをまとめて圧縮対象にする",
                                       "Include all textures on this renderer"));
            if (EditorGUI.EndChangeCheck())
            {
                SetObjectIncluded(node, nextInclude);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.Space(2f);
            GUILayout.EndHorizontal();

            // ── 行全体クリックで開閉。
            // Hierarchy や Project ウィンドウと同じ「行クリック＝展開」に合わせる。
            // ここを ON/OFF トグルにすると、開こうとしただけで Renderer 配下が
            // まるごと圧縮対象から外れる事故が起きる。ON/OFF はスイッチのみで行う。
            Rect rowRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && rowRect.Contains(Event.current.mousePosition)
                && !expandBtnRect.Contains(Event.current.mousePosition)) // ▶ボタンは除外
            {
                SetExpanded(expandedObjects, key, !expanded);
                Event.current.Use();
            }
            EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);

            // ── 展開コンテンツ
            if (expanded)
            {
                foreach (AvatarMaterialNode matNode in node.Materials
                    .Where(m => MaterialVisible(node, m)))
                {
                    DrawMaterialRow(node, matNode);
                    GUILayout.Space(2f);
                }
            }
        }

        private void SetObjectIncluded(AvatarObjectNode node, bool include)
        {
            Undo.RecordObject(component, "Toggle Object Compression");
            component.SetObjectIncluded(node.ObjectPath, include);
            MarkDirty();
            RefreshIncludes();
        }

        /// <summary>
        /// 一覧に出ている Renderer をまとめて ON/OFF する。
        /// フィルタ中は表示行だけが対象（ボタン側のラベルも「表示分ON/OFF」に変わる）。
        /// </summary>
        private void SetAllObjectsIncluded(bool include)
        {
            if (scan == null) return;

            List<AvatarObjectNode> targets = HasSearch
                ? scan.Objects.Where(ObjectVisible).ToList()
                : scan.Objects;
            if (targets.Count == 0) return;

            Undo.RecordObject(component, include ? "Include Renderers" : "Exclude Renderers");
            foreach (AvatarObjectNode node in targets)
                component.SetObjectIncluded(node.ObjectPath, include);
            MarkDirty();
            RefreshIncludes();
        }

        // ─── マテリアル行 ─────────────────────────────────────────────

        private void DrawMaterialRow(AvatarObjectNode objNode, AvatarMaterialNode node)
        {
            string key         = MaterialKey(objNode, node);
            bool   expanded    = IsExpanded(expandedMaterials, key);
            bool   objIncluded = component.GetObjectIncluded(objNode.ObjectPath);
            bool   matIncluded = component.GetMaterialIncluded(node.Material);
            bool   effective   = objIncluded && matIncluded;

            GUILayout.BeginHorizontal();
            GUILayout.Space(INDENT_CHILD);

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal(TexSlimStyles.NestedCardStyle, GUILayout.Height(ROW_HEIGHT));

            // ▶▼ 開閉ボタン
            if (TexSlimStyles.ColoredButton(
                    expanded ? "▼" : "▶",
                    effective ? TexSlimStyles.ActiveTabColor : TexSlimStyles.NeutralColor,
                    TexSlimStyles.Arrow(22f, THUMB_SIZE, 10),
                    GUILayout.Width(22f), GUILayout.Height(THUMB_SIZE)))
            {
                SetExpanded(expandedMaterials, key, !expanded);
            }
            Rect matExpandBtnRect = GUILayoutUtility.GetLastRect();

            GUILayout.Space(4f);

            // マテリアルプレビュー（AssetPreview）。クリックで Project に Ping。
            Texture2D matPreview = AssetPreview.GetAssetPreview(node.Material);
            if (matPreview != null)
                GUILayout.Label(matPreview, GUILayout.Width(THUMB_SIZE), GUILayout.Height(THUMB_SIZE));
            else
            {
                Texture2D matIcon = EditorGUIUtility.ObjectContent(node.Material, typeof(Material)).image as Texture2D;
                if (matIcon != null)
                    GUILayout.Label(matIcon, GUILayout.Width(THUMB_SIZE), GUILayout.Height(THUMB_SIZE));
                else
                    GUILayout.Box("", GUILayout.Width(THUMB_SIZE), GUILayout.Height(THUMB_SIZE));
            }
            Rect matPreviewRect = GUILayoutUtility.GetLastRect();
            HandleIconPing(matPreviewRect, node.Material);

            GUILayout.Space(6f);

            // マテリアル名・テクスチャ数
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            GUILayout.Label(node.Material.name,
                TexSlimStyles.RowName(11, effective
                    ? (EditorGUIUtility.isProSkin ? Color.white : Color.black)
                    : TexSlimStyles.DimTextColor));

            int included = node.Textures.Count(t => t.Include);
            GUILayout.Label(
                L.F("対象 {0} / {1} 枚", "Included {0} / {1} textures", included, node.Textures.Count),
                TexSlimStyles.StatusLabelStyle);

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // 含めるトグル（役割の説明はツールチップ側に持たせ、行幅を節約する）
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!objIncluded))
            {
                EditorGUI.BeginChangeCheck();
                bool nextInclude = TexSlimGUI.DrawToggleSwitch(
                    matIncluded, disabled: !objIncluded,
                    tooltip: objIncluded
                        ? L.T("このマテリアルのテクスチャを圧縮対象にする",
                              "Include this material's textures")
                        : L.T("このメッシュが圧縮対象から外れているため操作できません",
                              "Locked because the parent renderer is excluded"));
                if (EditorGUI.EndChangeCheck())
                {
                    SetMaterialIncluded(node, nextInclude);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.Space(2f);
            GUILayout.EndHorizontal(); // マテリアル行本体

            // ── 行全体クリックで開閉（Renderer 行と同じ規則。ON/OFF はスイッチのみ）
            Rect matRowRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && matRowRect.Contains(Event.current.mousePosition)
                && !matExpandBtnRect.Contains(Event.current.mousePosition))
            {
                SetExpanded(expandedMaterials, key, !expanded);
                Event.current.Use();
            }
            EditorGUIUtility.AddCursorRect(matRowRect, MouseCursor.Link);

            if (expanded)
            {
                GUILayout.Space(4f);
                foreach (AvatarTextureNode tex in node.Textures
                    .Where(t => TextureVisible(objNode, node, t)))
                {
                    DrawTextureRow(objNode, node, tex);
                    GUILayout.Space(2f);
                }
                GUILayout.Space(2f);
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void SetMaterialIncluded(AvatarMaterialNode node, bool include)
        {
            Undo.RecordObject(component, "Toggle Material Compression");
            component.SetMaterialIncluded(node.Material, include);
            MarkDirty();
            RefreshIncludes();
        }

        // ─── テクスチャ行（Material行と同スタイル） ──────────────────────────

        private void DrawTextureRow(
            AvatarObjectNode   objNode,
            AvatarMaterialNode matNode,
            AvatarTextureNode  texNode,
            bool indent = true)
        {
            bool isPro          = EditorGUIUtility.isProSkin;
            bool protectedTex   = texNode.ProtectedByName;
            bool parentsEnabled = component.GetObjectIncluded(objNode.ObjectPath)
                               && component.GetMaterialIncluded(matNode.Material);
            // 圧縮中でもツリーは原本を表示しているので、対象トグルもサイズも常に編集できる。
            // 変更は次の圧縮／再圧縮に反映される。
            bool canToggle      = parentsEnabled && texNode.IsProjectAsset && texNode.IsTexture2D
                               && !protectedTex;
            bool curInclude     = component.GetTextureIncluded(texNode.Texture);
            // 「圧縮済」＝台帳に圧縮前設定が控えられている（＝ツールで圧縮した）。
            bool hasCompressed  = texNode.CompressedByTool;
            bool canCompress    = texNode.IsProjectAsset && texNode.IsTexture2D && !protectedTex;
            // 復元は常に安全な操作なので、保護中でも戻せる。
            // （圧縮したあとに保護を ON にすると戻せなくなる、という詰みを防ぐ）
            bool canRevert      = texNode.IsProjectAsset && texNode.IsTexture2D && hasCompressed;

            // ここへ来る行は常に代表ノード（TextureVisible が非代表を弾いている）。
            // 共有テクスチャの重複行は表示しない：設定はテクスチャ単位で
            // 重複行に操作できるものがなく、ノイズにしかならないため。

            // ── 外側インデント（ツリーでは Material 行と揃える。リスト表示では不要）
            GUILayout.BeginHorizontal();
            if (indent) GUILayout.Space(INDENT_TEX);

            GUILayout.BeginHorizontal(TexSlimStyles.NestedCardStyle, GUILayout.Height(ROW_HEIGHT));

            // サムネイル
            Texture2D preview = texNode.Texture != null ? AssetPreview.GetAssetPreview(texNode.Texture) : null;
            if (preview != null)
                GUILayout.Label(preview, GUILayout.Width(THUMB_SIZE), GUILayout.Height(THUMB_SIZE));
            else
            {
                if (texNode.Texture != null && AssetPreview.IsLoadingAssetPreviews()) Repaint();
                Texture2D icon = texNode.Texture != null
                    ? EditorGUIUtility.ObjectContent(texNode.Texture, typeof(Texture2D)).image as Texture2D
                    : null;
                if (icon != null)
                    GUILayout.Label(icon, GUILayout.Width(THUMB_SIZE), GUILayout.Height(THUMB_SIZE));
                else
                    GUILayout.Box(string.Empty, GUILayout.Width(THUMB_SIZE), GUILayout.Height(THUMB_SIZE));
            }
            Rect thumbRect = GUILayoutUtility.GetLastRect(); // ウィンドウ座標（BeginAreaなし）

            GUILayout.Space(6f);

            // テクスチャ名 + 解像度 + サブテキスト（プロパティ名・共有数）
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            Color nameColor = !canToggle
                ? TexSlimStyles.DimTextColor
                : (isPro ? Color.white : Color.black);
            string dispName = texNode.Texture != null ? texNode.Texture.name : "(null)";
            GUILayout.Label(dispName, TexSlimStyles.RowName(11, nameColor));

            // 解像度はこの一覧で最も見たい数字なので、薄い極小文字にしない。
            // 名前と同じ 11px の太字にし、状態で色を変えて一目で追えるようにする。
            // プロパティ名や共有数は補足なので、これまでどおり小さく薄いまま右へ流す。
            GUILayout.BeginHorizontal();
            GUILayout.Label(BuildTextureSizeText(texNode, canToggle, curInclude),
                TexSlimStyles.RowName(11, TextureSizeColor(texNode, canToggle, curInclude)));
            GUILayout.Space(6f);
            GUILayout.Label(BuildTextureMetaText(texNode), TexSlimStyles.StatusLabelStyle);

            // 非圧縮フォーマットの行には警告を添える。修正はかんたんタブの診断からまとめて行う。
            if (texNode.OriginalInfo != null && texNode.OriginalInfo.IsUncompressedFormat
                && texNode.IsProjectAsset && texNode.IsTexture2D)
            {
                GUILayout.Space(6f);
                GUILayout.Label(
                    new GUIContent(
                        L.T("非圧縮", "Uncompressed"),
                        L.T("圧縮形式が None のため、同じ解像度の圧縮済みテクスチャの約4倍の VRAM を使います。\n"
                            + "かんたんタブの「非圧縮フォーマットを直す」でまとめて修正できます。",
                            "Compression format is None, so this uses about 4x the VRAM of a compressed\n"
                            + "texture at the same resolution. Fix it from the Easy tab.")),
                    TexSlimStyles.TintedStatus(TexSlimStyles.WarnColor, bold: true));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // 状態バッジ
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            DrawTextureBadge(objNode, texNode, protectedTex, canToggle, curInclude, hasCompressed);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            // 個別サイズドロップダウン（圧縮可能なときのみ）
            if (canCompress)
            {
                GUILayout.Space(4f);
                int  curOverride = component.GetTextureMaxSizeOverride(texNode.Texture);
                bool hasOverride = curOverride > 0;

                // 選択肢の上限は「意味のある最大解像度」：
                //   未圧縮 → 現在の解像度（それ以上を選んでも拡大されない）
                //   圧縮済 → 台帳にある圧縮前の maxTextureSize（現在の解像度を上限にすると
                //            一度圧縮した後にサイズを上げ直す選択肢が消えてしまう）
                int sourceMax = texNode.CompressedByTool && texNode.OriginalMaxSize > 0
                    ? texNode.OriginalMaxSize
                    : texNode.OriginalInfo != null
                        ? Mathf.Max(texNode.OriginalInfo.Width, texNode.OriginalInfo.Height)
                        : 0;
                GetOverrideSizeChoices(sourceMax, out int[] values, out string[] labels);

                GUILayout.BeginVertical();
                GUILayout.FlexibleSpace();
                Color prevColor = GUI.color;
                if (hasOverride) GUI.color = TexSlimStyles.OverrideColor;
                EditorGUI.BeginChangeCheck();
                int newOverride = EditorGUILayout.IntPopup(curOverride, labels, values, GUILayout.Width(52f));
                GUI.color = prevColor;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(component, "Set Texture Max Size Override");
                    component.SetTextureMaxSizeOverride(texNode.Texture, newOverride);
                    MarkDirty();
                    RefreshIncludes();

                    // 圧縮済みの行では「サイズを選び直した＝そのサイズにしたい」なので、
                    // その場で 戻す→新しいサイズで再圧縮 を自動実行する。
                    // これがないと 512 で圧縮した後に 1024 へ上げる手段が
                    // 「戻す→サイズ選択→圧縮」の3手順になり、初心者には辿り着けない。
                    if (texNode.CompressedByTool)
                    {
                        int newEffective = component.GetEffectiveMaxSize(texNode.Texture);
                        if (texNode.OriginalInfo == null || newEffective != texNode.OriginalInfo.MaxSize)
                            RunResizeCompressed(texNode);
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
            }

            // 個別の圧縮／復元ボタン。圧縮済みなら「戻す」に切り替わる。
            // 戻すボタンは保護状態に関わらず出す（canRevert 参照）。
            if (canRevert || canCompress)
            {
                GUILayout.Space(2f);
                GUILayout.BeginVertical();
                GUILayout.FlexibleSpace();
                if (canRevert)       DrawSingleRevertButton(texNode);
                else                 DrawSingleCompressButton(texNode);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
            }

            // トグルスイッチ（役割の説明はツールチップ側に持たせ、行幅を節約する）
            GUILayout.Space(4f);
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!canToggle))
            {
                EditorGUI.BeginChangeCheck();
                bool nextInclude = TexSlimGUI.DrawToggleSwitch(
                    curInclude, disabled: !canToggle,
                    tooltip: canToggle
                        ? L.T("このテクスチャを圧縮対象にする", "Include this texture")
                        : L.T("保護中か、上のメッシュ・マテリアルが圧縮対象から外れているため操作できません",
                              "Locked: protected, or a parent is excluded"));
                if (EditorGUI.EndChangeCheck() && canToggle)
                {
                    SetTextureIncluded(texNode, nextInclude);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.Space(2f);
            GUILayout.EndHorizontal(); // NestedCardStyle

            Rect texRowRect = GUILayoutUtility.GetLastRect();

            // サムネイルクリック → Ping
            if (texNode.Texture != null
                && Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && thumbRect.Contains(Event.current.mousePosition))
            {
                EditorGUIUtility.PingObject(texNode.Texture);
                Event.current.Use();
            }

            // テクスチャ行には展開するものがないので、行クリックには何も割り当てない。
            // ここだけ「クリックで ON/OFF」にすると、親の行と規則が食い違って誤操作を招く。
            if (texNode.Texture != null)
                EditorGUIUtility.AddCursorRect(thumbRect, MouseCursor.Zoom);

            GUILayout.EndHorizontal(); // 外側インデント
        }

        private void SetTextureIncluded(AvatarTextureNode texNode, bool include)
        {
            Undo.RecordObject(component, "Toggle Texture Compression");
            component.SetTextureIncluded(texNode.Texture, include);
            MarkDirty();
            RefreshIncludes();
        }

        /// <summary>
        /// テクスチャ行の解像度表示。行の中で最も判断に使う数字なので独立させる。
        /// <list type="bullet">
        /// <item>圧縮済み … <c>512×512（元 2048）</c> — 今のサイズと元のサイズを併記</item>
        /// <item>これから縮む … <c>2048×2048 → 512×512</c></item>
        /// <item>変化しない … <c>2048×2048</c></item>
        /// </list>
        /// </summary>
        private string BuildTextureSizeText(
            AvatarTextureNode texNode, bool canToggle, bool curInclude)
        {
            TextureAssetInfo cur = texNode.OriginalInfo;  // OriginalInfo はスキャン時点＝現在の状態
            if (cur == null) return string.Empty;

            // 解像度に VRAM の実量を併記する。
            // 「大きい順」で順位は分かっても量が分からないと、
            // 「あと何枚潰せば目標に届くか」の逆算ができないため。
            string size = $"{cur.Width}×{cur.Height}";
            if (cur.RuntimeBytes > 0)
                size += " " + TextureSizeUtil.BytesToLabel(cur.RuntimeBytes);

            // 圧縮済み：現在サイズ（＝圧縮後）に、元の最大サイズが分かれば併記する
            if (texNode.CompressedByTool)
            {
                return texNode.OriginalMaxSize > 0
                    ? size + L.F("（元 {0}）", " (was {0})", texNode.OriginalMaxSize)
                    : size;
            }

            // 未圧縮：現在 → 圧縮後の推定。サイズが変わらないなら矢印を出さない
            // （「→ 2048×2048」は何も起きないのに変化するように見える）
            if (canToggle && curInclude
                && component.Mode != TexSlimComponent.CompressionMode.CrunchOnly)
            {
                TextureSizeUtil.ApplyMaxSize(
                    cur.Width, cur.Height,
                    component.GetEffectiveMaxSize(texNode.Texture),
                    out int predW, out int predH);
                if (predW != cur.Width || predH != cur.Height)
                {
                    string predicted = $"{predW}×{predH}";
                    long predictedBytes = scan != null ? scan.EstimateCompressedVram(texNode) : 0L;
                    if (predictedBytes > 0)
                        predicted += " " + TextureSizeUtil.BytesToLabel(predictedBytes);
                    return $"{size} → {predicted}";
                }
            }

            return size;
        }

        /// <summary>解像度表示の色。状態を色でも区別できるようにバッジと色相を揃える。</summary>
        private Color TextureSizeColor(
            AvatarTextureNode texNode, bool canToggle, bool curInclude)
        {
            if (texNode.OriginalInfo == null)  return TexSlimStyles.DimTextColor;
            if (texNode.CompressedByTool)      return TexSlimStyles.CompressedColor;
            if (!canToggle || !curInclude)     return TexSlimStyles.DimTextColor;

            // これから縮むものだけ「対象」の色で強調する。
            // Crunch のみモードは解像度が変わらないので強調しない。
            if (component.Mode != TexSlimComponent.CompressionMode.CrunchOnly)
            {
                TextureSizeUtil.ApplyMaxSize(
                    texNode.OriginalInfo.Width, texNode.OriginalInfo.Height,
                    component.GetEffectiveMaxSize(texNode.Texture),
                    out int predW, out int predH);
                if (predW != texNode.OriginalInfo.Width || predH != texNode.OriginalInfo.Height)
                    return TexSlimStyles.TargetColor;
            }

            return TexSlimStyles.OnSurfaceColor;
        }

        /// <summary>解像度の右に流す補足（プロパティ名・共有数）。</summary>
        private string BuildTextureMetaText(AvatarTextureNode texNode)
        {
            // 共有テクスチャの重複行は表示しないため、共有情報はここ（代表行）だけが頼り。
            string shared = texNode.UsageCount > 1
                ? L.F("・{0} 箇所で共有", "- shared in {0} places", texNode.UsageCount)
                : string.Empty;

            if (string.IsNullOrEmpty(shared))   return texNode.PropertyName;
            if (string.IsNullOrEmpty(texNode.PropertyName)) return shared;
            return texNode.PropertyName + "　" + shared;
        }

        /// <summary>テクスチャ行の状態バッジを描画する</summary>
        private void DrawTextureBadge(
            AvatarObjectNode objNode, AvatarTextureNode texNode,
            bool protectedTex, bool canToggle, bool curInclude, bool hasCompressed)
        {
            string badgeText;
            string tooltip = string.Empty;
            Color  accent;

            // 状態ごとに色相を変える。色だけ見て区別できるようにするため、
            // 近い意味でも同じ色は使わない（対象=緑 / 圧縮済=ティール など）。
            if (protectedTex)
            {
                badgeText = L.T("保護", "Protected");
                tooltip   = L.F("保護キーワードに一致したため圧縮対象から除外されています。\n一致理由: {0}",
                                "Excluded because a protection keyword matched.\nReason: {0}",
                                texNode.ProtectionReason);
                accent    = TexSlimStyles.AccentBlue;
            }
            else if (!texNode.IsProjectAsset || !texNode.IsTexture2D)
            {
                badgeText = L.T("圧縮不可", "Can't compress");
                tooltip   = !texNode.IsProjectAsset
                    ? L.T("Project 内のアセットではないため圧縮できません。",
                          "Cannot compress: not a project asset.")
                    : L.T("Texture2D ではないため圧縮できません（Cubemap / RenderTexture など）。",
                          "Cannot compress: not a Texture2D (Cubemap / RenderTexture etc).");
                accent    = TexSlimStyles.AccentGrey;
            }
            else if (!canToggle)
            {
                bool objOff = !component.GetObjectIncluded(objNode.ObjectPath);
                badgeText = objOff ? "Renderer↓" : "Material↓";
                tooltip   = objOff
                    ? L.T("このテクスチャを使っているメッシュが圧縮対象から外れています。", "The parent renderer is excluded.")
                    : L.T("親のマテリアルが圧縮対象から外れています。", "The parent material is excluded.");
                accent    = TexSlimStyles.AccentAmber;
            }
            else if (!curInclude)
            {
                badgeText = L.T("除外", "Excluded");
                tooltip   = L.T("手動で圧縮対象から外されています。", "Manually excluded from compression.");
                accent    = TexSlimStyles.AccentRed;
            }
            else if (hasCompressed)
            {
                badgeText = L.T("圧縮済", "Compressed");
                tooltip   = L.T("このテクスチャは圧縮されています。", "This texture is compressed.");
                accent    = TexSlimStyles.AccentTeal;
            }
            else
            {
                badgeText = L.T("対象", "Included");
                tooltip   = L.T("次回の圧縮で処理されます。", "Will be processed by the next compression.");
                accent    = TexSlimStyles.AccentGreen;
            }

            GUILayout.Label(new GUIContent(badgeText, tooltip),
                TexSlimStyles.Chip(accent),
                GUILayout.ExpandWidth(false));
        }

        /// <summary>
        /// このテクスチャ1枚を、左隣のサイズドロップダウンで選ばれた値で今すぐ圧縮するボタン。
        /// <para>
        /// サイズを選ぶ場所は行にひとつ（<c>Auto (N)</c> ドロップダウン）だけに保ち、
        /// このボタンは<strong>値を持たず、その値を実行に移すだけ</strong>にする。
        /// ボタンとドロップダウンの両方にサイズ一覧を置くと、ユーザーから見て
        /// 同じことを2箇所で選ばされることになり役割が重複する。
        /// </para>
        /// <para>
        /// ラベルには実行される値をそのまま出す（例: <c>1024で圧縮</c>）。
        /// ドロップダウンを変えるとラベルも追従するので、押す前に結果が確定して見える。
        /// </para>
        /// </summary>
        private void DrawSingleCompressButton(AvatarTextureNode texNode)
        {
            // ボタンには数字（サイズ）を出さない。
            // サイズは左のドロップダウンで選ぶので、ボタンにも数字を載せると
            // 「サイズ選択が2つある」ように見えてしまう。ボタンは動作名だけにする。
            string tooltip = L.F(
                "このテクスチャ1枚だけを今すぐ圧縮します。\n"
                + "適用内容: {0}\n"
                + "インポート設定を変えるだけで、元の画像ファイルはそのままです。\n"
                + "戻すときはこの [↩ 戻す] を使ってください。\n"
                + "サイズは左のドロップダウンで変更できます。",
                "Compresses only this texture, right now.\n"
                + "Effect: {0}\n"
                + "Only import settings change; the source image is untouched.\n"
                + "Use the [↩ Restore] button on this row to revert.\n"
                + "Pick the size in the dropdown on the left.",
                DescribeSingleEffect(texNode));

            if (TexSlimStyles.ColoredButton(
                    new GUIContent(L.T("この1枚を圧縮", "Compress this"), tooltip),
                    TexSlimStyles.NeutralColor,
                    TexSlimStyles.CompactButton(BTN_INPLACE_WIDTH, 20f, 10),
                    GUILayout.Width(BTN_INPLACE_WIDTH), GUILayout.Height(20f)))
            {
                RunSingleCompress(texNode);
            }
        }

        /// <summary>この1枚を圧縮前へ戻すボタン（圧縮済みの行に出る）</summary>
        private void DrawSingleRevertButton(AvatarTextureNode texNode)
        {
            string tooltip = texNode.OriginalMaxSize > 0
                ? L.F("このテクスチャを圧縮前（最大サイズ {0}）へ戻します。",
                      "Restores this texture to before compression (max size {0}).", texNode.OriginalMaxSize)
                : L.T("このテクスチャを圧縮前のインポート設定へ戻します。",
                      "Restores this texture's pre-compression import settings.");

            if (TexSlimStyles.ColoredButton(
                    new GUIContent(L.T("↩ 戻す", "↩ Restore"), tooltip),
                    TexSlimStyles.AccentAmber,
                    TexSlimStyles.CompactButton(BTN_INPLACE_WIDTH, 20f, 10),
                    GUILayout.Width(BTN_INPLACE_WIDTH), GUILayout.Height(20f)))
            {
                RunSingleRevert(texNode);
            }
        }

        /// <summary>個別圧縮で実際に起きることをモードに合わせて説明する</summary>
        private string DescribeSingleEffect(AvatarTextureNode texNode)
        {
            int size = component.GetEffectiveMaxSize(texNode.Texture);
            return component.Mode switch
            {
                TexSlimComponent.CompressionMode.CrunchOnly
                    => L.T("Crunch圧縮をかけます（解像度はそのまま）",
                           "apply Crunch compression (resolution unchanged)"),
                TexSlimComponent.CompressionMode.ResolutionOnly
                    => L.F("{0}px 以下まで小さくします（Crunchはかけません）",
                           "shrink it to {0}px or smaller (no Crunch)", size),
                _   => L.F("{0}px 以下まで小さくして、Crunch圧縮もかけます",
                           "shrink it to {0}px or smaller and apply Crunch compression", size),
            };
        }

        /// <summary>
        /// 個別サイズドロップダウンの選択肢を組み立てる。
        /// 元テクスチャの解像度を超える値は、選んでも拡大されず無意味なので除外する。
        /// </summary>
        private void GetOverrideSizeChoices(int sourceMax, out int[] values, out string[] labels)
        {
            List<int> vals = new List<int> { -1 };
            List<string> labs = new List<string> { $"Auto ({component.MaxTextureSize})" };

            foreach (int size in MaxSizeValues)
            {
                // sourceMax 不明（0）のときは従来どおり全部出す
                if (sourceMax > 0 && size > sourceMax) continue;
                vals.Add(size);
                labs.Add(size.ToString());
            }

            values = vals.ToArray();
            labels = labs.ToArray();
        }

    }
}
