using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Stage both assets without mutating the live objects. Acquire every destination
// before writing; retain the original bytes until the complete pair is committed.
internal static class RoomAssetSaveTransaction
{
    private sealed class Destination : IDisposable
    {
        public string path;
        public bool existed;
        public byte[] before;
        public byte[] after;
        public FileStream stream;
        public void Write(byte[] bytes)
        {
            stream.Position = 0;
            stream.Write(bytes, 0, bytes.Length);
            stream.SetLength(bytes.Length);
            stream.Flush(true);
        }
        public void Dispose() => stream?.Dispose();
    }

    public static void Save(GameObject root, BlockData draft, string blockPath, string artPath)
    {
        if (root == null) throw new InvalidOperationException("방 루트가 없습니다.");
        if (!blockPath.StartsWith("Assets/", StringComparison.Ordinal) ||
            !artPath.StartsWith("Assets/", StringComparison.Ordinal))
            throw new IOException("Assets 내부 경로만 저장할 수 있습니다.");
        string stage = "Assets/__RoomSave_" + Guid.NewGuid().ToString("N");
        string stagedBlockPath = stage + "/" + Path.GetFileName(blockPath);
        BlockData stagedData = null;
        var files = new List<Destination>();
        bool committed = false;
        bool wrote = false;
        try
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(stage));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(artPath) != null &&
                !AssetDatabase.CopyAsset(artPath, stage + "/Art.prefab"))
                throw new IOException("기존 아트 임시 복사 실패");
            // The editor opens an unpacked room root with nested instances intact.
            // Object.Instantiate would silently unpack those nested prefab links.
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                throw new InvalidOperationException("편집용 방 루트의 최외곽 연결을 먼저 해제해야 합니다.");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, stage + "/Art.prefab", out bool ok);
            if (!ok || prefab == null) throw new IOException("아트 임시 저장 실패");
            stagedData = UnityEngine.Object.Instantiate(draft);
            stagedData.visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(artPath) ?? prefab;
            stagedData.spaceCheckFingerprint = RoomValidationFingerprint.Compute(stagedData, stage + "/Art.prefab");
            AssetDatabase.CreateAsset(stagedData, stagedBlockPath);
            stagedData.name = Path.GetFileNameWithoutExtension(blockPath);
            EditorUtility.SetDirty(stagedData);
            AssetDatabase.SaveAssetIfDirty(stagedData);
            if (!File.Exists(stagedBlockPath)) throw new IOException("방 데이터 임시 저장 실패");

            Add(artPath, stage + "/Art.prefab");
            Add(blockPath, stagedBlockPath);
            // Existing GUIDs are retained. New assets inherit the staged GUIDs.
            if (!File.Exists(artPath + ".meta")) Add(artPath + ".meta", stage + "/Art.prefab.meta");
            if (!File.Exists(blockPath + ".meta")) Add(blockPath + ".meta", stagedBlockPath + ".meta");
            foreach (var f in files)
            {
                f.existed = File.Exists(f.path);
                f.stream = new FileStream(f.path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                f.before = new byte[f.stream.Length];
                int offset = 0;
                while (offset < f.before.Length)
                {
                    int read = f.stream.Read(f.before, offset, f.before.Length - offset);
                    if (read == 0) throw new EndOfStreamException(f.path);
                    offset += read;
                }
            }
            wrote = true;
            foreach (var f in files) f.Write(f.after);
            committed = true;
        }
        catch
        {
            if (wrote)
                foreach (var f in files)
                    if (f.stream != null) f.Write(f.before);
            throw;
        }
        finally
        {
            foreach (var f in files)
            {
                bool opened = f.stream != null;
                f.Dispose();
                if (!committed && opened && !f.existed) File.Delete(f.path);
            }
            // Remove staging before import so copied GUIDs are never imported twice.
            AssetDatabase.DeleteAsset(stage);
        }
        AssetDatabase.ImportAsset(artPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(blockPath, ImportAssetOptions.ForceUpdate);

        void Add(string destination, string source)
        {
            files.Add(new Destination { path = destination, after = File.ReadAllBytes(source) });
        }
    }
}
