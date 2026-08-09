// ============================================================
// NCV-01 Cybership - Ship Designer (Level-Designer Console)
//
// A palette-driven module editor that plays like a level editor:
//   1. Open the console, pick a module from the palette.
//   2. Point at the ship's mount grid and pull Use to paint cells.
//   3. Rotate cells, erase, clear, or scatter a random layout.
//   4. Save your blueprint to PlayerData, load it back any time.
//   5. Apply the finished layout to the synced ship so the whole
//      crew sees it.
//
// Grid: gridCols x gridRows cells. Each cell maps 1:1 to a hull
// mount. cellVisualRoots[i] is the cell root GameObject and its
// children are the module visuals, index-aligned with MODULE_NAMES;
// at most one child is active per cell at a time (empty = none).
// cellAnchors[i] is an optional selection highlight marker.
//
// Networking: the applied design is a compact [UdonSynced] string.
// Any player may apply via the official "become-owner" pattern - the
// editor briefly takes ownership to write the string, waits for the
// serialization to flush, then hands ownership back to the master.
// Personal blueprints persist per-player via PlayerData (which is
// client-authoritative, like the rank/XP system).
// ============================================================

using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using VRC.Udon.Common;

public class ShipDesignerManager : UdonSharpBehaviour
{
    [Header("Grid")]
    public int gridCols = 4;
    public int gridRows = 4;
    public int maxModulesPlaced = 24;

    [Header("Module Palette (index-aligned with cell visual children)")]
    public string[] MODULE_NAMES = new string[]
    {
        "Hull Plate",   // 0
        "Engine",       // 1
        "Turret",       // 2
        "Sensor Pod",   // 3
        "Power Cell",   // 4
        "Cargo Pod"     // 5
    };

    [Header("Hull Mounts (length = gridCols * gridRows)")]
    public Transform[] cellAnchors;
    public GameObject[] cellVisualRoots;

    [Header("Console")]
    public GameObject designerUI;
    public TextMeshPro statusText;
    public TextMeshPro gridText;

    [Header("Interaction")]
    public float rayRange = 30f;

    [Header("References")]
    public HUDManager hudManager;

    // Tool modes (level-designer style).
    private const int MODE_PLACE = 0;
    private const int MODE_ERASE = 1;

    // Applied (synced) design. Written via become-owner on apply.
    [UdonSynced, FieldChangeCallback(nameof(ShipDesignData))]
    private string _shipDesignData = "";

    [UdonSynced, FieldChangeCallback(nameof(DesignAuthor))]
    private string _designAuthor = "";

    // Local design being edited (your in-progress blueprint).
    private int[] _cellModules;
    private int[] _cellRotations;

    private int _editMode = MODE_PLACE;
    private int _activeModule = 0;
    private int _selectedCell = -1;
    private bool _isEditing = false;
    private bool _pendingApply = false;
    private bool _dataReady = false;

    private int _gridSize = 0;

    // PlayerData key (world-scoped automatically).
    private const string KEY_DESIGN = "cybership_design";

    public string ShipDesignData
    {
        get { return _shipDesignData; }
        set
        {
            _shipDesignData = value;

            // While a player is actively editing, keep their local
            // preview; otherwise reflect the shared ship design.
            if (!_isEditing)
            {
                ParseDesign(value);
                ApplyVisualGrid();
            }
        }
    }

    public string DesignAuthor
    {
        get { return _designAuthor; }
        set
        {
            _designAuthor = value;
            RefreshStatus();
        }
    }

    // ============================================================
    void Start()
    {
        _gridSize = gridCols * gridRows;
        if (_gridSize < 0) _gridSize = 0;

        _cellModules = new int[_gridSize];
        _cellRotations = new int[_gridSize];

        for (int i = 0; i < _gridSize; i++)
        {
            _cellModules[i] = -1;
            _cellRotations[i] = 0;
        }

        // Late joiners arrive with _shipDesignData already synced.
        ParseDesign(_shipDesignData);
        ApplyVisualGrid();
        RefreshStatus();
    }

    // PlayerData is only safe to read after the local player's data has been restored.
    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;

        _dataReady = true;
        LoadDesignFromPlayerData();

        // The host restores its saved layout to the shared ship.
        if (Networking.IsMaster)
            ApplyDesign();
    }

    // ============================================================
    // CONSOLE OPEN / CLOSE
    // ============================================================
    public override void OnStationEntered(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;
        OpenDesigner();
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;
        CloseDesigner();
    }

    public void OpenDesigner()
    {
        _isEditing = true;
        if (designerUI != null) designerUI.SetActive(true);

        ApplyVisualGrid();
        RefreshStatus();
    }

    public void CloseDesigner()
    {
        _isEditing = false;
        if (designerUI != null) designerUI.SetActive(false);

        // Revert the local preview to the shared (synced) design.
        ParseDesign(_shipDesignData);
        ApplyVisualGrid();
        RefreshStatus();
    }

    public void ToggleDesigner()
    {
        if (_isEditing) CloseDesigner();
        else OpenDesigner();
    }

    // ============================================================
    // POINT-AND-PAINT (level designer feel)
    // ============================================================
    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (!value) return;
        if (!_isEditing) return;

        VRCPlayerApi player = Networking.LocalPlayer;
        if (!Utilities.IsValid(player)) return;

        VRCPlayerApi.TrackingData hand = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        Ray ray = new Ray(hand.position, hand.rotation * Vector3.forward);

        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, rayRange)) return;

        ModuleMount mount = hit.collider.GetComponent<ModuleMount>();
        if (mount == null) return;

        PaintCell(mount.cellIndex);
    }

    // ============================================================
    // GRID EDITING
    // ============================================================
    public void PaintCell(int cell)
    {
        if (!_isEditing) return;
        if (cell < 0 || cell >= _gridSize) return;

        if (_editMode == MODE_ERASE)
        {
            _cellModules[cell] = -1;
            _cellRotations[cell] = 0;
        }
        else
        {
            if (_cellModules[cell] < 0 && CountPlaced() >= maxModulesPlaced)
            {
                Notify("SHIP DESIGNER: MODULE LIMIT REACHED (" + maxModulesPlaced.ToString() + ")");
                return;
            }

            _cellModules[cell] = _activeModule;
            _cellRotations[cell] = 0;
        }

        _selectedCell = cell;
        ApplyVisualGrid();
        RefreshStatus();
    }

    public void RotateSelected()
    {
        if (!_isEditing) return;
        if (_selectedCell < 0 || _selectedCell >= _gridSize) return;
        if (_cellModules[_selectedCell] < 0) return;

        _cellRotations[_selectedCell] = (_cellRotations[_selectedCell] + 1) % 4;
        ApplyVisualGrid();
        RefreshStatus();
    }

    public void CycleCellNext()
    {
        if (_gridSize <= 0) return;
        _selectedCell++;
        if (_selectedCell >= _gridSize) _selectedCell = 0;
        RefreshStatus();
    }

    public void CycleCellPrevious()
    {
        if (_gridSize <= 0) return;
        _selectedCell--;
        if (_selectedCell < 0) _selectedCell = _gridSize - 1;
        RefreshStatus();
    }

    public void ClearDesign()
    {
        if (!_isEditing) return;

        for (int i = 0; i < _gridSize; i++)
        {
            _cellModules[i] = -1;
            _cellRotations[i] = 0;
        }

        ApplyVisualGrid();
        RefreshStatus();
    }

    public void FillRandom()
    {
        if (!_isEditing) return;
        if (MODULE_NAMES == null || MODULE_NAMES.Length == 0) return;

        for (int i = 0; i < _gridSize; i++)
        {
            if (_cellModules[i] >= 0) continue;
            if (CountPlaced() >= maxModulesPlaced) break;

            _cellModules[i] = Random.Range(0, MODULE_NAMES.Length);
            _cellRotations[i] = Random.Range(0, 4);
        }

        ApplyVisualGrid();
        RefreshStatus();
    }

    // ============================================================
    // TOOLS & PALETTE
    // ============================================================
    public void SelectModule(int module)
    {
        if (module < 0 || module >= MODULE_NAMES.Length) return;

        _activeModule = module;
        _editMode = MODE_PLACE;
        RefreshStatus();
    }

    // Explicit wrappers so world-space UI buttons can be wired
    // without needing parameterized Udon events.
    public void SelectModule0() { SelectModule(0); }
    public void SelectModule1() { SelectModule(1); }
    public void SelectModule2() { SelectModule(2); }
    public void SelectModule3() { SelectModule(3); }
    public void SelectModule4() { SelectModule(4); }
    public void SelectModule5() { SelectModule(5); }

    public void SetPlaceMode() { _editMode = MODE_PLACE; RefreshStatus(); }
    public void SetEraseMode() { _editMode = MODE_ERASE; RefreshStatus(); }

    // ============================================================
    // PERSISTENCE (local blueprint)
    // ============================================================
    public void SaveDesign()
    {
        if (!_dataReady)
        {
            Notify("SHIP DESIGNER: DATA NOT READY");
            return;
        }

        PlayerData.SetString(KEY_DESIGN, EncodeDesign());
        Notify("SHIP DESIGNER: BLUEPRINT SAVED");
        RefreshStatus();
    }

    public void LoadDesign()
    {
        if (!_dataReady) return;

        LoadDesignFromPlayerData();
        ApplyVisualGrid();
        RefreshStatus();
        Notify("SHIP DESIGNER: BLUEPRINT LOADED");
    }

    private void LoadDesignFromPlayerData()
    {
        if (_gridSize <= 0) return;
        if (!PlayerData.HasKey(Networking.LocalPlayer, KEY_DESIGN)) return;

        ParseDesign(PlayerData.GetString(Networking.LocalPlayer, KEY_DESIGN));
    }

    // ============================================================
    // APPLY (become-owner, then hand back to master)
    // ============================================================
    public void ApplyDesign()
    {
        _pendingApply = true;

        if (Networking.IsOwner(gameObject))
            ApplyDesignAsOwner();
        else
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // OnOwnershipTransferred also fires when we LOSE ownership (e.g. two
        // players apply at once) - only write if the transfer actually landed.
        if (Networking.IsOwner(gameObject) && _pendingApply)
            ApplyDesignAsOwner();
    }

    private void ApplyDesignAsOwner()
    {
        if (!_pendingApply) return;
        _pendingApply = false;

        ShipDesignData = EncodeDesign();

        VRCPlayerApi local = Networking.LocalPlayer;
        if (Utilities.IsValid(local))
            DesignAuthor = local.displayName;

        RequestSerialization();
        ApplyVisualGrid();
        RefreshStatus();

        Notify("SHIP DESIGNER: DESIGN APPLIED");

        // Wait for the write to flush before returning ownership so a
        // stale owner does not re-serialize the previous design.
        SendCustomEventDelayedSeconds("HandBackOwnership", 1f);
    }

    public void HandBackOwnership()
    {
        if (!Networking.IsOwner(gameObject)) return;

        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;

        VRCPlayerApi master = GetMasterPlayer();
        if (master == null) return;
        if (master.playerId == local.playerId) return;

        Networking.SetOwner(master, gameObject);
    }

    private VRCPlayerApi GetMasterPlayer()
    {
        VRCPlayerApi[] players = VRCPlayerApi.GetPlayers();
        if (players == null) return null;

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].isMaster)
                return players[i];
        }
        return null;
    }

    // ============================================================
    // ENCODING ("cell:module:rotation;" separated)
    // ============================================================
    private string EncodeDesign()
    {
        string data = "";
        int placed = 0;

        for (int i = 0; i < _gridSize; i++)
        {
            if (_cellModules[i] < 0) continue;

            data += i.ToString() + ":" + _cellModules[i].ToString() + ":" + _cellRotations[i].ToString() + ";";
            placed++;
            if (placed >= maxModulesPlaced) break;
        }
        return data;
    }

    private void ParseDesign(string data)
    {
        if (_cellModules == null || _cellRotations == null) return;
        if (MODULE_NAMES == null || MODULE_NAMES.Length == 0) return;

        for (int i = 0; i < _gridSize; i++)
        {
            _cellModules[i] = -1;
            _cellRotations[i] = 0;
        }

        if (data == null || data == "") return;

        string[] tokens = data.Split(';');
        for (int t = 0; t < tokens.Length; t++)
        {
            if (tokens[t] == "") continue;

            string[] parts = tokens[t].Split(':');
            if (parts.Length < 3) continue;

            int cell = int.Parse(parts[0]);
            int module = int.Parse(parts[1]);
            int rotation = int.Parse(parts[2]);

            if (cell < 0 || cell >= _gridSize) continue;
            if (module < 0 || module >= MODULE_NAMES.Length) continue;

            _cellModules[cell] = module;
            _cellRotations[cell] = rotation % 4;
        }
    }

    // ============================================================
    // VISUALS
    // ============================================================
    private void ApplyVisualGrid()
    {
        if (cellVisualRoots == null) return;
        if (MODULE_NAMES == null) return;

        for (int i = 0; i < _gridSize; i++)
        {
            if (cellVisualRoots[i] == null) continue;

            Transform root = cellVisualRoots[i].transform;
            int module = _cellModules[i];
            int rotation = _cellRotations[i];

            for (int c = 0; c < root.childCount; c++)
            {
                GameObject child = root.GetChild(c).gameObject;
                bool isActive = (c == module);

                child.SetActive(isActive);
                if (isActive)
                    child.transform.localRotation = Quaternion.Euler(0f, rotation * 90f, 0f);
            }
        }
    }

    // ============================================================
    // READOUTS
    // ============================================================
    private void RefreshStatus()
    {
        UpdateGridText();
    }

    private void UpdateGridText()
    {
        string map = "";
        for (int row = 0; row < gridRows; row++)
        {
            string line = "";
            for (int col = 0; col < gridCols; col++)
            {
                int cell = row * gridCols + col;
                string symbol = ".";

                if (cell >= 0 && cell < _gridSize)
                {
                    int m = _cellModules[cell];
                    if (m >= 0) symbol = m.ToString();
                }

                line += symbol;
                if (col < gridCols - 1) line += " ";
            }

            map += line;
            if (row < gridRows - 1) map += "\n";
        }
    }

    private void Notify(string message)
    {
        if (hudManager != null)
            hudManager.ShowNotification(message);
    }

    private int CountPlaced()
    {
        int count = 0;
        for (int i = 0; i < _gridSize; i++)
        {
            if (_cellModules[i] >= 0) count++;
        }
        return count;
    }

    // ============================================================
    // ACCESSORS (for console UI / HUD)
    // ============================================================
    public bool IsEditing() { return _isEditing; }
    public int GetActiveModule() { return _activeModule; }
    public int GetEditMode() { return _editMode; }
    public int GetSelectedCell() { return _selectedCell; }
    public int GetGridCols() { return gridCols; }
    public int GetGridRows() { return gridRows; }
    public int GetGridSize() { return _gridSize; }
    public string GetDesignAuthor()
    {
        if (_designAuthor == "") return "AUTOPILOT";
        return _designAuthor;
    }

    public string GetActiveModuleName()
    {
        if (_activeModule < 0 || _activeModule >= MODULE_NAMES.Length) return "NONE";
        return MODULE_NAMES[_activeModule];
    }

    public string GetModuleName(int module)
    {
        if (module < 0 || module >= MODULE_NAMES.Length) return "NONE";
        return MODULE_NAMES[module];
    }

    public int GetModuleAt(int cell)
    {
        if (cell < 0 || cell >= _gridSize) return -1;
        return _cellModules[cell];
    }

    public int GetRotationAt(int cell)
    {
        if (cell < 0 || cell >= _gridSize) return 0;
        return _cellRotations[cell];
    }
}
