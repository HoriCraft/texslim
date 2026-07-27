// ==============================================================================
// Product : TexSlim
// File    : TexSlim.cs
// Role    : コンポーネント本体。設定データとマイグレーションを持つ。
//           圧縮前のインポート設定は、ここではなく ImportSettingsRegistry
//           （ProjectSettings/TexSlim.json）に記録する。アセット単位の情報なので、
//           シーン内のコンポーネントに置くと別シーンから辿れなくなるため。
// ==============================================================================


using System;
using System.Collections.Generic;
using UnityEngine;

namespace HoriCraft.TexSlim
{
    [DisallowMultipleComponent]
    [AddComponentMenu("HoriCraft/TexSlim")]
    // IEditorOnly を実装したコンポーネントは VRChat SDK のビルド時に自動除去される。
    // これがないとアップロード後のアバターに本コンポーネントが残る。
    // IEditorOnly は VRCSDKBase.dll（プリコンパイル済みプラグイン）にあり、
    // asmdef の overrideReferences が false なので自動的に参照される。
    // TEXSLIM_HAS_VRCSDK は asmdef の versionDefines（com.vrchat.base）から供給される。
    public sealed class TexSlim : MonoBehaviour
#if TEXSLIM_HAS_VRCSDK
        , VRC.SDKBase.IEditorOnly
#endif
    {
        public const string ToolVersion = "1.1.0";

        /// <summary>設定データのスキーマ版。旧データのマイグレーション判定に使う。</summary>
        public const int CurrentSettingsVersion = 1;

        /// <summary>
        /// v0 の protectedKeywords 初期値。組み込みの顔・瞳キーワードと重複しており、
        /// PreserveFaceAndEyes を OFF にしても保護が解除されない不具合の原因になっていた。
        /// マイグレーション時にこれらと完全一致するエントリのみ除去する。
        /// </summary>
        private static readonly string[] LegacyDefaultKeywords =
        {
            "Face", "Eye", "Eyes", "Iris", "Head", "Mouth", "顔", "瞳"
        };

        /// <summary>圧縮モードの定義</summary>
        public enum CompressionMode
        {
            // 数値は変えないこと。既存シーンに保存済みの値と対応している。
            /// <summary>解像度ダウン + Crunch圧縮の両方を適用</summary>
            Both            = 0,
            /// <summary>解像度はそのまま、Crunch圧縮形式だけ変更</summary>
            CrunchOnly      = 1,
            /// <summary>解像度を下げるが、圧縮形式は変更しない（デフォルト）</summary>
            ResolutionOnly  = 2,
        }

        // 初期値は 1024。2048 だと元から 2048 以下のテクスチャが1枚も縮まず、
        // 「押しても大して変わらない」という第一印象になりやすい。
        // 1024 はアバター軽量化で常用される値で、実測でも
        // ダウンロードサイズ -38% / テクスチャメモリ -50% と体感できる差が出た。
        [SerializeField] private int maxTextureSize = 1024;
        [SerializeField] private bool preserveFaceAndEyes = true;
        [SerializeField] private bool protectHair          = true;   // 髪カテゴリ保護
        [SerializeField] private int activeTab;
        [SerializeField] private bool isCompressed;
        [SerializeField] private string lastCompressionUtc;
        // 初期値は ResolutionOnly。
        // 実測（作者のアバター / 最大サイズ 1024）では、解像度を下げたうえで
        // さらに Crunch をかけても、ダウンロードサイズは 33.42MB → 32.12MB
        // （1.30MB・3.9%）しか変わらず、テクスチャメモリは 80.66MB で完全に同じだった。
        // 1024 まで落とすと元データが小さく、アセットバンドルの圧縮が
        // ほとんど取り切ってしまうため、Crunch の出番が残らない。
        // その 1.3MB のために全テクスチャへ二重の非可逆圧縮と
        // 読み込み時の CPU 展開を課すのは割に合わない。
        // なお既存シーンには値が保存済みなので、この変更が効くのは新規追加分だけ。
        [SerializeField] private CompressionMode compressionMode   = CompressionMode.ResolutionOnly;
        [SerializeField] private int  compressionQuality           = 75;
        // 旧データは 0（フィールド未保存）のままデシリアライズされるため、
        // Reset() を通った新規コンポーネントとマイグレーション対象を区別できる。
        [SerializeField] private int settingsVersion;

        // ProtectedKeywords はユーザー追加のカスタムキーワード専用。
        // 顔・瞳・髪の組み込みキーワードは PreserveFaceAndEyes / ProtectHair フラグで管理するため、
        // ここに組み込みと同じ語を入れてはいけない（フラグを OFF にしても効かなくなる）。
        [SerializeField] private List<string> protectedKeywords = new List<string>();

        // 保護の例外（negative keyword）。保護キーワードに一致していても、
        // ここに挙げた語を含むものは保護しない。
        // 部分一致の誤爆（例: "hair" が "Chair" に一致する）を打ち消すための逃げ道であり、
        // 組み込みキーワードを個別に無効化する手段も兼ねる。
        [SerializeField] private List<string> excludedKeywords = new List<string>();

        [SerializeField] private List<ObjectCompressionOverride> objectOverrides = new List<ObjectCompressionOverride>();
        [SerializeField] private List<MaterialCompressionOverride> materialOverrides = new List<MaterialCompressionOverride>();
        [SerializeField] private List<TextureCompressionOverride> textureOverrides = new List<TextureCompressionOverride>();

        /// <summary>
        /// グローバル最大テクスチャサイズ。
        /// UI のドロップダウンが 256〜4096 しか出さないため、範囲もそこへ揃える
        /// （範囲外の値が入るとドロップダウンの表示が空欄になってしまう）。
        /// </summary>
        public int MaxTextureSize
        {
            get { return maxTextureSize; }
            set { maxTextureSize = Mathf.Clamp(value, 256, 4096); }
        }

        /// <summary>顔・瞳カテゴリの保護を有効にする</summary>
        public bool PreserveFaceAndEyes
        {
            get { return preserveFaceAndEyes; }
            set { preserveFaceAndEyes = value; }
        }

        /// <summary>髪カテゴリの保護を有効にする</summary>
        public bool ProtectHair
        {
            get { return protectHair; }
            set { protectHair = value; }
        }

        public int ActiveTab
        {
            get { return activeTab; }
            set { activeTab = Mathf.Clamp(value, 0, 2); }
        }

        public CompressionMode Mode
        {
            get { return compressionMode; }
            set { compressionMode = value; }
        }

        /// <summary>Crunch圧縮の品質（1〜100、推奨：50〜80。デフォルト 75）</summary>
        public int CompressionQuality
        {
            get { return compressionQuality; }
            set { compressionQuality = Mathf.Clamp(value, 1, 100); }
        }

        public bool IsCompressed
        {
            get { return isCompressed; }
            set { isCompressed = value; }
        }

        public string LastCompressionUtc
        {
            get { return lastCompressionUtc; }
            set { lastCompressionUtc = value ?? string.Empty; }
        }

        public List<string> ProtectedKeywords
        {
            get { return protectedKeywords; }
        }

        /// <summary>保護の例外。保護キーワードに一致していても、ここに該当すれば保護しない。</summary>
        public List<string> ExcludedKeywords
        {
            get { return excludedKeywords; }
        }

        public List<ObjectCompressionOverride> ObjectOverrides
        {
            get { return objectOverrides; }
        }

        public List<MaterialCompressionOverride> MaterialOverrides
        {
            get { return materialOverrides; }
        }

        public List<TextureCompressionOverride> TextureOverrides
        {
            get { return textureOverrides; }
        }

        public bool GetObjectIncluded(string objectPath)
        {
            ObjectCompressionOverride entry = objectOverrides.Find(item => item.ObjectPath == objectPath);
            return entry == null || entry.Include;
        }

        public void SetObjectIncluded(string objectPath, bool include)
        {
            if (string.IsNullOrEmpty(objectPath))
            {
                return;
            }

            ObjectCompressionOverride entry = objectOverrides.Find(item => item.ObjectPath == objectPath);
            if (entry == null)
            {
                objectOverrides.Add(new ObjectCompressionOverride(objectPath, include));
            }
            else
            {
                entry.Include = include;
            }
        }

        public bool GetMaterialIncluded(Material material)
        {
            if (material == null)
            {
                return false;
            }

            MaterialCompressionOverride entry = materialOverrides.Find(item => item.Material == material);
            return entry == null || entry.Include;
        }

        public void SetMaterialIncluded(Material material, bool include)
        {
            if (material == null)
            {
                return;
            }

            MaterialCompressionOverride entry = materialOverrides.Find(item => item.Material == material);
            if (entry == null)
            {
                materialOverrides.Add(new MaterialCompressionOverride(material, include));
            }
            else
            {
                entry.Include = include;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // テクスチャ単位の設定
        //
        // 圧縮処理は「テクスチャアセット1枚」を単位として行われる（同じテクスチャが
        // 複数のシェーダープロパティで使われていても複製されるのは1枚）。
        // したがって Include / サイズ上書きのキーもテクスチャ参照のみとし、
        // プロパティ名は含めない。旧データのプロパティ名別エントリは
        // MigrateSettings() で1件に統合される。
        // ─────────────────────────────────────────────────────────────

        public bool GetTextureIncluded(Texture texture)
        {
            if (texture == null)
            {
                return false;
            }

            TextureCompressionOverride entry = textureOverrides.Find(item => item.Texture == texture);
            return entry == null || entry.Include;
        }

        public void SetTextureIncluded(Texture texture, bool include)
        {
            if (texture == null)
            {
                return;
            }

            GetOrCreateTextureOverride(texture).Include = include;
        }

        /// <summary>テクスチャ個別の最大サイズ上書きを取得（-1 = グローバル設定を使用）</summary>
        public int GetTextureMaxSizeOverride(Texture texture)
        {
            if (texture == null) return -1;
            TextureCompressionOverride entry = textureOverrides.Find(item => item.Texture == texture);
            return entry != null ? entry.MaxTextureSizeOverride : -1;
        }

        /// <summary>テクスチャ個別の最大サイズ上書きを設定（-1 でグローバル設定に戻す）</summary>
        public void SetTextureMaxSizeOverride(Texture texture, int size)
        {
            if (texture == null) return;
            GetOrCreateTextureOverride(texture).MaxTextureSizeOverride = size;
        }

        /// <summary>
        /// グローバル設定と個別上書きから実効的な最大サイズを求める。
        /// 個別設定はグローバルより大きくてもよい（「全体は 512、これだけ 2048」ができる）。
        /// 選んだ値がそのまま使われるほうが直感的なため、上限で丸めない。
        /// なお元の解像度を超えて拡大されることは、Import 時に元サイズで頭打ちになるため起きない。
        /// </summary>
        public int GetEffectiveMaxSize(Texture texture)
        {
            int over = GetTextureMaxSizeOverride(texture);
            return over > 0 ? over : MaxTextureSize;
        }

        private TextureCompressionOverride GetOrCreateTextureOverride(Texture texture)
        {
            TextureCompressionOverride entry = textureOverrides.Find(item => item.Texture == texture);
            if (entry == null)
            {
                entry = new TextureCompressionOverride(texture, true);
                textureOverrides.Add(entry);
            }

            return entry;
        }

        /// <summary>
        /// 「圧縮済み」表示をリセットする。
        /// 圧縮前の Import 設定そのものは ProjectSettings 側の台帳が持っているため、
        /// ここで消すのはコンポーネントの状態表示だけ。
        /// </summary>
        public void ClearCompressionState()
        {
            isCompressed = false;
            lastCompressionUtc = string.Empty;
        }

        /// <summary>
        /// 旧バージョンで保存された設定を現行スキーマへ移行する。
        /// Editor の OnEnable から一度だけ呼ばれる。変更があった場合は true を返す。
        /// </summary>
        public bool MigrateSettings()
        {
            if (settingsVersion >= CurrentSettingsVersion)
            {
                return false;
            }

            bool changed = false;

            // v0 → v1 ① 組み込みと重複する初期キーワードを除去する。
            //            これがあると PreserveFaceAndEyes を OFF にしても保護が外れない。
            if (protectedKeywords != null)
            {
                int removed = protectedKeywords.RemoveAll(keyword =>
                    Array.Exists(LegacyDefaultKeywords,
                        legacy => string.Equals(legacy, (keyword ?? string.Empty).Trim(),
                                                StringComparison.OrdinalIgnoreCase)));
                changed |= removed > 0;
            }

            // v0 → v1 ② テクスチャ上書きをプロパティ名別からテクスチャ単位へ統合する。
            //            Include は「1つでも除外なら除外」、サイズは有効値の最小を採る。
            if (textureOverrides != null && textureOverrides.Count > 0)
            {
                List<TextureCompressionOverride> merged = new List<TextureCompressionOverride>();
                foreach (TextureCompressionOverride entry in textureOverrides)
                {
                    if (entry == null || entry.Texture == null)
                    {
                        changed = true;
                        continue;
                    }

                    TextureCompressionOverride existing = merged.Find(item => item.Texture == entry.Texture);
                    if (existing == null)
                    {
                        merged.Add(entry);
                        continue;
                    }

                    existing.Include &= entry.Include;
                    if (entry.MaxTextureSizeOverride > 0)
                    {
                        existing.MaxTextureSizeOverride = existing.MaxTextureSizeOverride > 0
                            ? Mathf.Min(existing.MaxTextureSizeOverride, entry.MaxTextureSizeOverride)
                            : entry.MaxTextureSizeOverride;
                    }

                    changed = true;
                }

                if (changed)
                {
                    textureOverrides = merged;
                }
            }

            settingsVersion = CurrentSettingsVersion;
            // 版の更新自体も保存が必要（changed だけ返すと、キーワード等に変更のない
            // 旧データでは SetDirty されず、開くたびにマイグレーションが再実行される）
            return true;
        }

        private void Reset()
        {
            // 新規追加されたコンポーネントはマイグレーション不要。
            settingsVersion = CurrentSettingsVersion;
        }

        private void OnValidate()
        {
            MaxTextureSize = maxTextureSize;
            ActiveTab = activeTab;

            if (protectedKeywords == null)
            {
                protectedKeywords = new List<string>();
            }

            if (excludedKeywords == null)
            {
                excludedKeywords = new List<string>();
            }

            if (objectOverrides == null)
            {
                objectOverrides = new List<ObjectCompressionOverride>();
            }

            if (materialOverrides == null)
            {
                materialOverrides = new List<MaterialCompressionOverride>();
            }

            if (textureOverrides == null)
            {
                textureOverrides = new List<TextureCompressionOverride>();
            }
        }
    }

    [Serializable]
    public sealed class ObjectCompressionOverride
    {
        [SerializeField] private string objectPath;
        [SerializeField] private bool include = true;

        public ObjectCompressionOverride(string objectPath, bool include)
        {
            this.objectPath = objectPath;
            this.include = include;
        }

        public string ObjectPath
        {
            get { return objectPath; }
        }

        public bool Include
        {
            get { return include; }
            set { include = value; }
        }
    }

    [Serializable]
    public sealed class MaterialCompressionOverride
    {
        [SerializeField] private Material material;
        [SerializeField] private bool include = true;

        public MaterialCompressionOverride(Material material, bool include)
        {
            this.material = material;
            this.include = include;
        }

        public Material Material
        {
            get { return material; }
        }

        public bool Include
        {
            get { return include; }
            set { include = value; }
        }
    }

    [Serializable]
    public sealed class TextureCompressionOverride
    {
        [SerializeField] private Texture texture;
        /// <summary>v0 の残存フィールド。現在はキーとして使用しない（YAML 互換のため残置）。</summary>
        [SerializeField] private string propertyName;
        [SerializeField] private bool include = true;
        /// <summary>-1 = グローバル設定を使用（デフォルト）。正の値はそのサイズで上書き。</summary>
        [SerializeField] private int maxTextureSizeOverride = -1;

        public TextureCompressionOverride(Texture texture, bool include)
        {
            this.texture = texture;
            this.propertyName = string.Empty;
            this.include = include;
        }

        public Texture Texture
        {
            get { return texture; }
        }

        public bool Include
        {
            get { return include; }
            set { include = value; }
        }

        /// <summary>-1 = グローバル設定を使用。256/512/1024/2048/4096 など正の値で上書き。</summary>
        public int MaxTextureSizeOverride
        {
            get { return maxTextureSizeOverride; }
            set { maxTextureSizeOverride = value; }
        }
    }

}
