// ==============================================================================
// Product : TexSlim
// File    : TexSlimEditor.cs
// Role    : コンポーネント Inspector の CustomEditor。
//           かえポン！の OnGUI パターンを Inspector 版として踏襲。
//           詳細モードはサムネイル付き ROW_HEIGHT 行リストで視覚的に整理。
//
// 重要 : このファイルの中で GUIStyle を new しないこと。
//        OnInspectorGUI は毎フレーム走るため、派生スタイルは必ず
//        TexSlimStyles のキャッシュ（RowName / TintedStatus など）から取得する。
// ==============================================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TexSlimComponent = global::HoriCraft.TexSlim.TexSlim;

namespace HoriCraft.TexSlim.Editor
{
    [CustomEditor(typeof(TexSlimComponent))]
    public sealed partial class TexSlimEditor : UnityEditor.Editor
    {
        // ─── レイアウト定数（かえポン！と同値） ─────────────────────────
        private const float ROW_HEIGHT   = 50f;
        private const float THUMB_SIZE   = 44f;
        private const float INDENT_CHILD = 20f;
        private const float INDENT_TEX   = 40f;
        // 「1024で圧縮」ボタンの幅。文字が切れると何のボタンか分からなくなるので余裕を持たせる
        // 「この1枚を圧縮」が切れない幅
        private const float BTN_INPLACE_WIDTH = 86f;
        private const float BTN_RESCAN_WIDTH  = 66f;
        private const float BTN_EXPAND_WIDTH  = 74f;
        // 詳細タブ「ツール」カードの左ラベル列。検索・表示・圧縮モード・最大サイズ・保護を
        // 同じ幅で揃えると、右側に並ぶのが「何の操作か」が読まなくても対応づく。
        private const float LABEL_COL_WIDTH   = 68f;

        private static readonly int[]    MaxSizeValues      = { 256, 512, 1024, 2048, 4096 };
        private static readonly string[] MaxSizeLabels      = { "256", "512", "1024", "2048", "4096" };
        // ─── 状態 ────────────────────────────────────────────────────
        private TexSlimComponent     component;
        private AvatarTextureScanResult scan;
        private string                  searchQuery = string.Empty;
        // 状態フィルタ（0=すべて）。保護・除外などを素早く絞り込むため。
        private int statusFilter;

        // ツリー/リスト表示の切り替え（EditorPrefs でユーザー単位に保存）。
        // 既定はリスト。この一覧で最初にやりたいのは「重いテクスチャを見つけて潰す」ことで、
        // 階層をたどる操作はその邪魔になる。構造で見たい人はツリーへ切り替えられる。
        private const string ViewModePrefKey = "TexSlim_ViewMode";
        private bool listView = true;

        // リスト表示の並び順（false=マテリアル順 / true=VRAM が大きい順）。
        // 「大きい順」は重いテクスチャから順に潰していく使い方のためにある。
        private const string ListSortPrefKey = "TexSlim_ListSortBySize";
        private bool listSortBySize;

        private static string[] StatusFilterLabels => L.English
            ? new[] { "All", "Included", "Compressed", "Protected", "Excluded", "N/A" }
            : new[] { "すべて", "対象", "圧縮済", "保護", "除外", "対象外" };

        // ツールチップ変化検出用（変化時にRepaint()を呼ぶ）
        private string                  lastTooltip = string.Empty;

        // アコーディオン展開状態
        private readonly HashSet<string> expandedObjects   = new HashSet<string>();
        private readonly HashSet<string> expandedMaterials = new HashSet<string>();

        // ─── ライフサイクル ──────────────────────────────────────────

        private void OnEnable()
        {
            component = target as TexSlimComponent;
            TexSlimStyles.Acquire();
            listView       = EditorPrefs.GetBool(ViewModePrefKey, true);
            listSortBySize = EditorPrefs.GetBool(ListSortPrefKey, false);

            // 旧バージョンの設定を移行する（保護キーワードの重複除去など）
            if (component != null && component.MigrateSettings())
            {
                MarkDirty();
                Debug.Log(
                    L.T("[TexSlim] 旧バージョンの設定を移行しました。"
                        + "組み込みと重複していた保護キーワードを整理しています。",
                        "[TexSlim] Migrated settings from an older version "
                        + "(removed protection keywords that duplicated built-ins)."),
                    component);
            }

            RefreshScan();
            // 階層変更時に自動再スキャン
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            TexSlimStyles.Release();
        }

        private void OnHierarchyChanged()
        {
            if (component != null) RefreshScan();
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            if (component == null)
            {
                // Undo でコンポーネントが消えた直後などに到達する
                EditorGUILayout.HelpBox(
                    L.T("コンポーネントを取得できませんでした。", "Could not access the component."),
                    MessageType.Warning);
                return;
            }

            TexSlimStyles.EnsureInitialized();

            if (scan == null) RefreshScan();

            DrawToolHeader();
            DrawTabBar();

            // ── ツールチップバーは上（タブ直下）に置く。
            // 最下部に置くと、内容が縦に長くてスクロールで隠れたときに説明が読めない。
            // 上に置く都合で表示するのは「前フレームの GUI.tooltip」になるが、
            // MouseMove とツールチップ変化時に Repaint しているので体感は即時。
            string tip = string.IsNullOrEmpty(lastTooltip)
                ? L.T("ボタンにマウスを乗せると説明が表示されます",
                      "Hover over a button to see its description")
                : lastTooltip;
            EditorGUILayout.HelpBox(tip, MessageType.None);
            EditorGUILayout.Space(6f);

            if      (component.ActiveTab == 0) DrawEasyMode();
            else if (component.ActiveTab == 1) DrawAdvancedMode();
            else                               DrawSettingsMode();

            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.Repaint && lastTooltip != GUI.tooltip)
            {
                lastTooltip = GUI.tooltip;
                Repaint();
            }
        }

        // ─── ヘッダー ─────────────────────────────────────────────────

        private void DrawToolHeader()
        {
            EditorGUILayout.BeginVertical(TexSlimStyles.CardStyle);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("TexSlim", TexSlimStyles.HeaderStyle);
            GUILayout.FlexibleSpace();

            // 言語切替はヘッダーに常時表示する。
            // 設定タブの中だけに置くと、日本語が読めない人は「設定」に
            // 言語設定があること自体に気づけない。
            // ラベルは「切替先の言語名を、その言語で」出す（英語話者には English が見える）。
            if (GUILayout.Button(
                    new GUIContent(L.English ? "日本語" : "English",
                        "表示言語を切り替えます / Switch display language"),
                    EditorStyles.miniButton, GUILayout.Width(76f)))
            {
                L.English = !L.English;
                Repaint();
            }

            GUILayout.Space(4f);
            GUILayout.Label("v" + TexSlimComponent.ToolVersion,
                TexSlimStyles.VersionLabelStyle, GUILayout.Width(40f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            {
                bool isCompressed = AvatarHasCompressed;
                GUILayout.Label(
                    isCompressed
                        ? L.F("圧縮済 {0}枚", "{0} compressed", scan.CompressedTextureCount)
                        : L.T("未圧縮", "Not compressed"),
                    TexSlimStyles.Chip(isCompressed
                        ? TexSlimStyles.AccentTeal
                        : TexSlimStyles.AccentGrey),
                    GUILayout.ExpandWidth(false));

                if (scan != null)
                    GUILayout.Label(
                        L.F("対象 {0} / 全 {1} 枚", "Included {0} / {1} textures",
                            scan.IncludedTextureCount, scan.TextureCount),
                        TexSlimStyles.MiniLabelStyle);
            }
            EditorGUILayout.EndHorizontal();

            string lastCompression = FormatLastCompression();
            if (lastCompression != null)
            {
                GUILayout.Label(lastCompression, TexSlimStyles.StatusLabelStyle);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>最終圧縮日時を「最終圧縮: yyyy/MM/dd HH:mm」形式で返す。未圧縮なら null。</summary>
        private string FormatLastCompression()
        {
            if (string.IsNullOrEmpty(component.LastCompressionUtc)) return null;

            string localTime = "―";
            if (System.DateTime.TryParseExact(
                component.LastCompressionUtc, "O", null,
                System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime dt))
            {
                localTime = dt.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
            }

            return L.F("最終圧縮: {0}", "Last compressed: {0}", localTime);
        }

        // ─── タブバー ─────────────────────────────────────────────────

        private void DrawTabBar()
        {
            var tabs = new[]
            {
                (label: L.T("かんたん", "Easy"),     tip: L.T("現在の状態を見て、圧縮と復元を実行します", "See the current state, then compress or restore")),
                (label: L.T("詳細",    "Detail"),   tip: L.T("テクスチャを1枚ずつ確認して、個別に設定します", "Check textures one by one and set them individually")),
                (label: L.T("設定",    "Settings"), tip: L.T("言語・Crunch品質・保護キーワードの管理", "Language, Crunch quality and protection keywords"))
            };

            GUILayout.BeginHorizontal();
            for (int i = 0; i < tabs.Length; i++)
            {
                Color bg = (component.ActiveTab == i)
                    ? TexSlimStyles.ActiveTabColor
                    : TexSlimStyles.NeutralColor;
                if (TexSlimStyles.ColoredButton(
                        new GUIContent(tabs[i].label, tabs[i].tip),
                        bg, TexSlimStyles.TabColoredBtnStyle))
                {
                    if (component.ActiveTab != i)
                    {
                        Undo.RecordObject(component, "Change Compressor Tab");
                        component.ActiveTab = i;
                        MarkDirty();
                    }
                }
            }
            GUILayout.EndHorizontal();
        }


        // ─── ユーティリティ ──────────────────────────────────────────

        /// <summary>木構造ごと作り直す（アセットへアクセスするので重い）</summary>
        private void RefreshScan()
        {
            if (component != null)
                scan = AvatarTextureScanner.Scan(component);
        }

        /// <summary>
        /// 設定変更時に、Include 判定と集計だけを再計算する。
        /// アセットへは触らないので、トグル操作のたびに走らせても軽い。
        /// </summary>
        private void RefreshIncludes()
        {
            if (scan != null) scan.RecomputeIncludes();
            else RefreshScan();
        }

        /// <summary>
        /// コンポーネントの変更をシーン／Prefab へ確実に反映する。
        /// アバターは Prefab インスタンスとして置かれることが多いため、
        /// SetDirty だけでなく Prefab オーバーライドとしても記録する。
        /// </summary>
        private void MarkDirty()
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        private static void SetExpanded(HashSet<string> set, string key, bool expanded)
        {
            if (expanded) set.Add(key);
            else          set.Remove(key);
        }

        private bool HasSearch => !string.IsNullOrWhiteSpace(searchQuery) || statusFilter != 0;

        /// <summary>テクスチャの状態カテゴリ（フィルタ・分類用。1=対象/2=圧縮済/3=保護/4=除外/5=対象外）</summary>
        private int TexCategory(AvatarObjectNode obj, AvatarMaterialNode mat, AvatarTextureNode tex)
        {
            if (!tex.IsProjectAsset || !tex.IsTexture2D) return 5;
            if (tex.ProtectedByName)                     return 3;
            if (tex.CompressedByTool)                    return 2;
            bool eff = component.GetObjectIncluded(obj.ObjectPath)
                    && component.GetMaterialIncluded(mat.Material)
                    && component.GetTextureIncluded(tex.Texture);
            return eff ? 1 : 4;
        }

        private bool TextureVisible(AvatarObjectNode obj, AvatarMaterialNode mat, AvatarTextureNode tex)
            // 共有テクスチャは代表行だけを表示する。
            // 設定はテクスチャ単位なので重複行には操作できるものがなく、ノイズにしかならない。
            // 「共有されていること」は代表行のサブテキスト（・N 箇所で共有）で分かる。
            => tex.IsPrimaryUsage
            && AvatarTextureScanner.MatchesSearch(tex, searchQuery)
            && (statusFilter == 0 || TexCategory(obj, mat, tex) == statusFilter);

        private bool MaterialVisible(AvatarObjectNode obj, AvatarMaterialNode mat)
            => AvatarTextureScanner.MatchesSearch(mat, searchQuery)
            && mat.Textures.Any(t => TextureVisible(obj, mat, t));

        private bool ObjectVisible(AvatarObjectNode obj)
            => obj.Renderer != null
            && AvatarTextureScanner.MatchesSearch(obj, searchQuery)
            && obj.Materials.Any(m => MaterialVisible(obj, m));

        /// <summary>このアバターにツールで圧縮済みのテクスチャがあるか（台帳ベース）</summary>
        private bool AvatarHasCompressed => scan != null && scan.CompressedTextureCount > 0;

        /// <summary>
        /// 検索中は一致した行を自動的に展開する。
        /// フィルタしても畳んだままだと、検索結果に辿り着くのに毎回2階層開く必要があり、
        /// 検索が機能していないように見えるため。
        /// </summary>
        private bool IsExpanded(HashSet<string> set, string key) => HasSearch || set.Contains(key);

        // 展開状態のキーはマテリアル参照に依存させない。
        // 圧縮すると slot のマテリアルが _Compressed.mat に変わり InstanceID もずれるため、
        // GetInstanceID を含めると圧縮のたびに展開状態を見失って畳まれてしまう。
        // Renderer の相対パス＋スロット番号は圧縮しても変わらないので、これをキーにする。
        private static string MaterialKey(AvatarObjectNode objNode, AvatarMaterialNode matNode)
            => objNode.ObjectPath + "::slot" + matNode.SlotIndex;

        /// <summary>
        /// アイコン領域のクリックで対象を Ping する共通処理。
        /// クリックを消費するので、行全体クリック（展開）とは二重発火しない。
        /// </summary>
        private void HandleIconPing(Rect iconRect, Object target)
        {
            if (target == null) return;

            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && iconRect.Contains(Event.current.mousePosition))
            {
                EditorGUIUtility.PingObject(target);
                Event.current.Use();
            }
            EditorGUIUtility.AddCursorRect(iconRect, MouseCursor.Zoom);
        }

        /// <summary>ツリーの全 Renderer / Material を展開する</summary>
        private void ExpandAll()
        {
            if (scan == null) return;
            foreach (AvatarObjectNode objNode in scan.Objects)
            {
                expandedObjects.Add(objNode.ObjectPath);
                foreach (AvatarMaterialNode matNode in objNode.Materials)
                    expandedMaterials.Add(MaterialKey(objNode, matNode));
            }
        }

        private void CollapseAll()
        {
            expandedObjects.Clear();
            expandedMaterials.Clear();
        }
    }
}
