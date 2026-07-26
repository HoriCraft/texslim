// ==============================================================================
// Product : TexSlim
// File    : TexSlimEditor.Actions.cs
// Role    : 圧縮・復元の実行（ダイアログ・結果表示）
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
        // ─── 圧縮・復元 ──────────────────────────────────────────────

        /// <summary>圧縮ボタンのラベル。両タブで同一の処理なので文言も共通にする。</summary>
        private string CompressButtonLabel => L.T("圧縮を実行", "Compress");

        private string CompressButtonTooltip
            => L.T("圧縮対象のテクスチャをまとめて軽くします。\n"
                   + "元の画像ファイルはそのままなので、[↩ 元に戻す] でいつでも取り消せます。",
                   "Changes the import settings of all included textures to shrink them.\n"
                   + "Source images stay untouched; [↩ Restore] undoes it at any time.");

        private void RunCompression()
        {
            RefreshScan();
            CompressionResult result = TextureCompressionProcessor.Compress(component);
            RefreshScan();

            foreach (string w in result.Warnings)
                Debug.LogWarning($"[TexSlim] {w}", component);

            string warns = result.Warnings.Count > 0
                ? L.F("\n\n警告 ({0}件):\n", "\n\nWarnings ({0}):\n", result.Warnings.Count)
                  + string.Join("\n", result.Warnings.Take(3))
                  + (result.Warnings.Count > 3
                      ? L.T("\n…（Console を確認してください）", "\n… (see Console for the rest)")
                      : "")
                : "";

            EditorUtility.DisplayDialog(
                "TexSlim",
                result.BuildSummaryText() + warns,
                "OK");
        }

        /// <summary>テクスチャ1枚だけを圧縮する（インポート設定を変更）。</summary>
        private void RunSingleCompress(AvatarTextureNode texNode)
        {
            if (texNode?.Texture == null) return;

            string textureName = texNode.Texture.name;
            CompressionResult result = TextureCompressionProcessor.CompressSingle(component, texNode);
            RefreshScan();

            foreach (string w in result.Warnings)
                Debug.LogWarning($"[TexSlim] {w}", component);

            if (result.TextureCount == 0)
            {
                EditorUtility.DisplayDialog(
                    "TexSlim",
                    L.F("「{0}」を圧縮できませんでした。\n", "Could not compress \"{0}\".\n", textureName)
                    + (result.Warnings.Count > 0
                        ? result.Warnings[0]
                        : L.T("このテクスチャは処理できませんでした。", "This texture could not be processed.")),
                    "OK");
                return;
            }

            Debug.Log(L.F("[TexSlim] 個別圧縮完了: {0}", "[TexSlim] Compressed: {0}", textureName), component);
            Repaint();
        }

        /// <summary>
        /// 圧縮済みテクスチャのサイズを選び直したとき、戻す→新サイズで再圧縮を1操作で行う。
        /// 台帳は「戻す」で原本設定を消費し、直後の圧縮で同じ原本設定を再記録するので、
        /// 何度サイズを変えても常に本当の原本へ戻せる。
        /// </summary>
        private void RunResizeCompressed(AvatarTextureNode texNode)
        {
            if (texNode?.Texture == null || string.IsNullOrEmpty(texNode.AssetPath)) return;

            string guid = AssetDatabase.AssetPathToGUID(texNode.AssetPath);
            if (!TextureCompressionProcessor.RevertTexture(guid, out string error))
            {
                EditorUtility.DisplayDialog(
                    L.T("TexSlim — エラー", "TexSlim — Error"),
                    L.F("サイズ変更に失敗しました。\n\n{0}", "Resize failed.\n\n{0}", error), "OK");
                RefreshScan();
                return;
            }

            CompressionResult result = TextureCompressionProcessor.CompressSingle(component, texNode);
            AssetDatabase.SaveAssets();
            RefreshScan();

            foreach (string w in result.Warnings)
                Debug.LogWarning($"[TexSlim] {w}", component);

            Debug.Log(
                L.F("[TexSlim] サイズを変更しました: {0} → {1}px 以下",
                    "[TexSlim] Resized: {0} → within {1}px",
                    texNode.Texture.name, component.GetEffectiveMaxSize(texNode.Texture)),
                component);
            Repaint();
        }

        /// <summary>この1枚を圧縮前へ戻す。</summary>
        private void RunSingleRevert(AvatarTextureNode texNode)
        {
            if (texNode?.Texture == null || string.IsNullOrEmpty(texNode.AssetPath)) return;

            string guid = AssetDatabase.AssetPathToGUID(texNode.AssetPath);
            bool ok = TextureCompressionProcessor.RevertTexture(guid, out string error);
            AssetDatabase.SaveAssets();
            RefreshScan();

            if (!ok)
            {
                EditorUtility.DisplayDialog(
                    L.T("TexSlim — エラー", "TexSlim — Error"),
                    L.F("復元に失敗しました。\n\n{0}", "Restore failed.\n\n{0}", error), "OK");
                return;
            }

            Debug.Log(L.F("[TexSlim] 復元完了: {0}", "[TexSlim] Restored: {0}", texNode.Texture.name), component);
            Repaint();
        }

        /// <summary>このアバターで圧縮したテクスチャをすべて圧縮前へ戻す。</summary>
        private void RunRevert()
        {
            int count = scan?.CompressedTextureCount ?? 0;
            bool confirmed = EditorUtility.DisplayDialog(
                L.T("元に戻す", "Restore"),
                L.F("このアバターで圧縮した {0} 枚のテクスチャを、圧縮前のインポート設定へ戻します。\n\n"
                    + "・元の画像ファイルには手を加えていないので、画質は完全に元どおりになります\n"
                    + "・その後に手動で設定を変えていた場合は、その変更も上書きされます\n\n"
                    + "実行しますか？",
                    "Restores {0} compressed textures on this avatar to their\n"
                    + "pre-compression import settings.\n\n"
                    + "- Source images were never modified, so quality is fully restored\n"
                    + "- Any manual import-setting changes made since will be overwritten\n\n"
                    + "Proceed?",
                    count),
                L.T("元に戻す", "Restore"), L.T("キャンセル", "Cancel"));
            if (!confirmed) return;

            int reverted = TextureCompressionProcessor.RevertAll(component);
            RefreshScan();

            EditorUtility.DisplayDialog(
                "TexSlim",
                L.F("↩ 圧縮前の状態へ戻しました。\n\n復元: {0} 枚", "↩ Restored to pre-compression state.\n\nRestored: {0}", reverted),
                "OK");
        }

        /// <summary>プロジェクト全体の台帳にある全テクスチャを戻す（設定タブ）。</summary>
        private void RunRevertEverything(int count)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                L.T("圧縮したテクスチャをすべて元に戻す", "Restore ALL compressed textures"),
                L.F("プロジェクト全体で圧縮した {0} 枚のテクスチャを、圧縮前のインポート設定へ戻します。\n"
                    + "（このアバターに限らず、このツールで圧縮したすべてが対象です）\n\n"
                    + "実行しますか？",
                    "Restores all {0} textures compressed anywhere in this project\n"
                    + "(not just this avatar) to their pre-compression import settings.\n\n"
                    + "Proceed?",
                    count),
                L.T("すべて元に戻す", "Restore all"), L.T("キャンセル", "Cancel"));
            if (!confirmed) return;

            int reverted = TextureCompressionProcessor.RevertEverything();
            RefreshScan();

            EditorUtility.DisplayDialog(
                "TexSlim",
                L.F("↩ すべて圧縮前へ戻しました。\n\n復元: {0} 枚", "↩ All restored.\n\nRestored: {0}", reverted),
                "OK");
        }

    }
}
