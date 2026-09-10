-- Production health curves, group refresh and UNIT_HEALTH handling.
local root = arg[1] or "."
local writes, reads, health = {}, {}, {}
local raid, hidden = true, false
local secret = newproxy(true)
getmetatable(secret).__mul = function() error("secret health must not be used in arithmetic") end
function issecretvalue(value) return rawequal(value, secret) end
function GetTime() return 100 end
function CreateColor(r, g, b) return { GetRGB = function() return r, g, b end } end
Enum = { LuaCurveType = { Linear = 1 } }
C_CurveUtil = { CreateColorCurve = function()
    local points = {}
    return {
        SetType = function() end,
        AddPoint = function(_, x, color) local _, _, b = color:GetRGB(); points[x] = b end,
        Evaluate = function(_, x)
            local keys = {}; for key in pairs(points) do keys[#keys + 1] = key end; table.sort(keys)
            for i = 2, #keys do
                local left, right = keys[i - 1], keys[i]
                if x <= right then return points[left] + (points[right] - points[left]) * (x - left) / (right - left) end
            end
        end,
    }
end }
C_UnitAuras = { GetAuraDataBySpellId = function() return nil end }
function UnitHealthPercent(unit, _, curve)
    reads[unit] = (reads[unit] or 0) + 1
    return CreateColor(0, 0, hidden and secret or curve:Evaluate(health[unit] / 100))
end
function UnitIsDeadOrGhost(unit) return health[unit] == 0 end
function UnitCanAssist() return true end
Fuyutsui = {
    state = { classId = 2, specIndex = 1, castTargetUnit = "raid2" },
    EnumPowerType = {}, roleMap = { DAMAGER = 5 }, group = {}, groupList = {},
    blocks = { groups = { start = 123, num = 11, healthPercent = 1, role = 2, canHeal = 11 } },
    CreateTexture = function(_, index, value) writes[index] = value end,
    UpdateStateBlock = function() end,
    GetUnitRange = function() return 0, 40 end,
    IsRaidGroup = function() return raid end,
}
for i = 1, 30 do
    local unit = "raid" .. i
    health[unit] = 100
    Fuyutsui.groupList[i] = unit
    Fuyutsui.group[unit] = { index = i, valid = true, inSight = true, role = "DAMAGER", inComingHeals = 0 }
end
assert(loadfile(root .. "/Fuyutsui/core/curves.lua"))("Fuyutsui", {})
assert(loadfile(root .. "/Fuyutsui/core/group.lua"))("Fuyutsui", {})
assert(loadfile(root .. "/Fuyutsui/core/events.lua"))("Fuyutsui", {})
local function pixel(slot) return math.floor(writes[123 + (slot - 1) * 11 + 1] * 255 + 0.5) end

health.raid2 = 70
for _, spell in ipairs({ 19750, 82326 }) do
    Fuyutsui:ApplyIncomingHealsCurve(spell)
    Fuyutsui:UpdateUnitHealthInfo("raid2")
    assert(pixel(2) == 70, "pending cast " .. spell .. " must not report healing before it happens")
    assert(math.abs(Fuyutsui.group.raid2.healthPercentValue - 0.70) < 0.005,
        "forecast health must decode the same 0-255 pixel scale")
end
Fuyutsui:UpdateAllIncomingHealsCurves()
reads = {}
for _ = 1, 4 do Fuyutsui:UpdateGroupInRangeAndHealth() end
for i = 1, 30 do assert(reads["raid" .. i], "raid fallback missed member " .. i .. " after four frames") end

reads = {}
health.raid30 = 40
Fuyutsui:UNIT_HEALTH(nil, "raid30")
assert(reads.raid30 == 1 and math.abs(pixel(30) - 40) <= 1,
    "UNIT_HEALTH must immediately publish changed member health within pixel quantization")
health.raid30 = 100
Fuyutsui:UNIT_HEALTH(nil, "raid30")
assert(pixel(30) == 100, "a healed member must immediately leave the deficit count")
hidden = true
Fuyutsui:UpdateUnitHealthInfo("raid2")
assert(writes[135] == secret, "protected health is forwarded to its pixel without Lua arithmetic")
hidden = false

reads = {}; raid = false
Fuyutsui:UpdateGroupInRangeAndHealth()
local count = 0; for _ in pairs(reads) do count = count + 1 end
assert(count == 1, "party fallback keeps its existing cadence")
reads = {}; raid = true; Fuyutsui.state.classId = 6
Fuyutsui:UpdateGroupInRangeAndHealth()
count = 0; for _ in pairs(reads) do count = count + 1 end
assert(count == 1, "other classes keep their existing cadence")
print("Raid health refresh production Lua replay passed")
