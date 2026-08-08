// ============================================================
// NCV-01 Cybership - Module Mount (Ship Designer grid cell)
//
// Attach to each cell of the ship's mount grid. This is the
// collider you point at (with the Use trigger) to paint modules
// from the Ship Designer console. cellIndex must match the mount's
// position in ShipDesignerManager.cellAnchors[].
// ============================================================

using UdonSharp;
using UnityEngine;

public class ModuleMount : UdonSharpBehaviour
{
    public int cellIndex = 0;
}
