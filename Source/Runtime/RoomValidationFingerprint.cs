using UnityEngine;
#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
#endif

// Editor-side provenance of a saved validation result. Player builds use the
// persisted state, refreshed by the asset watcher and build preprocessor.
public static class RoomValidationFingerprint
{
#if UNITY_EDITOR
    public static bool IsCurrent(BlockData data)
    {
        if (!EditorUtility.IsPersistent(data)) return true; // transient runtime/test inputs
        if (string.IsNullOrEmpty(data.spaceCheckFingerprint)) return false;
        try { return data.spaceCheckFingerprint == Compute(data); }
        catch { return false; } // missing/unreadable dependency must never authorize generation
    }

    public static string Compute(BlockData data, string artPathOverride = null)
    {
        string artPath = artPathOverride ?? AssetDatabase.GetAssetPath(data.visualPrefab);
        if (string.IsNullOrEmpty(artPath) || !File.Exists(artPath)) return "";
        var s = new StringBuilder();
        s.Append(data.spaceCheckVersion).Append('|');
        if (data.cells != null) foreach (var c in data.cells) s.Append(c.x).Append(',').Append(c.y).Append(',').Append(c.z).Append(';');
        s.Append('|');
        if (data.portals != null) foreach (var p in data.portals)
        {
            if (p == null) { s.Append("null;"); continue; }
            s.Append(p.portalId).Append(':').Append((int)p.socketType).Append(':').Append(p.directionIndex).Append(':').Append(p.useReservationBounds);
            Number(p.localPosition.x); Number(p.localPosition.y); Number(p.localPosition.z);
            Number(p.openingSize.x); Number(p.openingSize.y);
            Number(p.reservationBounds.x); Number(p.reservationBounds.y); Number(p.reservationBounds.z);
            s.Append(AssetDatabase.GetAssetPath(p.visualPrefab)).Append(';');
        }
        using (var hash = SHA256.Create())
        {
            s.Append(Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(artPath))));
            // The containing prefab's GUID/path is deliberately excluded: a staged
            // save and its committed counterpart have identical content/dependencies.
            foreach (string dependency in AssetDatabase.GetDependencies(artPath, true).Where(p => p != artPath).OrderBy(p => p, StringComparer.Ordinal))
                s.Append('|').Append(AssetDatabase.AssetPathToGUID(dependency)).Append(':').Append(AssetDatabase.GetAssetDependencyHash(dependency));
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(s.ToString())));
        }
        void Number(float v) => s.Append(':').Append(v.ToString("R", CultureInfo.InvariantCulture));
    }
#endif
}
