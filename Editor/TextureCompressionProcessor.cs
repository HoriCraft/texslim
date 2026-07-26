// ==============================================================================
// Product : TexSlim
// File    : TextureCompressionProcessor.cs
// Role    : 圧縮・復元の本体。
//
//           圧縮方式は「テクスチャの インポート設定を直接変更する方式」。
//           _Compressed コピーもマテリアル複製も作らない。テクスチャアセットそのものが
//           小さくなり、それを使う全マテリアルが自動的に圧縮版を使う。
//
//           元の画像データ（.png 本体）は無変更で、変わるのは .meta の インポート設定だけ。
//           圧縮前の設定は ImportSettingsRegistry（プロジェクト全体の台帳）に控えるので、
//           どのシーン・どのアバターからでも真の原本へ戻せる。
// ==============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TexSlimComponent = global::HoriCraft.TexSlim.TexSlim;

namespace HoriCraft.TexSlim.Editor
{
    internal static class TextureCompressionProcessor
    {
        // ─── 圧縮 ────────────────────────────────────────────────────

        /// <summary>対象になっている全テクスチャを圧縮する（一括）。</summary>
        public static CompressionResult Compress(TexSlimComponent component)
        {
            if (component == null) return CompressionResult.Empty;

            AvatarTextureScanResult scan = AvatarTextureScanner.Scan(component);
            List<AvatarTextureNode> targets = scan.Objects
                .SelectMany(o => o.Materials)
                .SelectMany(m => m.Textures)
                .Where(t => t.Include)
                .GroupBy(t => t.Texture)
                .Select(g => g.First())
                .ToList();

            List<string> warnings = new List<string>();
            long beforeVram = 0, afterVram = 0;
            long beforeStorage = 0, afterStorage = 0;
            int done = 0;
            bool canceled = false;
            bool crunchApplied = component.Mode != TexSlimComponent.CompressionMode.ResolutionOnly;

            // チャンク単位で StartAssetEditing / StopAssetEditing に挟む。
            //  - 1枚ずつ SaveAndReimport すると毎回インポータが回り、枚数が多いと非常に遅い
            //  - 全体を1バッチにすると Stop の瞬間に全インポートが走り、キャンセルが実質効かない
            // 10枚ごとに区切ることで、速度（リフレッシュ回数 1/10）と
            // キャンセル地点（チャンク境界）を両立させる。
            const int ChunkSize = 10;

            try
            {
                for (int start = 0; start < targets.Count && !canceled; start += ChunkSize)
                {
                    int end = Math.Min(start + ChunkSize, targets.Count);
                    List<AvatarTextureNode> applied = new List<AvatarTextureNode>();

                    try
                    {
                        AssetDatabase.StartAssetEditing();
                        for (int i = start; i < end; i++)
                        {
                            AvatarTextureNode tex = targets[i];
                            if (DisplayCancelableProgress(
                                    L.T("テクスチャを圧縮中", "Compressing texture"),
                                    tex.Texture.name, i, targets.Count))
                            {
                                canceled = true;
                                break;
                            }

                            beforeVram    += tex.OriginalInfo?.RuntimeBytes ?? 0L;
                            beforeStorage += tex.OriginalInfo?.StorageBytes ?? 0L;
                            if (CompressTextureInPlace(component, tex.AssetPath, tex.Texture, out string warning))
                            {
                                done++;
                                applied.Add(tex);
                            }
                            else
                            {
                                afterVram    += tex.OriginalInfo?.RuntimeBytes ?? 0L;
                                afterStorage += tex.OriginalInfo?.StorageBytes ?? 0L;
                            }
                            if (warning != null) warnings.Add(warning);
                        }
                    }
                    finally
                    {
                        // ここでチャンク分のインポートがまとめて走る
                        AssetDatabase.StopAssetEditing();
                    }

                    // インポート確定後でないと実測値が更新されないため、計測は Stop の後
                    foreach (AvatarTextureNode tex in applied)
                    {
                        Reload(tex.AssetPath, out long vram, out long storage);
                        afterVram    += vram;
                        afterStorage += storage;
                    }
                }

                if (canceled)
                {
                    warnings.Add(L.F(
                        "中断されました（{0} / {1} 枚まで適用済み。適用分は [↩ 元に戻す] で戻せます）",
                        "Canceled ({0} / {1} textures were applied; use [↩ Restore] to undo them)",
                        done, targets.Count));
                }

                FinalizeCompression(component, done > 0);
                return new CompressionResult(
                    done, beforeVram, afterVram, beforeStorage, afterStorage, crunchApplied, warnings);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>テクスチャ1枚だけを圧縮する。</summary>
        public static CompressionResult CompressSingle(TexSlimComponent component, AvatarTextureNode texNode)
        {
            if (component == null || texNode?.Texture == null) return CompressionResult.Empty;

            List<string> warnings = new List<string>();
            bool crunchApplied = component.Mode != TexSlimComponent.CompressionMode.ResolutionOnly;
            long beforeVram    = texNode.OriginalInfo?.RuntimeBytes ?? 0L;
            long beforeStorage = texNode.OriginalInfo?.StorageBytes ?? 0L;

            try
            {
                DisplayProgress(L.T("テクスチャを圧縮中", "Compressing texture"), texNode.Texture.name, 0, 1);
                bool ok = CompressTextureInPlace(component, texNode.AssetPath, texNode.Texture, out string warning);
                if (warning != null) warnings.Add(warning);

                long afterVram = beforeVram, afterStorage = beforeStorage;
                if (ok) Reload(texNode.AssetPath, out afterVram, out afterStorage);

                FinalizeCompression(component, ok);
                return new CompressionResult(
                    ok ? 1 : 0, beforeVram, afterVram, beforeStorage, afterStorage, crunchApplied, warnings);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 1テクスチャの インポート設定を圧縮向きに書き換える。
        /// 圧縮前の設定は台帳に控える（初回のみ・以後は上書きしない）。
        /// </summary>
        private static bool CompressTextureInPlace(
            TexSlimComponent component, string assetPath, Texture texture, out string warning)
        {
            warning = null;

            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                warning = L.F("{0}: Project内アセットではないためスキップしました。", "{0}: skipped (not a project asset).", TexName(texture));
                return false;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                warning = L.F("{0}: TextureImporter を取得できませんでした。", "{0}: could not get its TextureImporter.", TexName(texture));
                return false;
            }

            // 圧縮前の設定を台帳へ（初回だけ。以後は真の原本を保つため上書きしない）
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            ImportSettingsRegistry.StoreIfAbsent(
                guid,
                importer.maxTextureSize,
                (int)importer.textureCompression,
                importer.crunchedCompression,
                importer.compressionQuality);

            TexSlimComponent.CompressionMode mode = component.Mode;
            bool isNormalMap     = importer.textureType == TextureImporterType.NormalMap;
            bool applyCrunch     = mode != TexSlimComponent.CompressionMode.ResolutionOnly && !isNormalMap;
            bool applyResolution = mode != TexSlimComponent.CompressionMode.CrunchOnly;

            if (mode != TexSlimComponent.CompressionMode.ResolutionOnly && isNormalMap)
                warning = L.F("{0}: NormalMap のため Crunch 圧縮は適用していません（法線が破綻するため）。", "{0}: Crunch skipped because this is a normal map (Crunch corrupts normals).", TexName(texture));

            // 解像度は「現在値より大きくしない」。元画像を超える拡大は Import 時に頭打ちになる。
            if (applyResolution)
            {
                int effectiveMax = component.GetEffectiveMaxSize(texture);
                importer.maxTextureSize = Mathf.Min(importer.maxTextureSize, effectiveMax);
            }

            if (applyCrunch)
            {
                importer.textureCompression  = TextureImporterCompression.Compressed;
                importer.crunchedCompression = true;
                importer.compressionQuality  = component.CompressionQuality;
            }

            importer.SaveAndReimport();
            return true;
        }

        private static void FinalizeCompression(TexSlimComponent component, bool anyDone)
        {
            if (!anyDone) return;

            Undo.RegisterCompleteObjectUndo(component, "Compress Textures");
            component.LastCompressionUtc = DateTime.UtcNow.ToString("O");
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            AssetDatabase.SaveAssets();
        }

        // ─── 復元 ────────────────────────────────────────────────────

        /// <summary>1テクスチャを圧縮前の インポート設定へ戻す（台帳から復元）。</summary>
        /// <param name="showProgress">
        /// 単発呼び出し用の進捗バーを出すか。
        /// バッチ（RevertGuids）から呼ぶ場合は false にする — 呼び出し側のキャンセル付き
        /// 進捗バーをこの内部バーが上書きし、表示がチラつくため。
        /// </param>
        public static bool RevertTexture(string assetGuid, out string errorMessage, bool showProgress = true)
        {
            errorMessage = null;

            OriginalImportEntry entry = ImportSettingsRegistry.Get(assetGuid);
            if (entry == null)
            {
                errorMessage = L.T("このテクスチャの圧縮前の設定が記録されていません。", "No pre-compression settings found in the registry.");
                return false;
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
            TextureImporter importer = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (importer == null)
            {
                // アセットが消えているなら台帳だけ整理する
                ImportSettingsRegistry.Remove(assetGuid);
                errorMessage = L.T("対象のテクスチャが見つかりませんでした。記録だけ削除しました。", "Texture not found. Removed the leftover record.");
                return false;
            }

            try
            {
                if (showProgress)
                {
                    EditorUtility.DisplayProgressBar(
                        "TexSlim",
                        L.F("インポート設定を復元中: {0}", "Restoring import settings: {0}", System.IO.Path.GetFileName(assetPath)), 0.5f);
                }

                importer.maxTextureSize      = entry.maxTextureSize;
                importer.textureCompression  = (TextureImporterCompression)entry.textureCompression;
                importer.crunchedCompression = entry.crunchedCompression;
                importer.compressionQuality  = entry.compressionQuality;
                importer.SaveAndReimport();

                ImportSettingsRegistry.Remove(assetGuid);
                return true;
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// このアバターが使うテクスチャのうち、ツールで圧縮したもの（台帳にあるもの）を
        /// すべて圧縮前へ戻す。
        /// </summary>
        /// <returns>戻した枚数</returns>
        public static int RevertAll(TexSlimComponent component)
        {
            if (component == null) return 0;

            AvatarTextureScanResult scan = AvatarTextureScanner.Scan(component);
            List<string> guids = scan.Objects
                .SelectMany(o => o.Materials)
                .SelectMany(m => m.Textures)
                .Where(t => t.IsProjectAsset)
                .Select(t => AssetDatabase.AssetPathToGUID(t.AssetPath))
                .Where(g => !string.IsNullOrEmpty(g) && ImportSettingsRegistry.Contains(g))
                .Distinct()
                .ToList();

            int reverted = RevertGuids(guids);

            if (reverted > 0)
            {
                Undo.RegisterCompleteObjectUndo(component, "Revert Texture Compression");
                component.ClearCompressionState();
                EditorUtility.SetDirty(component);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                AssetDatabase.SaveAssets();
            }
            return reverted;
        }

        /// <summary>台帳にある全テクスチャ（プロジェクト全体）を戻す。設定タブの掃除用。</summary>
        public static int RevertEverything()
        {
            int reverted = RevertGuids(ImportSettingsRegistry.AllGuids());
            if (reverted > 0) AssetDatabase.SaveAssets();
            return reverted;
        }

        private static int RevertGuids(List<string> guids)
        {
            // 圧縮と同じ理由でチャンク単位のバッチ＋キャンセル対応
            const int ChunkSize = 10;
            int reverted = 0;
            bool canceled = false;

            try
            {
                for (int start = 0; start < guids.Count && !canceled; start += ChunkSize)
                {
                    int end = Math.Min(start + ChunkSize, guids.Count);
                    try
                    {
                        AssetDatabase.StartAssetEditing();
                        for (int i = start; i < end; i++)
                        {
                            string name = System.IO.Path.GetFileName(AssetDatabase.GUIDToAssetPath(guids[i]));
                            if (DisplayCancelableProgress(
                                    L.T("インポート設定を復元中", "Restoring import settings"),
                                    name, i, guids.Count))
                            {
                                canceled = true;
                                break;
                            }
                            if (RevertTexture(guids[i], out _, showProgress: false)) reverted++;
                        }
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return reverted;
        }

        // ─── ユーティリティ ──────────────────────────────────────────

        /// <summary>再インポート後のテクスチャを読み直し、VRAM とストレージサイズを実測する</summary>
        private static void Reload(string assetPath, out long runtimeBytes, out long storageBytes)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            runtimeBytes = TextureSizeUtil.GetRuntimeBytes(tex);
            storageBytes = TextureSizeUtil.GetStorageBytes(tex);
        }

        private static string TexName(Texture texture)
            => texture != null ? texture.name : "(texture)";

        private static void DisplayProgress(string title, string itemName, int step, int totalSteps)
        {
            float progress = totalSteps <= 0 ? 0f : Mathf.Clamp01((float)step / totalSteps);
            EditorUtility.DisplayProgressBar("TexSlim", $"{title}: {itemName}", progress);
        }

        /// <summary>キャンセルボタン付き進捗バー。true が返ったら中断する。</summary>
        private static bool DisplayCancelableProgress(string title, string itemName, int step, int totalSteps)
        {
            float progress = totalSteps <= 0 ? 0f : Mathf.Clamp01((float)step / totalSteps);
            return EditorUtility.DisplayCancelableProgressBar(
                "TexSlim", $"{title}: {itemName} ({step + 1}/{totalSteps})", progress);
        }
    }

    internal sealed class CompressionResult
    {
        public static readonly CompressionResult Empty =
            new CompressionResult(0, 0L, 0L, 0L, 0L, false, new List<string>());

        public CompressionResult(
            int textureCount,
            long originalVramBytes, long compressedVramBytes,
            long originalStorageBytes, long compressedStorageBytes,
            bool crunchApplied, List<string> warnings)
        {
            TextureCount           = textureCount;
            OriginalVramBytes      = originalVramBytes;
            CompressedVramBytes    = compressedVramBytes;
            OriginalStorageBytes   = originalStorageBytes;
            CompressedStorageBytes = compressedStorageBytes;
            CrunchApplied          = crunchApplied;
            Warnings               = warnings ?? new List<string>();
        }

        public int  TextureCount           { get; }
        /// <summary>圧縮前の VRAM 合計（実測）</summary>
        public long OriginalVramBytes      { get; }
        /// <summary>圧縮後の VRAM 合計（実測）</summary>
        public long CompressedVramBytes    { get; }
        /// <summary>圧縮前のビルド上サイズ合計 ＝ ダウンロードサイズ（テクスチャ分）。Crunch の効果が現れる</summary>
        public long OriginalStorageBytes   { get; }
        /// <summary>圧縮後のビルド上サイズ合計</summary>
        public long CompressedStorageBytes { get; }
        /// <summary>Crunch 圧縮を適用したか</summary>
        public bool CrunchApplied          { get; }
        public List<string> Warnings       { get; }

        /// <summary>圧縮結果ダイアログ用の要約テキストを生成する</summary>
        public string BuildSummaryText()
        {
            if (TextureCount == 0)
            {
                return L.T("圧縮対象のテクスチャがありませんでした。\n\n"
                           + "・保護キーワードによってすべて除外されていないか\n"
                           + "・詳細タブでスイッチが OFF になっていないか\n"
                           + "を確認してください。",
                           "No textures were compressed.\n\n"
                           + "- Are they all excluded by protection keywords?\n"
                           + "- Are their toggles switched off in the Detail tab?");
            }

            // ユーザーが実際に目標にしているのは「DL サイズ ◯MB 以下」「テクスチャメモリ ◯MB 以下」
            // という生の数字なので、その2つを実測でそのまま出す。
            string sizeInfo = string.Empty;
            if (OriginalStorageBytes > 0 && CompressedStorageBytes > 0)
            {
                sizeInfo += L.T("\n\n非圧縮サイズ（テクスチャ分）:", "\n\nUncompressed size (textures):")
                          + $"\n  {TextureSizeUtil.BytesToLabel(OriginalStorageBytes)}"
                          + $" → {TextureSizeUtil.BytesToLabel(CompressedStorageBytes)}"
                          + DeltaLabel(OriginalStorageBytes, CompressedStorageBytes);
            }
            if (OriginalVramBytes > 0 && CompressedVramBytes > 0)
            {
                sizeInfo += L.T("\nテクスチャメモリ (VRAM):", "\nTexture memory (VRAM):")
                          + $"\n  {TextureSizeUtil.BytesToLabel(OriginalVramBytes)}"
                          + $" → {TextureSizeUtil.BytesToLabel(CompressedVramBytes)}"
                          + DeltaLabel(OriginalVramBytes, CompressedVramBytes);
            }

            // 初心者が必ずつまずく2点を、成功ダイアログの場で伝えておく。
            // 初心者が必ずつまずく点を、成功ダイアログの場で伝えておく。
            // 特に「非圧縮サイズ」は VRChat のアバター情報に出る同名の項目と対応する数字で、
            // 実際のダウンロードサイズはこれをさらに圧縮したものになる（Editor では測れない）。
            string notes = L.T(
                "\n\n※ 上の数字は Unity 上での推定値で、テクスチャ分のみです（メッシュ等は含みません）。\n"
                + "　 VRChat のアバター情報に出る数字とは数%ずれます。\n"
                + "※ VRChat の「ダウンロードサイズ」は、これをさらに圧縮した値になります。\n"
                + "　 見比べるときは「非圧縮サイズ」のほうと比べてください。\n"
                + "※ 同じテクスチャを使う他のアバターにも反映されます。\n"
                + "※ 戻すときは [↩ 元に戻す] を使ってください（Ctrl+Z では戻りません）。",
                "\n\n* The numbers above are Unity-side estimates and cover textures only.\n"
                + "  They differ from VRChat's avatar info by a few percent.\n"
                + "* VRChat's \"Download Size\" is compressed further than this.\n"
                + "  Compare against \"Uncompressed Size\" instead.\n"
                + "* Other avatars using the same textures are affected too.\n"
                + "* Use [↩ Restore] to undo (Ctrl+Z will not work).");

            return L.F("圧縮が完了しました。\n  テクスチャ : {0}枚",
                       "Compression finished.\n  Textures: {0}", TextureCount)
                 + sizeInfo
                 + notes;
        }

        private static string DeltaLabel(long before, long after)
        {
            float reduction = (1f - (float)after / before) * 100f;
            return
                reduction >  0.5f ? L.F(" (-{0:0}%)", " (-{0:0}%)", reduction) :
                reduction < -0.5f ? L.F(" (+{0:0}%)", " (+{0:0}%)", -reduction) :
                                    L.T(" (変化なし)", " (unchanged)");
        }
    }
}
