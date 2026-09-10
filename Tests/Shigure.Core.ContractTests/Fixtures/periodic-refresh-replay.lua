-- Execute production Lua with restricted API values and injected refresh failures.
local root = arg[1] or "."
local chargeCalls, chargeInfo = 0, nil
local rangeResult, measuredRange = nil, 5
local secret = newproxy(true)
getmetatable(secret).__lt = function() error("attempt to compare a secret number") end
function issecretvalue(value) return rawequal(value, secret) end
function GetTime() return 100 end
function CreateColor() return {} end
function LibStub()
    return { GetRange = function() return 0, measuredRange end }
end
C_Spell = {
    GetSpellCharges = function(id)
        assert(id == 50842)
        chargeCalls = chargeCalls + 1
        return chargeInfo
    end,
    IsSpellInRange = function() return rangeResult end,
}
C_SpellBook = {}
C_CurveUtil = {
    CreateColorCurve = function() return { SetType = function() end } end,
    EvaluateColorFromBoolean = function()
        error("restricted range must not be decoded into a Lua comparison")
    end,
}
Enum = { LuaCurveType = { Step = 1 } }
Fuyutsui = {
    state = { classId = 2, specIndex = 1 },
    target = {}, focus = {}, mouseover = {}, boss = {}, nameplate = {},
    UpdateStateBlock = function() end,
    GetEstimatedGCDSeconds = function() return 1.5 end,
}
assert(loadfile(root .. "/Fuyutsui/core/spells.lua"))("Fuyutsui", {})
assert(loadfile(root .. "/Fuyutsui/core/target.lua"))("Fuyutsui", {})
assert(loadfile(root .. "/Fuyutsui/core/events.lua"))("Fuyutsui", {})
for _, method in ipairs({
    "UpdateBloodBoilAutomationState", "UpdatePlayerCastBlocks",
    "UpdatePlayerStationaryDuration", "UpdateUnitCastingOrChannelingInfo",
    "UpdateGroupInRangeAndHealth", "UpdatePlayerCombat", "UpdatePlayerCombatTime",
    "UpdatePlayerAssistant", "UpdateRune", "UpdateTargetFullInfo",
    "UpdateEnemyCount", "UpdateItemCooldown", "UpdateKnightStatusCount",
}) do
    Fuyutsui[method] = function() end
end

local failures = 0
local function test(name, run)
    local ok, err = pcall(run)
    if ok then
        print("PASS " .. name)
    else
        failures = failures + 1
        print("FAIL " .. name .. ": " .. tostring(err))
    end
end
local function reset(classId, specIndex)
    Fuyutsui.state.classId, Fuyutsui.state.specIndex = classId, specIndex
    Fuyutsui.state.protocolHeartbeat = 0
    Fuyutsui.state.bloodBoilChargeRemaining = nil
    Fuyutsui.state.bloodBoilNearCap = nil
    Fuyutsui.timeElapsed, Fuyutsui.timeElapsed1 = 0, 0
    Fuyutsui.target.maxRange = 25
    chargeCalls, chargeInfo = 0, nil
    rangeResult, measuredRange = nil, 5
end

test("Holy Paladin refresh never queries Blood Boil and updates stale distance", function()
    reset(2, 1)
    chargeInfo = { currentCharges = secret, maxCharges = 2 }
    Fuyutsui:OnUpdate(0.21)
    assert(chargeCalls == 0, "Holy Paladin queried a DK spell")
    assert(Fuyutsui.target.maxRange == 5, "distance stayed at 25")
    assert(Fuyutsui.state.protocolHeartbeat == 1)
end)

test("all other class/spec combinations skip Blood Boil", function()
    for _, identity in ipairs({ {1, 1}, {2, 2}, {6, 2}, {6, 3} }) do
        reset(identity[1], identity[2])
        Fuyutsui:UpdateBloodBoilChargeRemaining()
        assert(chargeCalls == 0)
    end
end)

test("Blood DK restricted charge fields keep conservative defaults", function()
    for _, field in ipairs({ "currentCharges", "maxCharges", "cooldownStartTime",
        "cooldownDuration", "chargeModRate" }) do
        reset(6, 1)
        chargeInfo = { currentCharges = 1, maxCharges = 2,
            cooldownStartTime = 90, cooldownDuration = 11, chargeModRate = 1 }
        chargeInfo[field] = secret
        Fuyutsui:OnUpdate(0.21)
        assert(Fuyutsui.state.bloodBoilChargeRemaining == 3, field)
        assert(Fuyutsui.state.bloodBoilNearCap == false, field)
        assert(Fuyutsui.target.maxRange == 5, field .. " interrupted range update")
    end
end)

test("Blood DK public charge timing still detects near cap", function()
    reset(6, 1)
    chargeInfo = { currentCharges = 1, maxCharges = 2,
        cooldownStartTime = 90, cooldownDuration = 11, chargeModRate = 1 }
    Fuyutsui:UpdateBloodBoilChargeRemaining()
    assert(Fuyutsui.state.bloodBoilChargeRemaining == 1)
    assert(Fuyutsui.state.bloodBoilNearCap == true)
end)

test("failed refresh never publishes a healthy heartbeat or retries every frame", function()
    reset(2, 1)
    local original = Fuyutsui.UpdateSpellCooldown
    local calls = 0
    Fuyutsui.UpdateSpellCooldown = function()
        calls = calls + 1
        error("injected cooldown failure")
    end
    local ok = pcall(Fuyutsui.OnUpdate, Fuyutsui, 0.21)
    local heartbeat = Fuyutsui.state.protocolHeartbeat
    pcall(Fuyutsui.OnUpdate, Fuyutsui, 0.01)
    Fuyutsui.UpdateSpellCooldown = original
    assert(not ok, "error was swallowed")
    assert(heartbeat == 0, "heartbeat advanced before a complete refresh")
    assert(calls == 1, "failed refresh retried at rendering frame rate")
    Fuyutsui:OnUpdate(0.21)
    assert(Fuyutsui.state.protocolHeartbeat == 1)
    assert(Fuyutsui.target.maxRange == 5)
end)

test("range true, false and unavailable results remain distinct", function()
    reset(2, 1)
    measuredRange, rangeResult = 25, true
    Fuyutsui:UpdateUnitRangeBlock("target")
    assert(Fuyutsui.target.maxRange == 5)
    measuredRange, rangeResult = 5, false
    Fuyutsui:UpdateUnitRangeBlock("target")
    assert(Fuyutsui.target.maxRange > 5)
    measuredRange, rangeResult = 25, nil
    Fuyutsui:UpdateUnitRangeBlock("target")
    assert(Fuyutsui.target.maxRange == 25)
    rangeResult = secret
    Fuyutsui:UpdateUnitRangeBlock("target")
    assert(Fuyutsui.target.maxRange == 25)
end)

assert(failures == 0, tostring(failures) .. " periodic refresh replay failures")
print("Periodic refresh production Lua replay passed")
