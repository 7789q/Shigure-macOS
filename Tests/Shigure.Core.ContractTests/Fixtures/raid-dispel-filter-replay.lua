-- Execute the production member-container constructor against a recording UI.
-- The native RAID filter, rather than the debuff's type alone, owns eligibility.
local path = arg[1] or "Fuyutsui/core/block.lua"
local file = assert(io.open(path, "r"))
local source = file:read("*a")
file:close()
local first = assert(source:find("local function GroupDispelFilter(", 1, true))
local last = assert(source:find("\n--- 过场后重绑全部光环槽", first, true))
local captured
local released = 0
groupAuraContainers = {}
local raid = true
Fuyutsui = {
    state = { classId = 2, specIndex = 1 },
    IsRaidGroup = function() return raid end,
}
UIParent = {}
AURA_DURATION_STRATA, AURA_DURATION_LEVEL, BLOCK_FIX_COUNT = "HIGH", 20, 520
AuraContainerSortMethod, AuraContainerSortDirection = { Expiration = 1 }, { Normal = 1 }
function EnsureAuraContainerLoaded() end
function GroupAuraPixelIndex() return 291 end
function MakeDispelSlotInitializer() return function() end end
function ReleaseFrame() released = released + 1 end
function CollectGroupAuraDefs() return {} end
function CreateFrame()
    return {
        SetPoint = function() end, SetEnabled = function() end,
        SetFrameStrata = function() end, SetFrameLevel = function() end,
        SetUnit = function() end, Show = function() end, Hide = function() end,
        AddAuraSlot = function(_, key, filter, options)
            captured = { key = key, filter = filter, options = options }
        end,
    }
end
local create = assert(loadstring(source:sub(first, last - 1) .. "\nreturn CreateGroupMemberAuraContainer"))()
local capabilities = { Magic = true, Poison = true, Disease = true }
function CopyIncludeDispelTypes() return capabilities end
create(16, { dispel = 3 }, {}, capabilities)
assert(captured.filter == "HARMFUL|RAID", "raid Holy Paladin must ask Blizzard for player-dispellable debuffs")
assert(captured.options.candidateFilters.includeDispelTypes == capabilities, "learned dispel capability filter remains intact")
raid = false
create(16, { dispel = 3 }, {}, capabilities)
assert(captured.filter == "HARMFUL", "existing party dispel behavior is outside this raid fix")
raid = true
Fuyutsui.state.classId = 6
create(16, { dispel = 3 }, {}, capabilities)
assert(captured.filter == "HARMFUL", "other classes retain their existing filter")
Fuyutsui.state.classId = 2
Fuyutsui.blocks = { groups = { start = 123, num = 11, dispel = 3 } }
Fuyutsui.groupList, Fuyutsui.group = { "player" }, { player = { index = 1 } }
raid = false
Fuyutsui:RefreshGroupAuraContainers()
assert(groupAuraContainers[1].fuyutsuiDispelSlot.filter == "HARMFUL", "party container initialized")
raid = true
Fuyutsui:RefreshGroupAuraContainers()
assert(released == 1 and groupAuraContainers[1].fuyutsuiDispelSlot.filter == "HARMFUL|RAID",
    "joining a raid replaces stale party filters on reused member slots")
raid = false
Fuyutsui:RefreshGroupAuraContainers()
assert(released == 2 and groupAuraContainers[1].fuyutsuiDispelSlot.filter == "HARMFUL",
    "leaving the raid restores the original party filter")
print("Raid dispel filter production Lua replay passed")
