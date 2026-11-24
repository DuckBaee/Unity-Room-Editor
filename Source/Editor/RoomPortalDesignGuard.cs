using System.Linq;
using UnityEditor;
using UnityEngine;

// Compare live values, including edits that have not recorded prefab overrides yet.
internal static class RoomPortalDesignGuard
{
    public static string FindViolation(RoomPortalAuthoring instance, GameObject design)
    {
        if (instance == null || design == null) return null;
        string path = AssetDatabase.GetAssetPath(design);
        if (PrefabUtility.GetCorrespondingObjectFromSourceAtPath(instance.gameObject, path) != design)
            return "원본 포털 디자인 연결이 해제되었습니다. 디자인을 다시 지정하세요.";
        return Compare(instance.transform, design.transform, true);

        string Compare(Transform actual, Transform source, bool root)
        {
            if (actual.childCount != source.childCount) return "포털 디자인의 하위 오브젝트 구성이 변경되었습니다.";
            var a = actual.GetComponents<Component>();
            var b = source.GetComponents<Component>();
            if (a.Length != b.Length) return "포털 디자인의 컴포넌트 구성이 변경되었습니다.";
            if ((!root && actual.name != source.name) || actual.gameObject.activeSelf != source.gameObject.activeSelf ||
                actual.gameObject.layer != source.gameObject.layer || actual.gameObject.tag != source.gameObject.tag)
                return "포털 디자인의 오브젝트 속성이 변경되었습니다.";
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null || b[i] == null || a[i].GetType() != b[i].GetType())
                    return "포털 디자인의 컴포넌트가 없거나 변경되었습니다.";
                using var left = new SerializedObject(a[i]);
                using var right = new SerializedObject(b[i]);
                var p = left.GetIterator();
                bool enterChildren = true;
                while (p.Next(enterChildren))
                {
                    enterChildren = p.propertyType == SerializedPropertyType.Generic;
                    string key = p.propertyPath;
                    if (key.StartsWith("m_Prefab") || key == "m_CorrespondingSourceObject" || key == "m_GameObject" ||
                        key == "m_ObjectHideFlags" || key.StartsWith("m_Children") || key == "m_Father" || key == "m_RootOrder") continue;
                    if (root && a[i] is Transform && (key.StartsWith("m_LocalPosition") || key.StartsWith("m_LocalRotation") || key.StartsWith("m_LocalEulerAnglesHint"))) continue;
                    if (root && a[i] is RoomPortalAuthoring && new[] { "portalId", "designPrefab", "reservationDepth", "socketType" }.Contains(key)) continue;
                    var q = right.FindProperty(key);
                    if (q == null) return "포털 디자인의 직렬화 구조가 변경되었습니다.";
                    if (p.propertyType == SerializedPropertyType.Generic) continue;
                    bool equal;
                    if (p.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        var value = p.objectReferenceValue;
                        if (value != null && !EditorUtility.IsPersistent(value))
                            value = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(value, path) ?? value;
                        equal = value == q.objectReferenceValue;
                    }
                    else equal = SerializedProperty.DataEquals(p, q);
                    if (!equal) return $"포털 디자인 변경: {actual.name} / {a[i].GetType().Name}.{key}. 원본 디자인에서 수정하거나 다른 디자인을 사용하세요.";
                }
            }
            for (int i = 0; i < actual.childCount; i++)
            {
                var child = actual.GetChild(i);
                if (PrefabUtility.GetCorrespondingObjectFromSourceAtPath(child, path) != source.GetChild(i))
                    return "포털 디자인의 하위 순서 또는 연결이 변경되었습니다.";
                string error = Compare(child, source.GetChild(i), false);
                if (error != null) return error;
            }
            return null;
        }
    }
}
