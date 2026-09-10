-- Exercise the production schema loader and forecast writer together.
local root = arg[1] or "."
function GetTime() return 100 end
function issecretvalue() return false end
C_UnitAuras = { GetAuraDataBySpellId = function() return nil end }
local writes = {}
Fuyutsui = {
    state = {}, roleMap = {},
    groupList = { "player", "party1", "party2" },
    group = {},
    UpdateStateBlock = function() end,
    CreateTexture = function(_, index, value) writes[index] = value end,
    ClassBlocks = { [1] = { group = {
        num = 11, healthPercent = 1, role = 2, dispel = 3, canHeal = 11,
        expectedNeed = 7, burstNeed = 8, sustainNeed = 9, aura = {},
    } } },
}
assert(loadfile(root .. "/Fuyutsui/main.lua"))("Fuyutsui", {})
assert(loadfile(root .. "/Fuyutsui/core/group.lua"))("Fuyutsui", {})
Fuyutsui:LoadPlayerBlocks(1)
for i, unit in ipairs(Fuyutsui.groupList) do
    Fuyutsui.group[unit] = { valid = true, index = i, role = "DAMAGER", healthPercentValue = 1 }
end
Fuyutsui.group.player.healthPercentValue = 0.5
Fuyutsui:UpdateHolyPaladinForecast()
local base = Fuyutsui.blocks.groups.start
assert(writes[base + 7] and writes[base + 7] > 0, "expected need must reach its declared pixel")
assert(writes[base + 8] and writes[base + 8] > 0, "burst need must reach its declared pixel")
assert(writes[base + 9] and writes[base + 9] > 0, "sustain need must reach its declared pixel")
-- A reservation can retain resources, but cannot create injured group members.
Fuyutsui.state.aoeEventType, Fuyutsui.state.aoeEventStage = 2, 1
Fuyutsui:UpdateHolyPaladinForecast()
assert(Fuyutsui.state.spreadCount == 1, "an absorb warning must not fabricate group pressure")
print("Holy Paladin forecast production Lua replay passed")
