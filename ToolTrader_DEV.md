# ToolTrader — Developer Handoff

Technical breakdown of how ToolTrader works. Written as a reference for developers
who want to understand or reimplement the approach in another project.

---

## What this solves

SPT trader mods require authors to hand-write BSG-format JSON to define trader
inventory and quests. ToolTrader replaces that with an in-game UI: hover over any
item in your inventory, press a key, and it writes to the trader's assort.json
automatically in the correct format. Quests are configured through a UI and written
out as valid BSG quest objects.

The system is split across two DLLs with different runtimes. They communicate
purely through data files on disk. Neither DLL calls the other directly.

---

## Architecture — Two-DLL Model

```
[ Game Session ]
    ToolTrader.dll (BepInEx / .NET 4.7.2)
        Harmony patch captures hovered item
        ItemGrabber builds entry list
        Unity IMGUI window — 4 tabs
        AssortWriter / QuestWriter serialise to disk
              |
              |  writes data files
              v
    data/
        assort.json
        quests.json
        questassort.json
        custom_locales.json   <-- created on first quest write

[ SPT Server Startup ]
    TheButcher.dll (SPT Server mod / .NET 9.0)
        IOnLoad fires at startup (priority 500000)
        Loads base.json → registers trader
        Loads assort.json → overwrites trader assort
        Loads quests.json → adds quests to SPT database
        Builds locale stubs from QuestName field
        Conditionally loads custom_locales.json (if it exists)
        Injects all locale strings via AddTransformer()
```

The data files are the only interface between the two DLLs. This means you can
inspect or edit the files manually, and either DLL can be replaced independently.

---

## Data Files

Four files live in the trader mod's `data/` folder.

| File | Ships as | Written by | Read by |
|------|----------|------------|---------|
| `assort.json` | `{"items":[],"barter_scheme":{},"loyal_level_items":{}}` | AssortWriter | TheButcher |
| `quests.json` | `{}` | QuestWriter | TheButcher |
| `questassort.json` | `{"started":{},"success":{},"fail":{}}` | QuestWriter | TheButcher |
| `custom_locales.json` | Does not ship | QuestWriter | TheButcher |

Shipping files as empty-but-valid JSON is intentional. TheButcher always has
something to load even before ToolTrader has written anything. No null checks,
no missing-file errors on first install.

---

## Hover-Capture Pipeline

This is the core feature. Three components chain together.

### Step 1 — Harmony patch on ItemView

A `HarmonyPostfix` fires every time the cursor enters an item slot in the inventory
UI. `ItemView` is EFT's UI component for a single item slot. The patch reads the
underlying `Item` object and writes three values to a static holder.

```csharp
[HarmonyPatch(typeof(ItemView), "OnPointerEnter")]
class ItemViewPatch {
    static void Postfix(ItemView __instance) {
        var item = __instance.Item;
        HoverTracker.LastHoveredItem     = item;
        HoverTracker.LastHoveredItemTpl  = item?.TemplateId;
        HoverTracker.LastHoveredItemName = item?.ShortName?.Localized();
    }
}
```

### Step 2 — Static holder

`HoverTracker` is a plain static class. The UI reads from it when the player
presses the capture keybind.

```csharp
static class HoverTracker {
    public static Item   LastHoveredItem;
    public static string LastHoveredItemTpl;
    public static string LastHoveredItemName;
}
```

### Step 3 — ItemGrabber tree builder

`BuildItemTree()` takes the `Item` from HoverTracker and produces a flat list of
`GrabbedItemEntry` objects. Root item gets `parentId = "hideout"`. Each child
references its parent by the generated ID. This mirrors BSG's assort item format
exactly — no translation needed at write time.

```csharp
public static List<GrabbedItemEntry> BuildItemTree(Item root) {
    var entries = new List<GrabbedItemEntry>();
    var rootId  = GenerateMongoId();

    entries.Add(new GrabbedItemEntry {
        Id          = rootId,
        Tpl         = root.TemplateId,
        DisplayName = root.ShortName?.Localized(),
        ParentId    = "hideout",
        SlotId      = "hideout",
        IsRoot      = true
    });

    if (root is CompoundItem compound) {
        foreach (var slot in compound.Slots) {
            if (slot.ContainedItem != null)
                RecurseItem(slot.ContainedItem, rootId, slot.Id, entries);
        }
    }
    return entries;
}

// RecurseItem follows the same pattern: generates a child ID,
// sets ParentId = callerRootId, then recurses into its own slots.
```

### GrabbedItemEntry model

```csharp
class GrabbedItemEntry {
    public string Id;
    public string Tpl;
    public string DisplayName;
    public string ParentId;
    public string SlotId;
    public bool   IsRoot;
}
```

### MongoId generation

SPT and BSG use 24-character lowercase hex IDs: 8 hex chars of Unix timestamp
followed by 16 hex chars of random bytes. Any ID in this format is accepted by
SPT's database layer without special registration.

```csharp
private static string GenerateMongoId() {
    var ts    = ((int)(DateTime.UtcNow - new DateTime(1970,1,1)).TotalSeconds)
                  .ToString("x8");
    var bytes = new byte[8];
    new Random().NextBytes(bytes);
    return ts + BitConverter.ToString(bytes).Replace("-", "").ToLower();
}
```

> **Known issue:** `new Random()` per call seeds from the current tick count.
> Two calls within the same millisecond produce identical IDs. Fix: use a
> `private static readonly Random _rng = new Random()` field instead.

---

## Writing to assort.json

`AssortWriter.AddItem()` takes the flat entry list from ItemGrabber and writes
three sections of assort.json simultaneously. All three must stay consistent.

```json
{
  "items": [
    {
      "_id":      "abc123def456789012345678",
      "_tpl":     "590c678286f77426c9660122",
      "parentId": "hideout",
      "slotId":   "hideout",
      "upd": {
        "UnlimitedCount":    false,
        "StackObjectsCount": 1
      }
    },
    {
      "_id":      "def456abc123789012345678",
      "_tpl":     "572b7adb24597762ae139821",
      "parentId": "abc123def456789012345678",
      "slotId":   "mod_pistol_grip",
      "upd": {}
    }
  ],

  "barter_scheme": {
    "abc123def456789012345678": [[
      { "count": 1, "_tpl": "5449016a4bdc2d6f028b456f" }
    ]]
  },

  "loyal_level_items": {
    "abc123def456789012345678": 1
  }
}
```

Children are **never** keyed in `barter_scheme` or `loyal_level_items` — only
root item IDs appear there. SPT resolves the full item tree from the `parentId`
chain in the items array.

**Dedup check:** Before adding, AssortWriter checks whether any existing root
item already has the same `_tpl`. If it does, the add is rejected to prevent
duplicate templates in the trader's inventory.

**Remove:** `RemoveItem()` calls `CollectDescendants()` — a recursive walk of
the items array following the `parentId` chain — to find all IDs to delete.
It then removes every matching entry from all three sections atomically.

---

## Quest Construction

Quests are written in BSG's native format. SPT loads them the same way it loads
official quests from the base game data.

### BSG quest structure

```json
{
  "questId": {
    "_id":       "questId",
    "QuestName": "Kill 10 Scavs on Customs",
    "traderId":  "6617beeaa6d9b8a42e001337",
    "type":      "Elimination",
    "location":  "bigmap",

    "conditions": {
      "AvailableForFinish": [ ],
      "AvailableForStart":  [ ],
      "Fail": []
    },

    "rewards": {
      "Success": [ ],
      "Started": [],
      "Fail": []
    },

    "startedMessageText":  "questId startedMessageText",
    "successMessageText":  "questId successMessageText",
    "description":         "questId description"
  }
}
```

> `QuestName` is not a BSG field. BSG and SPT ignore unknown fields in quest
> objects. See the "QuestName Interface Contract" section below.

### Condition types

| UI type | BSG _parent | Key _props fields | Notes |
|---------|-------------|-------------------|-------|
| Kill enemies | `CounterCreator` wrapping `Kills` | `target`, `value`, `location` | CounterCreator is the outer; Kills is a nested counter sub-condition |
| Kill enemies (grouped) | Multiple `CounterCreator` conditions | Same as kill, one per enemy type | Each enemy type gets its own condition ID |
| Transit / survive | `CounterCreator` wrapping `ExitStatus` | `status: ["Survived"]`, `location` | ExitStatus checks how the player left the raid |
| Prerequisite quest | `Quest` | `id` (quest ID), `status: [4]` | Status 4 = "Success" — unlocks when prior quest is complete |
| Quest-gated item | N/A — questassort.json | assortId → questId | Not a quest condition; gates a trader item behind completion |

### Kill condition — full BSG structure

```json
{
  "_parent": "CounterCreator",
  "_props": {
    "id":    "conditionId",
    "value": 10,
    "counter": {
      "id": "counterId",
      "conditions": [
        {
          "_parent": "Kills",
          "_props": {
            "target":     ["Savage"],
            "value":      10,
            "savageRole": ["assault"],
            "weapon":     [],
            "distance": {
              "compareMethod": ">=",
              "value": 50
            }
          }
        },
        {
          "_parent": "Location",
          "_props": { "target": ["bigmap"] }
        }
      ]
    }
  }
}
```

The `Location` sub-condition is a **sibling** of `Kills` inside the
`counter.conditions` array, not nested inside it. Both must be present for a
map-restricted kill quest.

Omit `distance` if no minimum range requirement. Omit `savageRole` for PMC and
boss targets. Omit the `Location` condition entirely for any-map quests.

### Survive / transit condition

```json
{
  "_parent": "CounterCreator",
  "_props": {
    "id":    "conditionId",
    "value": 2,
    "counter": {
      "id": "counterId",
      "conditions": [
        {
          "_parent": "ExitStatus",
          "_props": { "status": ["Survived"] }
        },
        {
          "_parent": "Location",
          "_props": { "target": ["factory4_day"] }
        }
      ]
    }
  }
}
```

### Prerequisite quest condition

```json
{
  "_parent": "Quest",
  "_props": {
    "id":     "prerequisiteQuestId",
    "status": [4]
  }
}
```

Goes in `conditions.AvailableForStart`. Status 4 = quest completed.

### Enemy type values

| Display name | BSG target value | savageRole |
|-------------|-----------------|------------|
| Scav | `Savage` | `assault` |
| Raider | `pmcBot` | `pmcBot` |
| Rogue | `exUsec` | `exUsec` |
| Cultist Priest | `sectantPriest` | `sectantPriest` |
| Cultist Warrior | `sectantWarrior` | `sectantWarrior` |
| Bear (PMC) | `AnyPmc` | — |
| USEC (PMC) | `AnyPmc` | — |
| Reshala | `bossBully` | `bossBully` |
| Killa | `bossKilla` | `bossKilla` |
| Shturman | `bossKojaniy` | `bossKojaniy` |
| Sanitar | `bossSanitar` | `bossSanitar` |
| Tagilla | `bossTagilla` | `bossTagilla` |
| Gluhar | `bossGluhar` | `bossGluhar` |
| Zryachiy | `bossZryachiy` | `bossZryachiy` |
| Kaban | `bossBoar` | `bossBoar` |
| Partisan | `bossPartisan` | `bossPartisan` |
| Kolontay | `bossKolontay` | `bossKolontay` |
| Knight | `bossKnight` | `bossKnight` |

### Map ID values

| Display name | BSG location value |
|-------------|-------------------|
| Customs | `bigmap` |
| Woods | `Woods` |
| Factory (day) | `factory4_day` |
| Factory (night) | `factory4_night` |
| Interchange | `Interchange` |
| Shoreline | `Shoreline` |
| Reserve | `RezervBase` |
| Labs | `laboratory` |
| Lighthouse | `Lighthouse` |
| Streets | `TarkovStreets` |
| Ground Zero | `sandbox` |

---

## The QuestName Interface Contract

Every quest object written to quests.json contains a `QuestName` field that is
not part of BSG's schema. BSG and SPT both ignore unknown fields in quest objects,
so it is safe to include.

Its purpose is cross-mod communication:

- **ToolTrader writes it** as a human-readable label when creating a quest
- **TheButcher.dll reads it** on startup to generate locale display strings
- **ToolTrader also uses it** in the UI dropdown when selecting a prerequisite quest

```csharp
// TheButcher.dll reads QuestName from each quest:
foreach (var kvp in quests) {
    var name = kvp.Value.QuestName ?? kvp.Key;  // falls back to ID if missing
    locales[$"{kvp.Key} name"]              = name;
    locales[$"{kvp.Key} description"]       = name;
    locales[$"{kvp.Key} successMessageText"] = "Well done.";
    locales[$"{kvp.Key} startedMessageText"] = "Accepted.";
}
```

Without `QuestName`, the quest would appear in-game with its hex ID as the display
name. The fallback to `kvp.Key` ensures it still works, just unreadably.

---

## Locale System (Layered)

Locale strings are applied in two layers. TheButcher generates minimal stubs at
startup from the data already in quests.json. ToolTrader's custom_locales.json
then overlays richer strings whenever quests are created or edited.

### Layer 1 — Stubs from BuildQuestLocales()

Generated from `QuestName` for every quest. These are functional but generic.
They allow quests to appear in-game with readable text even if ToolTrader has
never been run.

### Layer 2 — custom_locales.json overlay

Written by `QuestWriter.WriteLocaleEntries()`. Contains condition-level
description strings. Keys that exist here override the Layer 1 stubs for the
same ID.

```json
{
  "questId description":          "Kill 10 Scavs on Customs.",
  "questId successMessageText":   "Good work. Come back when you need more.",
  "questId startedMessageText":   "Get to it.",
  "condId failMessageText":       "Kill 10 Scavs on Customs."
}
```

### AddTransformer — SPT locale injection

`AddTransformer()` hooks into SPT's locale loading pipeline and runs a callback
for each language object as it is built. All strings added this way are applied
to all languages. Since ToolTrader writes English only, all language clients will
see the English strings.

```csharp
// TheButcher.dll — conditional load, safe on first install
if (File.Exists(customLocalesPath)) {
    var entries = ModHelper.GetJsonDataFromFile<Dictionary<string, string>>(customLocalesPath);
    _localeService.AddTransformer(locale => {
        foreach (var kvp in entries)
            locale[kvp.Key] = kvp.Value;
    });
}
```

---

## Quest-Gated Trader Items

An assort item can be locked behind quest completion by adding an entry to
questassort.json.

```json
{
  "started": {},
  "success": {
    "abc123def456789012345678": ["questId"]
  },
  "fail": {}
}
```

The key is the assort item's root ID. The value is a list of quest IDs that must
all be in "success" state before the item appears in the trader's shop.

`QuestWriter.LinkQuestReward()` writes this mapping when a reward item is linked
to a quest. The assort item must already exist in assort.json — the questassort
entry only controls access to it.

---

## Buy-Restriction Patches

Two Harmony patches work together to toggle unlimited stock. Both read from a
static property on the window class.

```csharp
// Fires when TraderAssortmentControllerClass is constructed
[HarmonyPatch(typeof(TraderAssortmentControllerClass), MethodType.Constructor)]
static void Postfix(TraderAssortmentControllerClass __instance) {
    if (ToolTraderWindow.BuyInfinite)
        __instance.BuyRestrictionMax = 0;
}

// Fires when SPT tries to update CurrentQuantity (restock system)
[HarmonyPatch(typeof(TraderAssortmentControllerClass), "set_CurrentQuantity")]
static bool Prefix(TraderAssortmentControllerClass __instance) {
    if (ToolTraderWindow.BuyInfinite) {
        __instance.BuyRestrictionMax = 0;  // re-zero after restock
        return false;                       // skip original setter
    }
    return true;
}
```

> If reimplementing this separately, have the patches read from a shared config
> class rather than the window class directly.

---

## Key Constants

| Constant | Value | Used in |
|----------|-------|---------|
| Trader ID | `6617beeaa6d9b8a42e001337` | base.json, QuestWriter, TheButcherConstants — must match in all three |
| RUB template | `5449016a4bdc2d6f028b456f` | barter_scheme entries |
| USD template | `5696686a4bdc2da3298b456a` | barter_scheme entries |
| EUR template | `569668774bdc2da2298b4568` | barter_scheme entries |
| Hideout parent | `"hideout"` | parentId and slotId for all root assort items |
| Quest success status | `[4]` | Quest condition _props.status |
| MongoId length | 24 hex chars | 8-char timestamp + 16-char random |

---

## Known Issues / Improvement Notes

These are limitations in the current implementation worth addressing if
reimplementing or building on top of this approach.

1. **Random seeding in MongoId generation** — `new Random()` per call can
   produce duplicate IDs if called twice in the same millisecond. Use a
   `private static readonly Random _rng = new Random()` field.

2. **Duplicated quest construction block** — the BSG JObject construction in
   QuestWriter.cs is copy-pasted across four methods (`CreateQuest`,
   `CreateKillQuest`, `CreateTransitQuest`, `CreateGroupKillQuest`). Extract
   into a shared `BuildBaseQuestObject()` helper.

3. **English-only locale strings** — all condition description strings are
   hardcoded English. A locale config layer would be needed for multi-language
   support.

4. **Hardcoded RUB currency** — AssortWriter always writes RUB as the barter
   currency. A currency parameter would enable USD/EUR support.

5. **Tight coupling between patches and UI class** — `TraderUnlimitedPatch`
   reads `ToolTraderWindow.BuyInfinite` directly. Should read from a shared
   config class instead.

---

## Server-Side DLL Tech Stack

TheButcher.dll uses SPT's C# server SDK, not TypeScript.

- Runtime: `.NET 9.0`
- DI: `[Injectable(InjectionType.Singleton)]` attribute-based
- Lifecycle: `IOnLoad` interface with priority value for load ordering
- JSON loading: `ModHelper.GetJsonDataFromFile<T>()` — typed deserialisation
- Database access: `DatabaseService` injected via constructor
- Locale injection: `ILocaleService.AddTransformer()`
- Trader registration: `ITraderHelper` — `AddTrader()`, `OverwriteTraderAssort()`

---

*ToolTrader was originally two separate mods (EgosAssorts + TheButcher) that were
later combined into a single release package. The two-DLL split reflects that
history and the hard runtime boundary between BepInEx (Unity) and SPT (server).*
