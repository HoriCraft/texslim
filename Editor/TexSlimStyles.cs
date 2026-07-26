// ==============================================================================
// Product : TexSlim
// File    : TexSlimStyles.cs
// Role    : GUIスタイルの初期化、カラーボタンヘルパー、
//           テクスチャ生成ユーティリティを担う。
//           かえポン！の Styles.cs と同一の設計パターンを採用。
//
// 重要 : GUIStyle と Texture2D は OnGUI の中で作ってはならない。
//        OnInspectorGUI は 1 秒に何十回も走るため、その場で new すると
//        生成物が際限なく積み上がる。派生スタイルは必ずここのキャッシュ経由で得ること。
// ==============================================================================

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HoriCraft.TexSlim.Editor
{
    /// <summary>
    /// TexSlim 用のスタイルシステム。
    /// かえポン！の GUIStyle 設計パターンをベースに、グリーンテーマで構築。
    /// </summary>
    internal static class TexSlimStyles
    {
        // ─── レイアウト定数 ───────────────────────────────────────────
        public const float  LargeButtonHeight = 40f;
        public const float  SmallButtonHeight = 28f;
        public const int    FontSizeTitle     = 15;
        public const int    FontSizeNormal    = 12;

        // Material の角丸。カード > 行 > ボタン > チップ の順に小さくする
        private const int CardCorner   = 10;
        private const int RowCorner    = 8;
        private const int ButtonCorner = 10;
        private const int ChipCorner   = 8;

        // ─── Material Design カラーパレット ───────────────────────────
        //
        // 状態ごとに「色相」を分けることで、バッジを読まなくても色で区別できるようにする。
        // 値は Material Design のスウォッチをそのまま使う。
        //
        //   Accent          … 塗りボタン・アイコン・強調テキスト
        //   Container       … バッジ（チップ）の背景。Accent を背景色へ寄せた淡い色
        //   OnContainer     … Container の上に載るテキスト色
        //
        // Container / OnContainer は Accent から自動生成する（下の Container / OnContainer）。
        // 20 個以上の色を手で調整すると背景とのなじみが崩れるため、生成に寄せている。

        public static readonly Color AccentGreen  = Hex(0x4CAF50); // 圧縮対象・主要アクション
        public static readonly Color AccentTeal   = Hex(0x26A69A); // 圧縮済み
        public static readonly Color AccentBlue   = Hex(0x2196F3); // 保護
        public static readonly Color AccentAmber  = Hex(0xFFB300); // 親側で除外されている
        public static readonly Color AccentRed    = Hex(0xEF5350); // 明示的な除外・元に戻す
        public static readonly Color AccentPurple = Hex(0xAB47BC); // 共有テクスチャ
        public static readonly Color AccentOrange = Hex(0xFF7043); // 個別サイズ上書き中
        public static readonly Color AccentGrey   = Hex(0x78909C); // 対象外

        // ─── 意味づけ（スキンに応じて EnsureInitialized で解決する） ───
        public static Color PrimaryColor;    // 圧縮・成功
        public static Color DangerColor;     // 元に戻す
        public static Color NeutralColor;    // 副次的なボタン
        public static Color ActiveTabColor;  // 選択中タブ
        public static Color ProtectedColor;  // 保護
        public static Color DimTextColor;    // 補足テキスト（OnSurfaceVariant）
        public static Color WarnColor;       // 親側で除外
        public static Color ExcludedColor;   // 明示的な除外
        public static Color TargetColor;     // 圧縮対象
        public static Color CompressedColor; // 圧縮済み
        public static Color OverrideColor;   // 個別サイズ上書き中
        public static Color OnSurfaceColor;  // 本文テキスト
        public static Color OutlineColor;    // 区切り線

        /// <summary>0xRRGGBB 形式のリテラルから Color を作る</summary>
        private static Color Hex(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>
        /// チップの背景色。Material Design のトーンに合わせる：
        /// Light は container ≒ tone 90（ごく淡く、彩度を上げすぎない）、
        /// Dark は container ≒ tone 30（下地よりわずかに持ち上げる）。
        /// Light 側の混合比を上げすぎると原色が主張して悪目立ちする。
        /// </summary>
        public static Color Container(Color accent)
            => EditorGUIUtility.isProSkin
                ? Color.Lerp(new Color(0.165f, 0.170f, 0.180f), accent, 0.32f)
                : Color.Lerp(Color.white, accent, 0.14f);

        /// <summary>
        /// チップ上のテキスト色（on-container）。
        /// Light は tone 10 相当まで落としてコントラスト比 ≥ 4.5:1 を確保する。
        /// </summary>
        public static Color OnContainer(Color accent)
            => EditorGUIUtility.isProSkin
                ? Color.Lerp(accent, Color.white, 0.55f)
                : Color.Lerp(accent, Color.black, 0.50f);

        /// <summary>
        /// 地のテキストに使う accent。
        /// Dark では明度を上げ、Light では明るい下地に沈まないよう暗くする。
        /// </summary>
        private static Color ForText(Color accent)
            => EditorGUIUtility.isProSkin
                ? Color.Lerp(accent, Color.white, 0.25f)
                : Color.Lerp(accent, Color.black, 0.35f);

        // ─── スタイルフィールド ───────────────────────────────────────
        private static bool _initialized;
        private static bool _initializedForProSkin;
        private static int  _activeEditors;

        // テキスト系
        public static GUIStyle HeaderStyle;
        public static GUIStyle LabelStyle;
        public static GUIStyle MiniLabelStyle;
        public static GUIStyle StatusLabelStyle;
        public static GUIStyle VersionLabelStyle;

        // カード
        public static GUIStyle CardStyle;
        public static GUIStyle NestedCardStyle;

        // ボタン（カラー用 — 常に白文字ベース）
        public static GUIStyle LargeColoredBtnStyle;
        public static GUIStyle SmallColoredBtnStyle;
        public static GUIStyle TabColoredBtnStyle;

        // ツリー行
        public static GUIStyle RowHeaderStyle;

        // その他
        public static GUIStyle SearchFieldStyle;
        public static GUIStyle ClearButtonStyle;

        // ─── キャッシュ ───────────────────────────────────────────────
        private static readonly List<Texture2D>              GeneratedTextures = new List<Texture2D>();
        private static readonly Dictionary<string, GUIStyle> StyleCache        = new Dictionary<string, GUIStyle>();
        private static readonly Dictionary<string, GUIStyle> DerivedCache      = new Dictionary<string, GUIStyle>();

        // ─── ライフサイクル ───────────────────────────────────────────

        /// <summary>Editor の OnEnable から呼ぶ。生存中の Inspector 数を数える。</summary>
        public static void Acquire()
        {
            _activeEditors++;
        }

        /// <summary>
        /// Editor の OnDisable から呼ぶ。
        /// 最後の Inspector が閉じたときだけテクスチャを破棄する。
        /// （Inspector をロックして複数開いているとき、片方の終了で
        ///  もう片方が参照中のテクスチャを壊さないようにするため）
        /// </summary>
        public static void Release()
        {
            _activeEditors = Mathf.Max(0, _activeEditors - 1);
            if (_activeEditors == 0)
            {
                DisposeGenerated();
            }
        }

        /// <summary>
        /// 全 GUIStyle を初期化する。
        /// OnInspectorGUI から毎フレーム呼ばれるが、フラグで二重初期化を防ぐ。
        /// スキン（Pro/Light）が変わったときだけ作り直す。
        /// </summary>
        public static void EnsureInitialized()
        {
            bool isPro = EditorGUIUtility.isProSkin;
            if (_initialized && _initializedForProSkin == isPro) return;

            // スキン変更による作り直しでは、前回生成分を確実に捨てる
            DisposeGenerated();

            // ─── カラーロールの解決 ──────────────────────────────────
            // 塗りボタンは白文字なので、Light スキンでは Accent をそのまま使うと
            // コントラストが不足する。Material の tone 40 相当まで沈めた色を使う。
            PrimaryColor    = isPro ? AccentGreen : Hex(0x388E3C);
            DangerColor     = isPro ? AccentRed   : Hex(0xC62828);
            ActiveTabColor  = PrimaryColor;
            // 副次的なボタンも白文字前提。Light の薄グレーでは白が読めないため
            // スレート寄りの中間色（白文字で 4.5:1 を満たす）にする。
            NeutralColor    = isPro ? new Color(0.32f, 0.33f, 0.35f) : Hex(0x5C6570);

            ProtectedColor  = ForText(AccentBlue);
            WarnColor       = ForText(AccentAmber);
            ExcludedColor   = ForText(AccentRed);
            TargetColor     = ForText(AccentGreen);
            CompressedColor = ForText(AccentTeal);
            OverrideColor   = ForText(AccentOrange);

            OnSurfaceColor  = isPro ? new Color(0.92f, 0.93f, 0.94f) : new Color(0.11f, 0.12f, 0.13f);
            DimTextColor    = isPro ? new Color(0.62f, 0.64f, 0.67f) : new Color(0.42f, 0.45f, 0.48f);
            OutlineColor    = isPro ? new Color(0.40f, 0.42f, 0.45f) : new Color(0.72f, 0.74f, 0.77f);

            Color textColor = OnSurfaceColor;
            // Material の elevation に倣い、入れ子になるほど surface を一段持ち上げる
            Color cardColor  = isPro ? new Color(0.165f, 0.170f, 0.180f) : new Color(0.960f, 0.965f, 0.972f);
            Color nestedCard = isPro ? new Color(0.215f, 0.222f, 0.235f) : new Color(0.898f, 0.910f, 0.925f);

            // ─── テキスト ────────────────────────────────────────────
            HeaderStyle = ForceColor(
                new GUIStyle(EditorStyles.boldLabel) { fontSize = FontSizeTitle, fontStyle = FontStyle.Bold },
                new Color(0.47f, 0.87f, 0.57f)); // アクセントグリーン

            LabelStyle = ForceColor(
                new GUIStyle(EditorStyles.boldLabel) { fontSize = FontSizeNormal, fontStyle = FontStyle.Bold },
                textColor);

            MiniLabelStyle = ForceColor(
                new GUIStyle(EditorStyles.miniLabel),
                isPro ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.40f, 0.40f, 0.40f));

            StatusLabelStyle = ForceColor(
                new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 },
                isPro ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.45f, 0.45f, 0.45f));

            VersionLabelStyle = ForceColor(
                new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontSize = 9 },
                isPro ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.55f, 0.55f, 0.55f));

            // ─── カード（Material の角丸カード。9スライスで角を保つ） ──
            CardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin  = new RectOffset(4, 4, 4, 6),
                border  = new RectOffset(CardCorner, CardCorner, CardCorner, CardCorner),
                normal  = { background = MakeRoundedTex(CardCorner * 2 + 2, CardCorner * 2 + 2, cardColor, CardCorner) }
            };

            NestedCardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin  = new RectOffset(2, 2, 2, 3),
                border  = new RectOffset(RowCorner, RowCorner, RowCorner, RowCorner),
                normal  = { background = MakeRoundedTex(RowCorner * 2 + 2, RowCorner * 2 + 2, nestedCard, RowCorner) }
            };

            RowHeaderStyle = ForceColor(
                new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, alignment = TextAnchor.MiddleLeft },
                textColor);

            // ─── カラーボタン用（常に白文字ベース） ──────────────────
            GUIStyle baseBtn = new GUIStyle(GUI.skin.button);
            LargeColoredBtnStyle = ForceColor(
                new GUIStyle(baseBtn) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = LargeButtonHeight },
                Color.white);
            SmallColoredBtnStyle = ForceColor(
                new GUIStyle(baseBtn) { fontSize = 12, fontStyle = FontStyle.Bold, fixedHeight = SmallButtonHeight },
                Color.white);
            TabColoredBtnStyle = ForceColor(
                new GUIStyle(baseBtn) { fontSize = 12, fontStyle = FontStyle.Bold, fixedHeight = 34f },
                Color.white);

            // ─── 検索フィールド ───────────────────────────────────────
            SearchFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                fontSize    = 12,
                fixedHeight = 24f,
                padding     = new RectOffset(6, 4, 3, 3)
            };

            ClearButtonStyle = ForceColor(
                new GUIStyle(GUIStyle.none)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize  = 13,
                    padding   = new RectOffset(0, 0, 0, 0)
                },
                isPro ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.40f, 0.40f, 0.40f));

            _initialized           = true;
            _initializedForProSkin = isPro;
        }

        // ─── 派生スタイル（すべてキャッシュ経由） ──────────────────────

        /// <summary>行のタイトル用スタイル。色とサイズの組み合わせごとにキャッシュされる。</summary>
        public static GUIStyle RowName(int fontSize, Color color, bool italic = false)
        {
            return Derive($"rowname|{fontSize}|{ColorKey(color)}|{italic}", RowHeaderStyle, s =>
            {
                s.fontSize  = fontSize;
                s.fontStyle = italic ? FontStyle.Italic : FontStyle.Bold;
                ForceColor(s, color);
            });
        }

        /// <summary>StatusLabelStyle をベースに色を差し替えた小さいラベル</summary>
        public static GUIStyle TintedStatus(Color color, bool bold = false)
        {
            return Derive($"status|{ColorKey(color)}|{bold}", StatusLabelStyle, s =>
            {
                s.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                ForceColor(s, color);
            });
        }

        /// <summary>MiniLabelStyle をベースに色を差し替えたラベル</summary>
        public static GUIStyle TintedMini(Color color, bool bold = false)
        {
            return Derive($"mini|{ColorKey(color)}|{bold}", MiniLabelStyle, s =>
            {
                s.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                ForceColor(s, color);
            });
        }

        /// <summary>LabelStyle をベースに色を差し替えた見出し</summary>
        public static GUIStyle TintedLabel(Color color)
        {
            return Derive($"label|{ColorKey(color)}", LabelStyle, s => ForceColor(s, color));
        }

        /// <summary>サマリー等で使う大きな数値ラベル</summary>
        public static GUIStyle BigNumber(int fontSize, Color color)
        {
            return Derive($"bignum|{fontSize}|{ColorKey(color)}", EditorStyles.boldLabel, s =>
            {
                s.fontSize  = fontSize;
                s.alignment = TextAnchor.MiddleCenter;
                ForceColor(s, color);
            });
        }

        /// <summary>
        /// Material のトーナルチップ（塗りつぶしバッジ）スタイル。
        /// <para>
        /// 状態を色付きテキストだけで示すと、目立たないうえ色数も稼げない。
        /// Accent から生成した淡い背景を敷き、その上に濃い同系色を載せることで
        /// 一覧を見渡したときに状態が色面として飛び込んでくるようにする。
        /// </para>
        /// </summary>
        public static GUIStyle Chip(Color accent)
        {
            return Derive($"chip|{ColorKey(accent)}|{EditorGUIUtility.isProSkin}", EditorStyles.miniLabel, s =>
            {
                s.fontSize  = 10;
                s.fontStyle = FontStyle.Bold;
                s.alignment = TextAnchor.MiddleCenter;
                s.padding   = new RectOffset(8, 8, 2, 2);
                s.margin    = new RectOffset(2, 2, 0, 0);
                s.border    = new RectOffset(ChipCorner, ChipCorner, ChipCorner, ChipCorner);
                s.fixedHeight = 18f;

                Texture2D bg = MakeRoundedTex(
                    ChipCorner * 2 + 2, ChipCorner * 2 + 2, Container(accent), ChipCorner);
                s.normal.background = bg;
                s.hover.background  = bg;
                ForceColor(s, OnContainer(accent));
            });
        }

        /// <summary>行の中に置く小さなアクションボタン用スタイル</summary>
        public static GUIStyle CompactButton(float width, float height, int fontSize)
        {
            return Derive($"compactbtn|{width}|{height}|{fontSize}", SmallColoredBtnStyle, s =>
            {
                s.fixedWidth  = width;
                s.fixedHeight = height;
                s.fontSize    = fontSize;
                s.alignment   = TextAnchor.MiddleCenter;
                s.padding     = new RectOffset(2, 2, 0, 0);
            });
        }

        /// <summary>アコーディオンの ▶▼ ボタン用スタイル</summary>
        public static GUIStyle Arrow(float size, float height, int fontSize)
        {
            return Derive($"arrow|{size}|{height}|{fontSize}", SmallColoredBtnStyle, s =>
            {
                s.fixedWidth  = size;
                s.fixedHeight = height;
                s.fontSize    = fontSize;
                s.alignment   = TextAnchor.MiddleCenter;
                s.padding     = new RectOffset(0, 0, 0, 0);
            });
        }

        private static GUIStyle Derive(string key, GUIStyle baseStyle, Action<GUIStyle> configure)
        {
            if (DerivedCache.TryGetValue(key, out GUIStyle cached)) return cached;

            GUIStyle style = new GUIStyle(baseStyle);
            configure(style);
            DerivedCache[key] = style;
            return style;
        }

        // ─── ColoredButton ────────────────────────────────────────────

        /// <summary>背景色付きボタンを描画する（テキスト版）</summary>
        public static bool ColoredButton(string text, Color bgColor, GUIStyle baseStyle, params GUILayoutOption[] options)
            => GUILayout.Button(text, GetColoredStyle(baseStyle, bgColor), options);

        /// <summary>背景色付きボタンを描画する（GUIContent 版）</summary>
        public static bool ColoredButton(GUIContent content, Color bgColor, GUIStyle baseStyle, params GUILayoutOption[] options)
            => GUILayout.Button(content, GetColoredStyle(baseStyle, bgColor), options);

        // ─── スタイルキャッシュ ───────────────────────────────────────

        /// <summary>
        /// 指定ベーススタイルと背景色の組み合わせで色付きスタイルを生成しキャッシュする。
        /// <para>
        /// キーはベーススタイルの「内容」から組み立てる。
        /// GUIStyle は GetHashCode をオーバーライドしていないため参照ハッシュになり、
        /// 呼び出し側が毎フレーム new したスタイルを渡すとキャッシュが素通りして
        /// 角丸テクスチャが無限に生成されてしまう。
        /// </para>
        /// </summary>
        public static GUIStyle GetColoredStyle(GUIStyle baseStyle, Color bgColor)
        {
            string key = BuildStyleKey(baseStyle, bgColor);
            if (StyleCache.TryGetValue(key, out GUIStyle cached)) return cached;

            // GUIStyle.none から作りなおすことで内部の描画プロパティが悪影響しないようにする
            GUIStyle s = new GUIStyle
            {
                font          = baseStyle.font,
                fontSize      = baseStyle.fontSize,
                fontStyle     = baseStyle.fontStyle,
                alignment     = baseStyle.alignment,
                wordWrap      = baseStyle.wordWrap,
                richText      = baseStyle.richText,
                padding       = baseStyle.padding,
                margin        = baseStyle.margin,
                border        = new RectOffset(ButtonCorner, ButtonCorner, ButtonCorner, ButtonCorner),
                overflow      = baseStyle.overflow,
                contentOffset = baseStyle.contentOffset,
                fixedWidth    = baseStyle.fixedWidth,
                fixedHeight   = baseStyle.fixedHeight,
                stretchWidth  = baseStyle.stretchWidth,
                stretchHeight = baseStyle.stretchHeight
            };

            // Material の state layer に倣い、ホバー 8% / プレス 12% の白を重ねる。
            // 元実装は 20% の白と 15% の黒で、押した瞬間に色相が濁っていた。
            int size = ButtonCorner * 2 + 2;
            s.normal.background  = MakeRoundedTex(size, size, bgColor,                                  ButtonCorner);
            s.hover.background   = MakeRoundedTex(size, size, Color.Lerp(bgColor, Color.white, 0.08f), ButtonCorner);
            s.active.background  = MakeRoundedTex(size, size, Color.Lerp(bgColor, Color.white, 0.12f), ButtonCorner);
            s.focused.background = s.normal.background;

            s.onNormal.background  = s.normal.background;
            s.onHover.background   = s.hover.background;
            s.onActive.background  = s.active.background;
            s.onFocused.background = s.normal.background;

            // 常に白文字（色付きボタン前提）
            ForceColor(s, Color.white);

            StyleCache[key] = s;
            return s;
        }

        private static string BuildStyleKey(GUIStyle s, Color bgColor)
        {
            return string.Concat(
                s.font != null ? s.font.name : "-", "|",
                s.fontSize.ToString(), "|",
                ((int)s.fontStyle).ToString(), "|",
                ((int)s.alignment).ToString(), "|",
                s.wordWrap ? "1" : "0",
                s.richText ? "1" : "0",
                s.stretchWidth ? "1" : "0",
                s.stretchHeight ? "1" : "0", "|",
                s.fixedWidth.ToString("0.##"), "x", s.fixedHeight.ToString("0.##"), "|",
                s.padding.left.ToString(), ",", s.padding.right.ToString(), ",",
                s.padding.top.ToString(), ",", s.padding.bottom.ToString(), "|",
                s.margin.left.ToString(), ",", s.margin.right.ToString(), ",",
                s.margin.top.ToString(), ",", s.margin.bottom.ToString(), "|",
                ColorKey(bgColor));
        }

        private static string ColorKey(Color c)
            => $"{c.r:0.###},{c.g:0.###},{c.b:0.###},{c.a:0.###}";

        // ─── テクスチャ生成 ───────────────────────────────────────────

        /// <summary>単色テクスチャを生成する</summary>
        public static Texture2D MakeTex(int width, int height, Color col)
        {
            var tex = new Texture2D(width, height) { hideFlags = HideFlags.HideAndDontSave };
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            tex.SetPixels(pix);
            tex.Apply();
            GeneratedTextures.Add(tex);
            return tex;
        }

        /// <summary>
        /// 角丸テクスチャを生成する。
        /// 角の円外ピクセルを透明にすることでボタンの角丸を表現する。
        /// </summary>
        public static Texture2D MakeRoundedTex(int w, int h, Color fill, int r)
        {
            var tex   = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            var clear = new Color(0, 0, 0, 0);
            var pix   = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float dx = 0, dy = 0;
                bool corner = false;
                if      (x < r    && y < r)    { dx = px - r;     dy = py - r;     corner = true; }
                else if (x >= w-r && y < r)    { dx = px - (w-r); dy = py - r;     corner = true; }
                else if (x < r    && y >= h-r) { dx = px - r;     dy = py - (h-r); corner = true; }
                else if (x >= w-r && y >= h-r) { dx = px - (w-r); dy = py - (h-r); corner = true; }
                pix[y * w + x] = (corner && dx*dx + dy*dy > (float)r*r) ? clear : fill;
            }
            tex.SetPixels(pix);
            tex.Apply();
            GeneratedTextures.Add(tex);
            return tex;
        }

        /// <summary>動的生成したテクスチャとスタイルキャッシュをすべて破棄する</summary>
        private static void DisposeGenerated()
        {
            foreach (Texture2D tex in GeneratedTextures)
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
            GeneratedTextures.Clear();
            StyleCache.Clear();
            DerivedCache.Clear();
            _initialized = false;

            // TexSlimGUI のトグルテクスチャキャッシュも破棄
            TexSlimGUI.ClearToggleCache();
        }

        // ─── 内部ユーティリティ ──────────────────────────────────────

        /// <summary>全ステートのテキスト色を一括適用する</summary>
        private static GUIStyle ForceColor(GUIStyle s, Color col)
        {
            foreach (GUIStyleState state in new[]
            {
                s.normal, s.hover, s.active, s.focused,
                s.onNormal, s.onHover, s.onActive, s.onFocused
            })
            {
                state.textColor = col;
            }

            return s;
        }
    }
}
