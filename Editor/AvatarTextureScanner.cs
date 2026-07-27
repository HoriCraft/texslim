// ==============================================================================
// Product : TexSlim
// File    : AvatarTextureScanner.cs
// Role    : アバター配下のテクスチャ走査・保護判定・集計。
//           サイズ計算は TextureSizeUtil に集約し、スキャナ・UI・圧縮処理で共有する
//           （表示された推定値と実際に適用される値がズレないようにするため）。
// ==============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TexSlimComponent = global::HoriCraft.TexSlim.TexSlim;

namespace HoriCraft.TexSlim.Editor
{
    /// <summary>
    /// テクスチャのメモリ量推定を一箇所にまとめたユーティリティ。
    /// スキャナ・UI・圧縮処理が同じ計算を使うことで表示と実処理のズレを防ぐ。
    /// </summary>
    internal static class TextureSizeUtil
    {
        /// <summary>
        /// テクスチャの VRAM 使用量（バイト）を求める。
        /// 解像度・フォーマット・ミップの有無から計算する。
        /// Crunch 圧縮は GPU 上で展開されるため VRAM は減らない点に注意。
        /// </summary>
        public static long GetRuntimeBytes(Texture2D texture)
        {
            if (texture == null) return 0L;

            // Profiler.GetRuntimeMemorySizeLong は使わない。
            // エディタ上ではテクスチャの CPU 側コピーも数えてしまうため、
            // VRChat のアバター情報に出る「テクスチャメモリー」のおよそ 2 倍の値を返す。
            // （実測例: ツール 316.00 MB に対して VRChat 162.66 MB）
            // 解像度・フォーマット・ミップの有無から計算するほうが実機の値に近い。
            return EstimateVramBytes(
                texture.width, texture.height, texture.format.ToString(),
                texture.mipmapCount > 1);
        }

        // TextureUtil.GetStorageMemorySizeLong は internal クラスのため反射で呼ぶ。
        // 「インポート後にビルドへ載るバイト数」＝ Crunch 適用後の実サイズ。
        private static System.Reflection.MethodInfo _getStorageSize;
        private static bool _storageLookupDone;

        /// <summary>
        /// テクスチャの「ビルドに載るサイズ」（バイト）。取得できない環境では 0。
        /// <para>
        /// これは VRChat のアバター情報でいう<strong>「非圧縮サイズ」</strong>に対応する値で、
        /// 「ダウンロードサイズ」ではない。VRChat の DL サイズはアセットバンドルを
        /// さらに LZMA 圧縮した後の値で、Editor 上では算出できない（実測で 157MB 対 54MB と桁が違った）。
        /// UI のラベルを「ダウンロードサイズ」にしてはいけない。
        /// </para>
        /// Crunch 圧縮の効果はこの値に現れる（VRAM には現れない）。
        /// </summary>
        public static long GetStorageBytes(Texture texture)
        {
            if (texture == null) return 0L;

            if (!_storageLookupDone)
            {
                _storageLookupDone = true;
                System.Type util = typeof(AssetDatabase).Assembly.GetType("UnityEditor.TextureUtil");
                _getStorageSize =
                    util?.GetMethod("GetStorageMemorySizeLong",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                    ?? util?.GetMethod("GetStorageMemorySize",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            }

            if (_getStorageSize == null) return 0L;

            object result = _getStorageSize.Invoke(null, new object[] { texture });
            return result is long l ? l : result is int i ? i : 0L;
        }

        /// <summary>
        /// 解像度とフォーマット名から VRAM 使用量を推定する。
        /// ミップマップを持たないテクスチャに 4/3 を掛けると 33% 過大になるため、
        /// 有無を呼び出し側から渡せるようにしている。
        /// </summary>
        public static long EstimateVramBytes(int width, int height, string format, bool hasMipmaps = true)
        {
            if (width <= 0 || height <= 0) return 0L;
            // ミップマップ全体 = base の約 4/3 倍
            double mip = hasMipmaps ? (4.0 / 3.0) : 1.0;
            return (long)(width * (double)height * BppOf(format) * mip);
        }

        /// <summary>フォーマット名から 1 ピクセルあたりのバイト数を推定する</summary>
        public static double BppOf(string format)
        {
            string f = (format ?? string.Empty).ToUpperInvariant();

            if      (f.Contains("DXT1") || f.Contains("BC1"))      return 0.5;
            else if (f.Contains("BC4"))                            return 0.5;
            else if (f.Contains("DXT5") || f.Contains("BC3")
                  || f.Contains("BC5")  || f.Contains("BC7"))      return 1.0;
            else if (f.Contains("BC6H"))                           return 1.0;
            else if (f.Contains("ETC2") || f.Contains("ASTC")
                  || f.Contains("PVRTC"))                          return 1.0;
            else if (f.Contains("RGBAFLOAT"))                      return 16.0;
            else if (f.Contains("RGBAHALF"))                       return 8.0;
            else if (f.Contains("RGBA64") || f.Contains("ARGB64")) return 8.0;
            else if (f.Contains("RGB24") || f.Contains("BGR24"))   return 3.0;
            else if (f.Contains("R16"))                            return 2.0;
            else if (f.Contains("R8") || f.Contains("ALPHA8"))     return 1.0;
            else if (f.Contains("RGBA") || f.Contains("ARGB")
                  || f.Contains("BGRA"))                           return 4.0;
            else                                                   return 4.0; // 不明なフォーマットは保守側に倒す
        }

        /// <summary>
        /// Crunch（＝DXT ブロック圧縮）後の VRAM を推定する。
        /// <para>
        /// GPU 上のブロックフォーマットはアルファの有無で決まる（不透明=DXT1 0.5 / 透過=DXT5 1.0）。
        /// 一律 DXT5 とみなすと、元が DXT1 のテクスチャで推定が倍増してしまう。
        /// また Crunch はブロックサイズを増やさないので、元の bpp を上限にする。
        /// </para>
        /// </summary>
        public static long EstimateCrunchedVramBytes(
            int width, int height, string originalFormat, bool hasAlpha, bool hasMipmaps = true)
        {
            if (width <= 0 || height <= 0) return 0L;
            double blockBpp = hasAlpha ? 1.0 : 0.5;
            double bpp = System.Math.Min(blockBpp, BppOf(originalFormat));
            double mip = hasMipmaps ? (4.0 / 3.0) : 1.0;
            return (long)(width * (double)height * bpp * mip);
        }

        /// <summary>
        /// Unity の maxTextureSize を適用したあとの解像度を求める。
        /// 長辺が上限に収まるよう等比縮小される。
        /// </summary>
        public static void ApplyMaxSize(int width, int height, int maxSize, out int outWidth, out int outHeight)
        {
            outWidth  = width;
            outHeight = height;
            if (maxSize <= 0 || width <= 0 || height <= 0) return;
            if (width <= maxSize && height <= maxSize) return;

            float scale = maxSize / (float)Mathf.Max(width, height);
            outWidth  = Mathf.Max(1, Mathf.RoundToInt(width  * scale));
            outHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
        }

        /// <summary>バイト数を B / KB / MB の読みやすいラベルに変換する</summary>
        public static string BytesToLabel(long bytes)
        {
            if (bytes <= 0)            return "―";
            if (bytes < 1024L)         return $"{bytes} B";
            if (bytes < 1024L * 1024L) return $"{bytes / 1024.0:0.0} KB";
            return                            $"{bytes / (1024.0 * 1024.0):0.00} MB";
        }
    }

    internal static class AvatarTextureScanner
    {
        // ── 組み込みカテゴリキーワード ──────────────────────────────────
        //
        // 判定は部分一致なので、短い語ほど誤爆する。以下の方針で選んである。
        //
        //  ・"eyes" は "eye" が拾うので入れない（同様に "hairs" も "hair" が拾う）
        //  ・"head" は Headphone / Forehead まで巻き込むうえ、
        //    実際に守りたい Headband / Headdress は「髪飾り」なので髪カテゴリへ移した
        //  ・日本語の「ヘア」は国内アバターで頻出するが "hair" にも "髪" にも一致しない
        //  ・"目" "口" は 布目 / 縫い目 / 袖口 などに誤爆するため入れない（"mouth" で拾う）

        private static readonly string[] FaceEyeKeywordList = {
            "face", "eye", "iris", "mouth", "teeth", "tongue",
            "顔", "瞳", "まつげ", "まつ毛", "睫毛", "眉"
        };

        private static readonly string[] HairKeywordList = {
            "hair", "ヘア", "髪", "headband", "headdress"
        };

        /// <summary>
        /// 組み込みの打ち消し語。部分一致の宿命的な誤爆をここで吸収する。
        /// 例: "Chair_Fabric" は "hair" を含むため髪として保護されてしまう。
        /// </summary>
        private static readonly string[] BuiltInNegativeKeywords = {
            "chair"
        };

        /// <summary>UI に一覧を表示するため公開する（表示文字列を二重管理しないこと）</summary>
        public static IReadOnlyList<string> FaceEyeKeywords => FaceEyeKeywordList;
        public static IReadOnlyList<string> HairKeywords    => HairKeywordList;
        public static IReadOnlyList<string> NegativeKeywords => BuiltInNegativeKeywords;

        public static AvatarTextureScanResult Scan(TexSlimComponent component)
        {
            AvatarTextureScanResult result = new AvatarTextureScanResult(component);
            if (component == null)
            {
                return result;
            }

            // 同一アセットを何度も AssetDatabase / ディスクから読み直さないためのスキャン内キャッシュ。
            // 1テクスチャが複数マテリアルから参照されるのは普通なので効果が大きい。
            Dictionary<string, TextureAssetInfo> infoCache = new Dictionary<string, TextureAssetInfo>();

            Renderer[] renderers = component.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                Material[] sharedMaterials = renderer.sharedMaterials;
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                {
                    continue;
                }

                string objectPath = GetRelativePath(component.transform, renderer.transform);

                // Prefab インスタンス配下かどうかを検出（アバタールート自体は除外）
                string prefabName = string.Empty;
                GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
                if (prefabRoot != null && prefabRoot != component.gameObject)
                    prefabName = prefabRoot.name;

                AvatarObjectNode objectNode = new AvatarObjectNode(renderer, objectPath, prefabName);

                for (int slotIndex = 0; slotIndex < sharedMaterials.Length; slotIndex++)
                {
                    Material material = sharedMaterials[slotIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    // インポート設定方式ではマテリアルを差し替えないので、常に現在のマテリアルを読む。
                    AvatarMaterialNode materialNode = new AvatarMaterialNode(material, slotIndex);
                    foreach (AvatarTextureNode textureNode in ScanMaterialTextures(component, material, infoCache))
                    {
                        materialNode.Textures.Add(textureNode);
                    }

                    if (materialNode.Textures.Count > 0)
                    {
                        objectNode.Materials.Add(materialNode);
                    }
                }

                if (objectNode.Materials.Count > 0)
                {
                    result.Objects.Add(objectNode);
                }
            }

            // Include・保護判定・集計はまとめて後段で行う（設定変更時に再利用するため）
            result.RecomputeIncludes();
            return result;
        }

        public static bool MatchesSearch(AvatarObjectNode node, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            string lowered = query.Trim().ToLowerInvariant();
            if (Contains(node.ObjectPath, lowered) || Contains(node.Renderer.name, lowered))
            {
                return true;
            }

            return node.Materials.Any(material =>
                Contains(material.Material.name, lowered) ||
                material.Textures.Any(texture => Contains(texture.Texture.name, lowered) || Contains(texture.PropertyName, lowered)));
        }

        public static bool MatchesSearch(AvatarMaterialNode node, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            string lowered = query.Trim().ToLowerInvariant();
            return Contains(node.Material.name, lowered) ||
                   node.Textures.Any(texture => Contains(texture.Texture.name, lowered) || Contains(texture.PropertyName, lowered));
        }

        public static bool MatchesSearch(AvatarTextureNode node, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            string lowered = query.Trim().ToLowerInvariant();
            return Contains(node.Texture.name, lowered) || Contains(node.PropertyName, lowered);
        }

        /// <summary>
        /// ルートからの相対パスを返す。
        /// 同名の兄弟がいる場合はインデックスを付けて一意にする
        /// （VRChat アバターでは衣装 Prefab 配下に同名 Renderer が並ぶことが多く、
        ///  名前だけのパスでは除外設定が別オブジェクトに巻き添えで効いてしまう）。
        /// </summary>
        public static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || root == target)
            {
                return root != null ? root.name : string.Empty;
            }

            Stack<string> parts = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Push(GetUniqueSiblingName(current));
                current = current.parent;
            }

            if (current == root)
            {
                parts.Push(root.name);
            }

            return string.Join("/", parts.ToArray());
        }

        private static string GetUniqueSiblingName(Transform target)
        {
            Transform parent = target.parent;
            if (parent == null)
            {
                return target.name;
            }

            int sameNameCount = 0;
            int indexAmongSameName = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name != target.name)
                {
                    continue;
                }

                if (child == target)
                {
                    indexAmongSameName = sameNameCount;
                }

                sameNameCount++;
            }

            // 一意な名前のときは従来どおりの表記を保つ（既存の除外設定を壊さないため）
            return sameNameCount > 1 ? $"{target.name}[{indexAmongSameName}]" : target.name;
        }

        private static IEnumerable<AvatarTextureNode> ScanMaterialTextures(
            TexSlimComponent component,
            Material material,
            Dictionary<string, TextureAssetInfo> infoCache)
        {
            Shader shader = material.shader;
            if (shader == null)
            {
                yield break;
            }

            int count = ShaderUtil.GetPropertyCount(shader);
            HashSet<string> seen = new HashSet<string>();
            for (int index = 0; index < count; index++)
            {
                if (ShaderUtil.GetPropertyType(shader, index) != ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    continue;
                }

                string propertyName = ShaderUtil.GetPropertyName(shader, index);
                if (string.IsNullOrEmpty(propertyName) || !seen.Add(propertyName))
                {
                    continue;
                }

                Texture texture = material.GetTexture(propertyName);
                if (texture == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(texture);
                bool isProjectAsset = !string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/", StringComparison.Ordinal);
                bool isTexture2D = texture is Texture2D;

                TextureAssetInfo originalInfo = null;

                // 「ツールで圧縮したか」はプロジェクト全体の台帳（GUID）で判定する。
                // アセット単位なので、別シーンで圧縮したテクスチャもここで圧縮済みと分かる。
                bool compressedByTool = false;
                OriginalImportEntry originalEntry = null;

                if (isProjectAsset)
                {
                    originalInfo = GetTextureAssetInfoCached(assetPath, infoCache);
                    string guid = AssetDatabase.AssetPathToGUID(assetPath);
                    originalEntry = ImportSettingsRegistry.Get(guid);
                    compressedByTool = originalEntry != null;
                }

                yield return new AvatarTextureNode(
                    texture, propertyName, assetPath,
                    isProjectAsset, isTexture2D,
                    originalInfo, compressedByTool,
                    originalEntry != null ? originalEntry.maxTextureSize : 0);
            }
        }


        private static TextureAssetInfo GetTextureAssetInfoCached(
            string assetPath, Dictionary<string, TextureAssetInfo> cache)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            if (cache.TryGetValue(assetPath, out TextureAssetInfo cached)) return cached;

            TextureAssetInfo info = GetTextureAssetInfo(assetPath);
            cache[assetPath] = info;
            return info;
        }

        /// <summary>指定パスのテクスチャの解像度・フォーマット・サイズを取得する</summary>
        internal static TextureAssetInfo GetTextureAssetInfo(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;

            // TextureImporter から インポート設定を取得
            TextureImporter imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            int maxSize     = imp != null ? imp.maxTextureSize : 0;
            bool isCrunched = imp != null && imp.crunchedCompression;
            bool isNormal   = imp != null && imp.textureType == TextureImporterType.NormalMap;
            bool hasAlpha   = imp != null && DoesSourceHaveAlpha(imp);
            bool isUncomp   = imp != null && imp.textureCompression == TextureImporterCompression.Uncompressed;
            string format   = "Unknown";

            // 実テクスチャから解像度・フォーマット・VRAM量を取得
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            int width  = tex != null ? tex.width  : 0;
            int height = tex != null ? tex.height : 0;
            if (tex != null) format = tex.format.ToString();
            long runtimeBytes = TextureSizeUtil.GetRuntimeBytes(tex);
            long storageBytes = TextureSizeUtil.GetStorageBytes(tex);

            // ディスク上ファイルサイズ（元素材の目安。Crunch の効果はここには現れない）
            long fileBytes = 0;
            string fullPath = System.IO.Path.GetFullPath(assetPath);
            System.IO.FileInfo fileInfo = new System.IO.FileInfo(fullPath);
            if (fileInfo.Exists) fileBytes = fileInfo.Length;

            bool hasMips = tex != null && tex.mipmapCount > 1;
            return new TextureAssetInfo(width, height, maxSize, format, isCrunched, isNormal, hasAlpha, hasMips, isUncomp, fileBytes, runtimeBytes, storageBytes);
        }

        private static bool DoesSourceHaveAlpha(TextureImporter importer)
        {
            try { return importer.DoesSourceTextureHaveAlpha(); }
            catch (Exception) { return importer.alphaSource != TextureImporterAlphaSource.None; }
        }

        /// <summary>
        /// 保護キーワードに一致するか判定し、一致した場合は理由文字列を返す（不一致なら null）。
        /// </summary>
        internal static string GetProtectionReason(
            TexSlimComponent component,
            Renderer renderer,
            Material material,
            Texture texture,
            string propertyName)
        {
            if (component == null) return null;

            string haystack = string.Join(" ", new[]
            {
                renderer != null ? renderer.name : string.Empty,
                material != null ? material.name : string.Empty,
                texture != null ? texture.name : string.Empty,
                propertyName ?? string.Empty
            }).ToLowerInvariant();

            // 除外語が先。保護キーワードに一致していても、ここに当たれば保護しない。
            // 部分一致の誤爆を打ち消すのと、組み込みキーワードを無効化する手段を兼ねる。
            if (MatchesNegative(component, haystack)) return null;

            // 顔・瞳カテゴリ（PreserveFaceAndEyes が ON のとき）
            if (component.PreserveFaceAndEyes)
            {
                string hit = FaceEyeKeywordList.FirstOrDefault(k => haystack.Contains(k));
                if (hit != null) return L.F("顔・瞳: \"{0}\"", "face/eyes: \"{0}\"", hit);
            }

            // 髪カテゴリ（ProtectHair が ON のとき）
            if (component.ProtectHair)
            {
                string hit = HairKeywordList.FirstOrDefault(k => haystack.Contains(k));
                if (hit != null) return L.F("髪: \"{0}\"", "hair: \"{0}\"", hit);
            }

            // カスタムキーワード（設定タブで追加したもの）
            if (component.ProtectedKeywords != null)
            {
                string hit = component.ProtectedKeywords
                    .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                    .FirstOrDefault(keyword => haystack.Contains(keyword.Trim().ToLowerInvariant()));
                if (hit != null) return L.F("追加キーワード: \"{0}\"", "extra keyword: \"{0}\"", hit.Trim());
            }

            return null;
        }

        private static bool MatchesNegative(TexSlimComponent component, string haystack)
        {
            if (BuiltInNegativeKeywords.Any(k => haystack.Contains(k))) return true;

            return component.ExcludedKeywords != null
                && component.ExcludedKeywords
                    .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                    .Any(keyword => haystack.Contains(keyword.Trim().ToLowerInvariant()));
        }

        private static bool Contains(string text, string loweredQuery)
        {
            return !string.IsNullOrEmpty(text) && text.ToLowerInvariant().Contains(loweredQuery);
        }
    }

    internal sealed class AvatarTextureScanResult
    {
        public AvatarTextureScanResult(TexSlimComponent component)
        {
            Component = component;
        }

        public TexSlimComponent Component { get; private set; }
        public List<AvatarObjectNode> Objects { get; private set; } = new List<AvatarObjectNode>();
        public int  TextureCount          { get; private set; }
        public int  IncludedTextureCount  { get; private set; }
        public int  ProtectedTextureCount { get; private set; }
        public int  SkippedAssetCount     { get; private set; }
        /// <summary>ツールで圧縮済みのテクスチャ数（台帳に記録があるもの）</summary>
        public int  CompressedTextureCount { get; private set; }
        /// <summary>圧縮対象テクスチャの現在の VRAM 推定合計（バイト）</summary>
        public long IncludedVramBytes     { get; private set; }
        /// <summary>圧縮対象テクスチャを現在の設定で圧縮したときの VRAM 推定合計（バイト）</summary>
        public long EstimatedVramBytes    { get; private set; }
        /// <summary>圧縮対象テクスチャの「ビルドに載るサイズ」合計 ＝ ダウンロードサイズ（テクスチャ分）の現在値</summary>
        public long IncludedStorageBytes  { get; private set; }
        /// <summary>
        /// アバターが使う全テクスチャ（保護・除外も含む）の VRAM / ビルド上サイズ合計。
        /// ユーザーが「50MB 以下」等の目標と比べたいのはこちら（対象のみの合計だと
        /// 保護中の顔・髪テクスチャが抜け、実際より小さく見えてしまう）。
        /// </summary>
        public long TotalVramBytes        { get; private set; }
        public long TotalStorageBytes     { get; private set; }

        /// <summary>
        /// 圧縮形式が None（非圧縮）のまま残っていて、修正できるテクスチャの数。
        /// 保護・除外中のものは含まない（ユーザーが触るなと言ったものは触らない）。
        /// </summary>
        public int  UncompressedFixableCount  { get; private set; }
        /// <summary>非圧縮のうち、保護・除外などで修正対象から外れている数。</summary>
        public int  UncompressedSkippedCount  { get; private set; }
        /// <summary>非圧縮を Compressed に直したときに減る VRAM の推定合計。</summary>
        public long UncompressedFixSavings    { get; private set; }
        /// <summary>圧縮対象テクスチャの元ディスクサイズ合計（バイト）</summary>
        public long OriginalTotalBytes    { get; private set; }

        /// <summary>
        /// 設定変更時に、木構造を作り直さずに保護判定・Include・集計だけを再計算する。
        /// AssetDatabase / ディスクへは一切アクセスしないので軽い。
        /// </summary>
        public void RecomputeIncludes()
        {
            if (Component == null)
            {
                RefreshSummary();
                return;
            }

            // ── パス1: テクスチャごとに保護理由を確定する
            //
            // 保護判定は Renderer 名・マテリアル名も見るため、同じテクスチャでも
            // 使われている場所によって結果が変わる。一方 Include はテクスチャ単位なので、
            // ノードごとに保護を判定すると「保護と表示されているのに圧縮される」ことになる。
            // そこで「どこか1箇所でも一致したら、そのテクスチャは保護」に統一する。
            Dictionary<Texture, string> reasons = new Dictionary<Texture, string>();
            foreach (AvatarObjectNode objectNode in Objects)
            {
                foreach (AvatarMaterialNode materialNode in objectNode.Materials)
                {
                    foreach (AvatarTextureNode textureNode in materialNode.Textures)
                    {
                        if (textureNode.Texture == null) continue;
                        if (reasons.TryGetValue(textureNode.Texture, out string found) && found != null) continue;

                        string reason = AvatarTextureScanner.GetProtectionReason(
                            Component, objectNode.Renderer, materialNode.Material,
                            textureNode.Texture, textureNode.PropertyName);

                        // どこで一致したのかが分からないと調べようがないので出所を添える
                        if (reason != null && objectNode.Renderer != null && materialNode.Material != null)
                        {
                            reason += $"（{objectNode.Renderer.name} / {materialNode.Material.name}）";
                        }

                        reasons[textureNode.Texture] = reason;
                    }
                }
            }

            // ── パス2: Include を決める
            foreach (AvatarObjectNode objectNode in Objects)
            {
                bool objectIncluded = Component.GetObjectIncluded(objectNode.ObjectPath);
                foreach (AvatarMaterialNode materialNode in objectNode.Materials)
                {
                    bool materialIncluded = Component.GetMaterialIncluded(materialNode.Material);
                    foreach (AvatarTextureNode textureNode in materialNode.Textures)
                    {
                        string reason = textureNode.Texture != null
                                     && reasons.TryGetValue(textureNode.Texture, out string found)
                            ? found
                            : null;

                        textureNode.SetProtection(reason);
                        textureNode.SetInclude(
                            objectIncluded &&
                            materialIncluded &&
                            Component.GetTextureIncluded(textureNode.Texture) &&
                            textureNode.IsProjectAsset &&
                            textureNode.IsTexture2D &&
                            reason == null);
                    }
                }
            }

            RefreshSummary();
        }

        public void RefreshSummary()
        {
            TextureCount           = 0;
            IncludedTextureCount   = 0;
            ProtectedTextureCount  = 0;
            SkippedAssetCount      = 0;
            CompressedTextureCount = 0;
            OriginalTotalBytes     = 0L;
            IncludedVramBytes      = 0L;
            EstimatedVramBytes     = 0L;
            IncludedStorageBytes   = 0L;
            TotalVramBytes         = 0L;
            TotalStorageBytes      = 0L;
            UncompressedFixableCount = 0;
            UncompressedSkippedCount = 0;
            UncompressedFixSavings   = 0L;

            List<AvatarTextureNode> allNodes = Objects
                .SelectMany(o => o.Materials)
                .SelectMany(m => m.Textures)
                .ToList();

            foreach (AvatarTextureNode texture in allNodes)
            {
                texture.SetPrimaryUsage(false);
            }

            // テクスチャごとの代表ノードを「スキャン順で最初に現れたノード」に固定する。
            // UI の展開状態に依存しないので「🔗 共有」バッジの位置がぶれない。
            foreach (IGrouping<Texture, AvatarTextureNode> group in allNodes.GroupBy(t => t.Texture))
            {
                AvatarTextureNode primary = group.First();
                primary.SetPrimaryUsage(true);

                int usage = group.Count();
                foreach (AvatarTextureNode node in group) node.SetUsageCount(usage);

                TextureCount++;
                if (primary.CompressedByTool) CompressedTextureCount++;

                // アバター全体の合計（保護・除外も含む）。目標の MB と比較する数字はこちら。
                if (primary.OriginalInfo != null)
                {
                    TotalVramBytes    += primary.OriginalInfo.RuntimeBytes;
                    TotalStorageBytes += primary.OriginalInfo.StorageBytes;
                }

                // 保護数・対象外数もテクスチャ単位で数える。
                // ノード単位で数えると、1枚を3箇所で使っているだけで「保護 3」になり、
                // 「全体」（テクスチャ単位）と単位が揃わず内訳の引き算が破綻する。
                // 排他的に分類することで 全体 = 対象 + 保護 + 対象外 + 除外 が成り立つ。
                if (!primary.IsProjectAsset || !primary.IsTexture2D) SkippedAssetCount++;
                else if (primary.ProtectedByName)                    ProtectedTextureCount++;

                // 非圧縮フォーマットの集計。解像度をいくら下げても、形式が None のままだと
                // BC/DXT の約4倍の VRAM を使い続けるため、別枠で数えて診断に出す。
                bool anyIncluded = group.Any(node => node.Include);
                if (primary.IsProjectAsset && primary.IsTexture2D
                    && primary.OriginalInfo != null && primary.OriginalInfo.IsUncompressedFormat)
                {
                    if (anyIncluded)
                    {
                        UncompressedFixableCount++;
                        // 同じ解像度のままブロック圧縮（DXT1/DXT5）へ変えた場合の差分。
                        long fixedBytes = TextureSizeUtil.EstimateCrunchedVramBytes(
                            primary.OriginalInfo.Width, primary.OriginalInfo.Height,
                            primary.OriginalInfo.Format, primary.OriginalInfo.HasAlpha,
                            primary.OriginalInfo.HasMipmaps);
                        UncompressedFixSavings +=
                            System.Math.Max(0L, primary.OriginalInfo.RuntimeBytes - fixedBytes);
                    }
                    else
                    {
                        UncompressedSkippedCount++;
                    }
                }

                // 圧縮処理は Where(Include).GroupBy(Texture) なので、
                // 1箇所でも Include ならそのテクスチャは圧縮される。集計もそれに合わせる
                //（代表ノードだけを見ると、親が除外された行が代表になったときに数が食い違う）。
                if (!anyIncluded) continue;

                IncludedTextureCount++;
                if (primary.OriginalInfo == null) continue;

                OriginalTotalBytes   += primary.OriginalInfo.FileBytes;
                IncludedVramBytes    += primary.OriginalInfo.RuntimeBytes;
                EstimatedVramBytes   += EstimateCompressedVram(primary);
                IncludedStorageBytes += primary.OriginalInfo.StorageBytes;
            }
        }

        /// <summary>
        /// 修正対象になる非圧縮テクスチャの代表ノードを集める。
        /// 判定基準は RefreshSummary の集計（UncompressedFixableCount）と同じにすること。
        /// </summary>
        public List<AvatarTextureNode> CollectUncompressedFixable()
        {
            List<AvatarTextureNode> result = new List<AvatarTextureNode>();
            IEnumerable<AvatarTextureNode> allNodes = Objects
                .SelectMany(o => o.Materials)
                .SelectMany(m => m.Textures);

            foreach (IGrouping<Texture, AvatarTextureNode> group in allNodes.GroupBy(t => t.Texture))
            {
                AvatarTextureNode primary = group.First();
                if (primary.IsProjectAsset && primary.IsTexture2D
                    && primary.OriginalInfo != null && primary.OriginalInfo.IsUncompressedFormat
                    && group.Any(node => node.Include))
                {
                    result.Add(primary);
                }
            }
            return result;
        }

        /// <summary>
        /// 現在の設定でこのテクスチャを圧縮したときの VRAM 推定値。
        /// Crunch は GPU 上で展開されるため VRAM を減らさない。減らせるのは解像度だけ。
        /// 詳細タブの行表示（圧縮後の MB 併記）からも使う。
        /// </summary>
        internal long EstimateCompressedVram(AvatarTextureNode node)
        {
            TextureAssetInfo info = node.OriginalInfo;
            if (info == null || Component == null) return 0L;

            bool reduceResolution =
                Component.Mode != global::HoriCraft.TexSlim.TexSlim.CompressionMode.CrunchOnly;

            int targetW = info.Width;
            int targetH = info.Height;
            if (reduceResolution)
            {
                TextureSizeUtil.ApplyMaxSize(
                    info.Width, info.Height,
                    Component.GetEffectiveMaxSize(node.Texture),
                    out targetW, out targetH);
            }

            // Crunch を掛けるなら DXT ブロック圧縮になる。
            // アルファ有無で DXT1/DXT5 が決まり、かつ元の bpp を超えない
            //（一律 DXT5 で見積もると DXT1 素材で推定が倍増する）。
            bool applyCrunch =
                Component.Mode != global::HoriCraft.TexSlim.TexSlim.CompressionMode.ResolutionOnly
                && !info.IsNormalMap;

            return applyCrunch
                ? TextureSizeUtil.EstimateCrunchedVramBytes(targetW, targetH, info.Format, info.HasAlpha, info.HasMipmaps)
                : TextureSizeUtil.EstimateVramBytes(targetW, targetH, info.Format, info.HasMipmaps);
        }
    }

    internal sealed class AvatarObjectNode
    {
        public AvatarObjectNode(Renderer renderer, string objectPath, string prefabName = "")
        {
            Renderer   = renderer;
            ObjectPath = objectPath;
            PrefabName = prefabName ?? string.Empty;
        }

        public Renderer Renderer   { get; private set; }
        public string   ObjectPath { get; private set; }
        /// <summary>Rendererが属する最小 Prefab インスタンスの名前。非 Prefab の場合は空文字列。</summary>
        public string   PrefabName { get; private set; }
        public List<AvatarMaterialNode> Materials { get; private set; } = new List<AvatarMaterialNode>();
    }

    internal sealed class AvatarMaterialNode
    {
        public AvatarMaterialNode(Material material, int slotIndex)
        {
            Material = material;
            SlotIndex = slotIndex;
        }

        public Material Material { get; private set; }
        public int SlotIndex { get; private set; }
        public List<AvatarTextureNode> Textures { get; private set; } = new List<AvatarTextureNode>();
    }

    internal sealed class AvatarTextureNode
    {
        public AvatarTextureNode(
            Texture texture,
            string propertyName,
            string assetPath,
            bool isProjectAsset,
            bool isTexture2D,
            TextureAssetInfo originalInfo = null,
            bool compressedByTool = false,
            int originalMaxSize = 0)
        {
            Texture         = texture;
            PropertyName    = propertyName;
            AssetPath       = assetPath;
            IsProjectAsset  = isProjectAsset;
            IsTexture2D     = isTexture2D;
            OriginalInfo    = originalInfo;
            CompressedByTool = compressedByTool;
            OriginalMaxSize = originalMaxSize;
        }

        public Texture          Texture         { get; private set; }
        public string           PropertyName    { get; private set; }
        public string           AssetPath       { get; private set; }
        public bool             Include         { get; private set; }
        /// <summary>保護キーワードに一致した理由。一致しなければ null。</summary>
        public string           ProtectionReason { get; private set; }
        public bool             ProtectedByName => ProtectionReason != null;
        public bool             IsProjectAsset  { get; private set; }
        public bool             IsTexture2D     { get; private set; }
        /// <summary>同じテクスチャを参照する複数ノードのうち、代表として扱うノードなら true</summary>
        public bool             IsPrimaryUsage  { get; private set; }
        /// <summary>このテクスチャがアバター内で参照されている箇所の数</summary>
        public int              UsageCount      { get; private set; } = 1;
        /// <summary>現在の（＝スキャン時点の）テクスチャ情報</summary>
        public TextureAssetInfo OriginalInfo    { get; private set; }
        /// <summary>ツールで圧縮済みか（台帳に圧縮前設定が控えられているか）</summary>
        public bool             CompressedByTool { get; private set; }
        /// <summary>圧縮前の maxTextureSize（台帳の値。未圧縮なら 0）</summary>
        public int              OriginalMaxSize { get; private set; }

        internal void SetInclude(bool value)        { Include = value; }
        internal void SetProtection(string reason)  { ProtectionReason = reason; }
        internal void SetPrimaryUsage(bool value)   { IsPrimaryUsage = value; }
        internal void SetUsageCount(int value)      { UsageCount = value; }
    }

    /// <summary>テクスチャアセットの静的情報を保持するデータクラス</summary>
    internal sealed class TextureAssetInfo
    {
        public TextureAssetInfo(
            int width, int height, int maxSize, string format,
            bool isCrunched, bool isNormalMap, bool hasAlpha, bool hasMipmaps,
            bool isUncompressedFormat,
            long fileBytes, long runtimeBytes, long storageBytes)
        {
            Width        = width;
            Height       = height;
            MaxSize      = maxSize;
            Format       = format;
            IsCrunched   = isCrunched;
            IsNormalMap  = isNormalMap;
            HasAlpha     = hasAlpha;
            HasMipmaps   = hasMipmaps;
            IsUncompressedFormat = isUncompressedFormat;
            FileBytes    = fileBytes;
            RuntimeBytes = runtimeBytes;
            StorageBytes = storageBytes;
        }

        public int    Width        { get; }
        public int    Height       { get; }
        public int    MaxSize      { get; }
        public string Format       { get; }
        public bool   IsCrunched   { get; }
        /// <summary>NormalMap としてインポートされているか（Crunch を避ける判断に使う）</summary>
        public bool   IsNormalMap  { get; }
        /// <summary>アルファを持つか（Crunch 後の DXT1/DXT5 を判定するのに使う）</summary>
        public bool   HasAlpha     { get; }
        /// <summary>ミップマップを持つか。VRAM 推定で 4/3 を掛けるかの判定に使う。</summary>
        public bool   HasMipmaps   { get; }
        /// <summary>
        /// インポート設定の圧縮形式が None（非圧縮）か。
        /// RGBA32 のままだと BC/DXT の約4倍の VRAM を使うため、診断で警告する。
        /// </summary>
        public bool   IsUncompressedFormat { get; }
        /// <summary>ディスク上のソースファイルサイズ。インポート設定を変えても変化しない点に注意。</summary>
        public long   FileBytes    { get; }
        /// <summary>実行時（VRAM）の使用量。インポート設定の効果はこちらに現れる。</summary>
        public long   RuntimeBytes { get; }
        /// <summary>ビルドに載るサイズ（ダウンロードサイズへの寄与）。Crunch の効果はこちらに現れる。</summary>
        public long   StorageBytes { get; }

        public string FileSizeLabel    => TextureSizeUtil.BytesToLabel(FileBytes);
        public string RuntimeSizeLabel => TextureSizeUtil.BytesToLabel(RuntimeBytes);
    }
}
