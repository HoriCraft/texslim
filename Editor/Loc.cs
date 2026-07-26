// ==============================================================================
// Product : TexSlim
// File    : Loc.cs
// Role    : 日英ローカライズ。
//
//           方式は「呼び出し側に日英を併記する」( L.T("日本語", "English") )。
//           辞書キー方式にしないのは、キー文字列のわずかなズレが
//           サイレントに翻訳漏れになる事故を避けるため。この方式なら
//           文字列を書いた場所に両言語が並ぶので、レビューでも漏れが見える。
//
//           言語設定は EditorPrefs（ユーザー単位）。プロジェクトやシーンには
//           保存しない。表示言語は共同作業者ごとに好みが違うため。
// ==============================================================================

using UnityEditor;

namespace HoriCraft.TexSlim.Editor
{
    internal static class L
    {
        private const string PrefKey = "TexSlim_English";

        private static bool? _english;

        /// <summary>true なら英語表示</summary>
        public static bool English
        {
            get
            {
                if (!_english.HasValue) _english = EditorPrefs.GetBool(PrefKey, false);
                return _english.Value;
            }
            set
            {
                _english = value;
                EditorPrefs.SetBool(PrefKey, value);
            }
        }

        /// <summary>現在の言語の文字列を返す</summary>
        public static string T(string ja, string en) => English ? en : ja;

        /// <summary>現在の言語のテンプレートへ string.Format を適用して返す</summary>
        public static string F(string ja, string en, params object[] args)
            => string.Format(English ? en : ja, args);
    }
}
