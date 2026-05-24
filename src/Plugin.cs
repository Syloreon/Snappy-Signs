using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SnappySigns
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SnappySignsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.syloreon.snappysigns";
        public const string PluginName = "Snappy Signs";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> Corners;
        internal static ConfigEntry<bool> EdgeMidpoints;
        internal static ConfigEntry<bool> Center;

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind(
                "General", "Enabled", true,
                "Master toggle. When off, signs are left untouched (takes effect on next world/game load).");
            Corners = Config.Bind(
                "SnapPoints", "Corners", true,
                "Add snap points at the four corners of each sign's board.");
            EdgeMidpoints = Config.Bind(
                "SnapPoints", "EdgeMidpoints", true,
                "Add snap points at the midpoint of each of the board's four edges.");
            Center = Config.Bind(
                "SnapPoints", "Center", true,
                "Add a snap point at the center of each sign's board.");

            new Harmony(PluginGuid).PatchAll();
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }
    }

    /// <summary>
    /// ZNetScene.Awake registers every prefab in the game. We run afterwards and
    /// inject snap points into any prefab that is a sign, so the build placement
    /// system (Piece.GetSnapPoints) will pick them up.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake_Patch
    {
        private static void Postfix(ZNetScene __instance)
        {
            if (!SnappySignsPlugin.ModEnabled.Value)
                return;

            int patched = 0;
            foreach (GameObject prefab in __instance.m_prefabs)
            {
                if (prefab == null)
                    continue;
                // A sign that can be built has both a Sign and a Piece component.
                if (prefab.GetComponent<Sign>() == null || prefab.GetComponent<Piece>() == null)
                    continue;
                if (SignSnapper.AddSnapPoints(prefab))
                    patched++;
            }

            SnappySignsPlugin.Log.LogInfo($"Added snap points to {patched} sign prefab(s).");
        }
    }

    internal static class SignSnapper
    {
        private const string SnapTag = "snappoint";

        /// <summary>
        /// Adds snap points to a sign prefab. Returns true if points were added.
        /// Idempotent: signs that already have snap points (vanilla or from a previous
        /// run / another mod) are left untouched.
        /// </summary>
        internal static bool AddSnapPoints(GameObject prefab)
        {
            if (HasSnapPoints(prefab.transform))
                return false;

            if (!TryGetLocalBounds(prefab, out Bounds bounds))
            {
                SnappySignsPlugin.Log.LogWarning(
                    $"Could not determine bounds for sign '{prefab.name}'; skipping.");
                return false;
            }

            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;

            // A sign board is a thin slab: the smallest extent is its thickness.
            // The other two axes define the visible face plane. We place snap points
            // on that plane (through the board's mid-thickness) so edges and centers
            // line up cleanly with walls, beams and posts.
            int thin = 0;
            if (e.y < e[thin]) thin = 1;
            if (e.z < e[thin]) thin = 2;
            int u = (thin + 1) % 3; // first face axis
            int v = (thin + 2) % 3; // second face axis

            Vector3 du = Axis(u) * e[u];
            Vector3 dv = Axis(v) * e[v];

            int added = 0;

            if (SnappySignsPlugin.Center.Value)
                added += Add(prefab.transform, c);

            if (SnappySignsPlugin.Corners.Value)
            {
                added += Add(prefab.transform, c + du + dv);
                added += Add(prefab.transform, c + du - dv);
                added += Add(prefab.transform, c - du + dv);
                added += Add(prefab.transform, c - du - dv);
            }

            if (SnappySignsPlugin.EdgeMidpoints.Value)
            {
                added += Add(prefab.transform, c + du);
                added += Add(prefab.transform, c - du);
                added += Add(prefab.transform, c + dv);
                added += Add(prefab.transform, c - dv);
            }

            if (added == 0)
                return false;

            SnappySignsPlugin.Log.LogInfo($"  '{prefab.name}': added {added} snap point(s).");
            return true;
        }

        private static int Add(Transform parent, Vector3 localPos)
        {
            GameObject sp = new GameObject("_snappoint")
            {
                tag = SnapTag,
                layer = parent.gameObject.layer
            };
            sp.transform.SetParent(parent, worldPositionStays: false);
            sp.transform.localPosition = localPos;
            sp.transform.localRotation = Quaternion.identity;
            return 1;
        }

        private static bool HasSnapPoints(Transform root)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i).CompareTag(SnapTag))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Computes a tight axis-aligned bounding box of all the prefab's meshes,
        /// expressed in the prefab root's local space.
        /// </summary>
        private static bool TryGetLocalBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            bool has = false;

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            Transform root = prefab.transform;

            foreach (MeshFilter mf in filters)
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null)
                    continue;

                Vector3 mc = mesh.bounds.center;
                Vector3 me = mesh.bounds.extents;

                // Transform all 8 mesh-local corners into the prefab root's local space.
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = mc + new Vector3(me.x * sx, me.y * sy, me.z * sz);
                    Vector3 world = mf.transform.TransformPoint(corner);
                    Vector3 local = root.InverseTransformPoint(world);

                    if (!has)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return has;
        }

        private static Vector3 Axis(int i)
        {
            switch (i)
            {
                case 0: return Vector3.right;
                case 1: return Vector3.up;
                default: return Vector3.forward;
            }
        }
    }
}
