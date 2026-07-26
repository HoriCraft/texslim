// ==============================================================================
// Product : TexSlim
// File    : ImportSettingsRegistry.cs
// Role    : 圧縮する前のインポート設定を、プロジェクト全体で
//           1つの台帳に控える。GUID をキーにするのでアセットの移動・改名に耐え、
//           シーンやアバターをまたいでも同じ台帳を参照できる。
//
//           台帳を「プロジェクト全体・アセット単位」に置くのが重要な点。
//           コンポーネント（＝シーン内のアバター）に記録すると、別シーンで同じ
//           テクスチャを開いたときに真の原本設定を辿れなくなるため。
//
//           保存先は ProjectSettings/ の JSON。アセットではないので再インポートを
//           誘発せず、バージョン管理にも自然に乗る。
// ==============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HoriCraft.TexSlim.Editor
{
    /// <summary>1テクスチャの「圧縮前」の TextureImporter 設定</summary>
    [Serializable]
    internal sealed class OriginalImportEntry
    {
        public string guid;
        public int    maxTextureSize;
        public int    textureCompression;   // TextureImporterCompression を int で保持
        public bool   crunchedCompression;
        public int    compressionQuality;
    }

    [Serializable]
    internal sealed class OriginalImportTable
    {
        public List<OriginalImportEntry> entries = new List<OriginalImportEntry>();
    }

    /// <summary>
    /// 圧縮前設定の台帳。プロジェクトに 1 つ。
    /// </summary>
    internal static class ImportSettingsRegistry
    {
        private const string FilePath = "ProjectSettings/TexSlim.json";

        /// <summary>
        /// ツール名が VRC Easy Texture Compressor だった頃の記録ファイル。
        /// ここに圧縮前の設定が残っている状態でファイル名だけ変えると、
        /// 圧縮済みのテクスチャを二度と戻せなくなる。読み込み時に引き継ぐ。
        /// </summary>
        private const string LegacyFilePath = "ProjectSettings/VRCEasyTextureCompressor.json";

        private static OriginalImportTable _table;

        private static OriginalImportTable Table
        {
            get
            {
                if (_table == null) Load();
                return _table;
            }
        }

        private static void Load()
        {
            try
            {
                string path = File.Exists(FilePath) ? FilePath
                            : File.Exists(LegacyFilePath) ? LegacyFilePath
                            : null;

                if (path == null)
                {
                    _table = new OriginalImportTable();
                    return;
                }

                string json = File.ReadAllText(path);
                _table = JsonUtility.FromJson<OriginalImportTable>(json) ?? new OriginalImportTable();

                // 旧ファイルから読んだら、新しい名前で保存し直して旧ファイルは片付ける
                if (path == LegacyFilePath)
                {
                    Save();
                    try { File.Delete(LegacyFilePath); } catch { /* 消せなくても実害はない */ }
                    Debug.Log($"[TexSlim] 圧縮前の設定を {LegacyFilePath} から {FilePath} へ引き継ぎました。");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TexSlim] 圧縮前の設定の記録ファイルを読めませんでした。新しく作り直します。\n{e.Message}");
                _table = new OriginalImportTable();
            }
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(_table, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[TexSlim] 圧縮前の設定の記録ファイルを保存できませんでした。\n{e.Message}");
            }
        }

        public static bool Contains(string guid)
            => !string.IsNullOrEmpty(guid) && Table.entries.Exists(e => e.guid == guid);

        public static OriginalImportEntry Get(string guid)
            => string.IsNullOrEmpty(guid) ? null : Table.entries.Find(e => e.guid == guid);

        /// <summary>
        /// 圧縮前設定を控える。すでに記録があれば<strong>上書きしない</strong>。
        /// 2回続けて縮小しても、最初の（＝真の原本）状態へ戻せるようにするため。
        /// </summary>
        public static void StoreIfAbsent(
            string guid, int maxTextureSize, int textureCompression,
            bool crunchedCompression, int compressionQuality)
        {
            if (string.IsNullOrEmpty(guid)) return;
            if (Contains(guid)) return;

            Table.entries.Add(new OriginalImportEntry
            {
                guid                = guid,
                maxTextureSize      = maxTextureSize,
                textureCompression  = textureCompression,
                crunchedCompression = crunchedCompression,
                compressionQuality  = compressionQuality,
            });
            Save();
        }

        public static void Remove(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;
            if (Table.entries.RemoveAll(e => e.guid == guid) > 0) Save();
        }

        /// <summary>台帳にあるすべての GUID（コピーを返す）</summary>
        public static List<string> AllGuids()
            => Table.entries.ConvertAll(e => e.guid);

        public static int Count => Table.entries.Count;
    }
}
