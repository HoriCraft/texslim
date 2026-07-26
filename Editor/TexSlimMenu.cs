// ==============================================================================
// Product : TexSlim
// File    : TexSlimMenu.cs
// Role    : Hierarchy の右クリック／GameObject メニューからコンポーネントを追加する導線。
//
//           Add Component ボタンからも追加できる（AddComponentMenu 属性）が、
//           アバターを選んで右クリック → 追加、という流れのほうが手数が少ない。
// ==============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TexSlimComponent = global::HoriCraft.TexSlim.TexSlim;

namespace HoriCraft.TexSlim.Editor
{
    internal static class TexSlimMenu
    {
        // "GameObject/" 配下の項目は Hierarchy の右クリックメニューにも出る。
        // MenuItem 属性は const 文字列しか受けられず実行時に言語を切り替えられないため、
        // メニュー項目だけは日英併記にする。
        private const string MenuPath = "GameObject/HoriCraft/TexSlim を追加 (Add TexSlim)";

        [MenuItem(MenuPath, false, 20)]
        private static void AddCompressorToSelection()
        {
            // MenuCommand を受け取らないことで、複数選択でも1回だけ呼ばれるようにする
            GameObject[] selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0) return;

            List<GameObject> targets = new List<GameObject>();
            foreach (GameObject selected in selection)
            {
                GameObject target = ResolveTarget(selected);
                if (target != null && !targets.Contains(target)) targets.Add(target);
            }

            List<Object> added = new List<Object>();
            foreach (GameObject target in targets)
            {
                TexSlimComponent existing = target.GetComponent<TexSlimComponent>();
                if (existing != null)
                {
                    // すでに付いているならそれを選択して知らせるだけにする
                    added.Add(target);
                    Debug.Log(
                        L.F("[TexSlim] {0} には既に追加されています。",
                            "[TexSlim] {0} already has the component.", target.name),
                        target);
                    continue;
                }

                Undo.AddComponent<TexSlimComponent>(target);
                added.Add(target);
                Debug.Log(
                    L.F("[TexSlim] {0} に追加しました。",
                        "[TexSlim] Added to {0}.", target.name),
                    target);
            }

            if (added.Count > 0)
            {
                Selection.objects = added.ToArray();
                EditorGUIUtility.PingObject(added[0]);
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateAddCompressorToSelection()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        /// <summary>
        /// 実際に追加すべき GameObject を決める。
        /// <para>
        /// 本コンポーネントはアバターのルートに1つだけ付ける想定なので、
        /// 子オブジェクトを選んで実行された場合は親側へ遡って付け替える。
        /// 手を滑らせて衣装メッシュなどに付いてしまう事故を防ぐ。
        /// </para>
        /// </summary>
        private static GameObject ResolveTarget(GameObject selected)
        {
            if (selected == null) return null;

            // 1) すでにコンポーネントを持つ祖先があれば、そこが正
            TexSlimComponent existing = selected.GetComponentInParent<TexSlimComponent>(true);
            if (existing != null) return existing.gameObject;

#if TEXSLIM_HAS_VRCSDK
            // 2) アバタールート（VRC_AvatarDescriptor を持つ祖先）
            VRC.SDKBase.VRC_AvatarDescriptor descriptor =
                selected.GetComponentInParent<VRC.SDKBase.VRC_AvatarDescriptor>(true);
            if (descriptor != null) return descriptor.gameObject;
#endif

            // 3) 判断材料がなければ選択されたオブジェクトそのもの
            return selected;
        }
    }
}
