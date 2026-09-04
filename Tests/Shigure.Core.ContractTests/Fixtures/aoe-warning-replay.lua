local sourcePath = assert(arg[1], "aoewarning.lua path is required")
local now = 0
local timers = {}
local casts = {}
local eventRemaining = {}
local eventStates = {}
local absorbs = { player = 0, party1 = 0 }
local currentAbsorbUnit

function GetTime() return now end
function issecretvalue(value) return type(value) == "table" and value.secret == true end
function UnitCanAttack(_, unit) return unit ~= "player" end
function UnitGUID(unit) return "Creature-0-0-0-0-" .. unit end
function GetInstanceInfo() return nil, nil, nil, nil, nil, nil, nil, 2993 end
function IsIndoors() return false end
function UnitLevel(unit) return unit == "player" and 70 or 71 end
function UnitPowerType() return 1 end
function UnitClassification() return "elite" end
function UnitAffectingCombat() return true end
function UnitCreatureFamily() return nil, nil end
function UnitSpellTargetName() return nil end
 C_Map = { GetBestMapForUnit = function() return 2588 end }
function UnitCastingInfo(unit)
    local cast = casts[unit]
    if not cast then return nil end
    return "cast", nil, nil, cast.startedAt * 1000, cast.endsAt * 1000
end
function UnitChannelInfo(unit) return UnitCastingInfo(unit) end
local function CastDuration(unit)
    if not casts[unit] then return nil end
    return {
        EvaluateRemainingDuration = function()
            local remaining = math.max(0, casts[unit].endsAt - now)
            return { GetRGB = function() return 0, 0, math.min(1, remaining / 25.5) end }
        end,
    }
end
function UnitCastingDuration(unit) return CastDuration(unit) end
function UnitChannelDuration(unit) return CastDuration(unit) end
function GetHaste() return 0 end
function UnitGetDetailedHealPrediction(unit)
    currentAbsorbUnit = unit
end
function CreateUnitHealPredictionCalculator()
    return { GetHealAbsorbs = function() return absorbs[currentAbsorbUnit] or 0 end }
end

C_Timer = {
    After = function(_, callback) table.insert(timers, callback) end,
}
C_Spell = {
    GetSpellCooldown = function() return { duration = 1.5 } end,
}
C_EncounterTimeline = {
    GetEventTimeRemaining = function(id) return eventRemaining[id] end,
    GetEventState = function(id) return eventStates[id] end,
}
Enum = {
    EncounterTimelineEventState = { Finished = 1, Canceled = 2 },
}

Fuyutsui = {
    state = { diGuaBridgeReady = true },
    db = { char = { aoeWarningDebug = false } },
    groupList = { "player", "party1" },
    group = { player = { valid = true }, party1 = { valid = true } },
    UpdateStateBlock = function() end,
    DiGuaBridge = {
        castEventTypeBySpell = { [1306517] = 2, [777] = 1, [888] = 1, [999] = 1 },
    },
}

assert(loadfile(sourcePath))("Fuyutsui", {})
Fuyutsui:InitializeAOEWarning()

local function flush()
    while #timers > 0 do
        local pending = timers
        timers = {}
        for _, callback in ipairs(pending) do callback() end
    end
end

local function update(expectedType, expectedStage, label)
    Fuyutsui:UpdateAOEWarningState()
    assert(Fuyutsui.state.aoeEventType == expectedType,
        label .. ": expected type " .. expectedType .. ", got " .. tostring(Fuyutsui.state.aoeEventType))
    assert(Fuyutsui.state.aoeEventStage == expectedStage,
        label .. ": expected stage " .. expectedStage .. ", got " .. tostring(Fuyutsui.state.aoeEventStage))
end

local function clear()
    Fuyutsui:ClearAOEWarningEvents("test reset")
    timers = {}
    casts = {}
    eventRemaining = {}
    eventStates = {}
    absorbs.player = 0
    absorbs.party1 = 0
end

local function add(id, name, spellID, remaining)
    eventRemaining[id] = remaining
    Fuyutsui:ObserveAOETimelineEvent({
        id = id,
        overrideName = name,
        spellID = spellID,
        duration = remaining,
    })
end

local function start(unit, castGUID, spellID, duration)
    casts[unit] = { startedAt = now, endsAt = now + duration }
    Fuyutsui:ObserveAOEEnemyCast(unit, castGUID, spellID, false)
end

-- Missing Spell IDs keep ordinary AOE in reservation. An absorb timeline with
-- no observed cast must never be promoted to a Virtue window by time alone.
add(1, "准备AOE", 0, 5)
update(1, 1, "spell zero reserves")
now = 6
update(1, 1, "spell zero cannot enter execution")
now = 16
update(0, 0, "spell zero expires silently")
clear()

now = 17
add(11, "准备吸奶盾", 0, 3)
now = 21
update(2, 1, "spell zero absorb timeline waits through grace")
now = 36
update(0, 0, "spell zero absorb timeline releases public stage after fallback grace")
now = 38
update(0, 0, "spell zero absorb timeline remains released without success")
clear()

-- Ordinary AOE enters the real cast windows, interruption preserves the warning,
-- and a new cast instance can complete independently.
now = 20
add(2, "准备AOE", 777, 5)
start("nameplate1", "ordinary-a", 777, 5)
now = 24.2
update(1, 2, "ordinary final second")
Fuyutsui:FinishAOEEnemyCast("nameplate1", "ordinary-a", 777, "interrupted")
flush()
update(1, 1, "interruption returns to reservation")
start("nameplate1", "ordinary-b", 777, 2)
now = 25.2
update(1, 2, "recast uses new timing")
Fuyutsui:FinishAOEEnemyCast("nameplate1", "ordinary-b", 777, "succeeded")
flush()
update(1, 4, "ordinary success opens impact window")
clear()

-- A cast-aware absorb warning stays reserved when instanced combat does not
-- expose a matchable cast terminal event; time alone is not success.
now = 30
add(3, "准备吸奶盾", 1306517, 3)
now = 34
update(2, 1, "cast-aware absorb timeline waits for a late cast")
now = 49
update(0, 0, "cast-aware absorb timeline releases public stage after fallback grace")
now = 51
update(0, 0, "cast-aware absorb timeline remains released without success")
clear()

-- A positive heal-absorb sample without a successful cast terminal is only
-- diagnostic and must not open the Virtue window.
now = 52
add(14, "准备吸奶盾", 1306517, 3)
now = 56
absorbs.player = 10
Fuyutsui:ObserveAOEHealAbsorbs()
update(2, 1, "observed absorb without success remains reserved")
now = 58
update(2, 1, "observed absorb without success never opens execution")
absorbs.player = 0
Fuyutsui:ObserveAOEHealAbsorbs()
clear()

-- A post-impact positive absorb without UNIT_SPELLCAST_SUCCEEDED is not a
-- success signal; absorb values are diagnostic only.
now = 60
add(15, "准备吸奶盾", 1306517, 3)
Fuyutsui:ObserveAOEHealAbsorbs()
now = 64
absorbs.player = 10
Fuyutsui:ObserveAOEHealAbsorbs()
update(2, 1, "post-impact absorb inference starts the delay")
now = 66
update(2, 1, "post-impact absorb inference never opens execution")
absorbs.player = 0
Fuyutsui:ObserveAOEHealAbsorbs()
clear()

-- A late real cast must still bind during the grace period instead of being
-- rejected because the timeline impact already passed.
now = 55
add(13, "准备吸奶盾", 1306517, 3)
now = 59
start("nameplate7", "late-absorb", 1306517, 3)
now = 60
update(2, 1, "late absorb cast remains reserved until its success terminal")
clear()

-- DiGua replacement cleanup can report Canceled for the absorb row before
-- the verified impact. That cleanup must not revoke the reservation.
now = 35
add(12, "准备吸奶盾", 1306517, 3)
eventStates[12] = Enum.EncounterTimelineEventState.Canceled
Fuyutsui:ObserveAOETimelineState(12)
Fuyutsui:RemoveAOETimelineEvent(12)
now = 38
update(2, 1, "canceled absorb timeline remains in grace")
now = 53
update(0, 0, "canceled absorb timeline releases public stage after fallback grace")
now = 55
update(0, 0, "canceled absorb timeline remains released without success")
clear()

-- A visible cast terminal remains the strongest execution signal.
now = 40
add(3, "准备吸奶盾", 1306517, 3)
start("nameplate2", "absorb-a", 1306517, 2)
now = 41.5
update(2, 1, "absorb cast stays in reservation before success")
Fuyutsui:FinishAOEEnemyCast("nameplate2", "absorb-a", 1306517, "succeeded")
flush()
update(2, 1, "absorb success starts the two-second post-cast delay")
now = 42.5
update(2, 5, "absorb success reserves the final GCD before Virtue is ready")
now = 44
update(2, 3, "absorb success opens the Virtue execution window two seconds after cast end")
absorbs.player = 10
Fuyutsui:ObserveAOEHealAbsorbs()
update(2, 3, "observed absorb enters stage three")
eventStates[3] = Enum.EncounterTimelineEventState.Canceled
Fuyutsui:ObserveAOETimelineState(3)
Fuyutsui:RemoveAOETimelineEvent(3)
update(2, 3, "late cancel does not revoke absorb success")
absorbs.player = 0
Fuyutsui:ObserveAOEHealAbsorbs()
update(2, 3, "first zero absorb cycle remains active")
Fuyutsui:ObserveAOEHealAbsorbs()
update(2, 3, "second zero absorb cycle keeps the unconfirmed execution window")
Fuyutsui:ConfirmAOEVirtue(200025)
Fuyutsui:ObserveAOEHealAbsorbs()
Fuyutsui:ObserveAOEHealAbsorbs()
update(0, 0, "confirmed Virtue allows the absorb event to complete")
clear()

-- STOP alone is unknown, and a higher-priority interruption wins over success in
-- the same event loop. A late interruption cannot revoke committed success.
now = 50
add(4, "准备吸奶盾", 1306517, 3)
start("nameplate3", "absorb-stop", 1306517, 2)
Fuyutsui:FinishAOEEnemyCast("nameplate3", "absorb-stop", 1306517, "stopped")
local failedBeforeStop = Fuyutsui.state.aoeCastsFailed or 0
flush()
update(2, 1, "stop alone is not success")
assert((Fuyutsui.state.aoeCastsFailed or 0) == failedBeforeStop,
    "STOP without a terminal result must not be diagnosed as a failed cast")
start("nameplate3", "absorb-race", 1306517, 2)
Fuyutsui:FinishAOEEnemyCast("nameplate3", "absorb-race", 1306517, "succeeded")
	Fuyutsui:FinishAOEEnemyCast("nameplate3", "absorb-race", 1306517, "interrupted")
	flush()
	update(2, 1, "interruption wins same-loop race")
	now = 54
	update(2, 1, "explicit interruption blocks timeline fallback")
	start("nameplate3", "absorb-committed", 1306517, 2)
Fuyutsui:FinishAOEEnemyCast("nameplate3", "absorb-committed", 1306517, "succeeded")
flush()
Fuyutsui:FinishAOEEnemyCast("nameplate3", "absorb-committed", 1306517, "interrupted")
flush()
absorbs.player = 10
Fuyutsui:ObserveAOEHealAbsorbs()
now = 58
update(2, 3, "late interruption is ignored after the post-cast delay")
clear()

-- Death and cancellation only terminate the event they own.
now = 60
add(5, "准备AOE", 888, 3)
add(6, "准备AOE", 888, 8)
start("nameplate4", "parallel-a", 888, 3)
start("nameplate5", "parallel-b", 888, 8)
Fuyutsui:CancelAOEEventsForUnitGUID(UnitGUID("nameplate4"))
flush()
update(1, 1, "one caster death preserves the other event")
eventStates[6] = Enum.EncounterTimelineEventState.Canceled
Fuyutsui:ObserveAOETimelineState(6)
Fuyutsui:RemoveAOETimelineEvent(6)
update(0, 0, "explicit cancellation ends remaining event")
clear()

-- Unknown removal cancels, while a Finished timeline lets a matched cast finish.
now = 70
add(7, "准备AOE", 999, 3)
Fuyutsui:RemoveAOETimelineEvent(7)
update(0, 0, "unknown timeline removal cancels")
add(8, "准备AOE", 999, 3)
start("nameplate6", "finished-timeline", 999, 3)
eventStates[8] = Enum.EncounterTimelineEventState.Finished
Fuyutsui:ObserveAOETimelineState(8)
Fuyutsui:RemoveAOETimelineEvent(8)
now = 72.5
update(1, 2, "finished timeline keeps real cast")
clear()

-- Instanced combat can protect the cast Spell ID and numeric timing. A same-frame
-- nameplate cast is correlated only with the newly created DiGua warning, while
-- completion still comes from the unit cast terminal event.
now = 80
local protectedSpell = { secret = true }
local rawBefore = Fuyutsui.state.aoeRawCasts or 0
local protectedBefore = Fuyutsui.state.aoeProtectedSpells or 0
local matchedBefore = Fuyutsui.state.aoeProtectedMatches or 0
casts.nameplate7 = { startedAt = now, endsAt = now + 4 }
local bridgeReadyBeforeProtected = Fuyutsui.state.diGuaBridgeReady
Fuyutsui.state.diGuaBridgeReady = false
Fuyutsui:ObserveAOEEnemyCast("nameplate7", protectedSpell, protectedSpell, false)
add(9, "准备吸奶盾", 1306517, 4)
update(2, 1, "protected cast remains absorb reservation inside Lua")
assert(Fuyutsui.state.aoeProtectedCastActive == true, "protected cast must publish its active protocol flag")
assert(Fuyutsui.state.aoeRawCasts == (rawBefore + 1) % 256,
    "protected correlation publishes the raw cast diagnostic")
assert(Fuyutsui.state.aoeProtectedSpells == (protectedBefore + 1) % 256,
    "protected correlation publishes the secret Spell ID diagnostic")
assert(Fuyutsui.state.aoeProtectedMatches == (matchedBefore + 1) % 256,
    "protected correlation publishes the semantic match diagnostic")
Fuyutsui:FinishAOEEnemyCast("nameplate7", protectedSpell, protectedSpell, "succeeded")
flush()
now = 86
update(2, 3, "protected cast success opens absorb execution")
Fuyutsui.state.diGuaBridgeReady = bridgeReadyBeforeProtected
clear()

-- Fuyutsui can receive the readable UNIT_SPELLCAST_START before DiGua creates
-- its semantic timeline event. The same-frame candidate must bind when ADDED arrives.
now = 90
casts.nameplate8 = { startedAt = now, endsAt = now + 4 }
Fuyutsui:ObserveAOEEnemyCast("nameplate8", "readable-before-warning", 777, false)
add(10, "准备AOE", 777, 4)
now = 93.2
update(1, 2, "readable cast before warning still reaches Virtue window")
Fuyutsui:FinishAOEEnemyCast("nameplate8", "readable-before-warning", 777, "succeeded")
flush()
update(1, 4, "readable cast before warning completes normally")

-- DiGua's UNIT_SPELLCAST_START path remains authoritative when no timeline row
-- is available yet. A mapped cast creates its own event from UnitCastingInfo;
-- the terminal event still decides whether it can open Virtue.
clear()
now = 100
casts.nameplate9 = { startedAt = now, endsAt = now + 3 }
Fuyutsui:ObserveAOEEnemyCast("nameplate9", "direct-absorb", 1306517, false)
update(2, 1, "direct cast monitoring reserves absorb")
Fuyutsui:FinishAOEEnemyCast("nameplate9", "direct-absorb", 1306517, "succeeded")
flush()
now = 105
update(2, 3, "direct cast success opens absorb execution after delay")

-- The same direct path works when DiGua protects the enemy Spell ID/GUID: its
-- nameplate/context filters identify the Ritual Lord, while the terminal event
-- still owns success versus interruption.
clear()
now = 110
casts.nameplate10 = { startedAt = now, endsAt = now + 3 }
Fuyutsui:ObserveAOEEnemyCast("nameplate10", protectedSpell, protectedSpell, false)
update(2, 1, "protected direct cast reserves absorb")
Fuyutsui:FinishAOEEnemyCast("nameplate10", protectedSpell, protectedSpell, "succeeded")
flush()
now = 115
update(2, 3, "protected direct success opens absorb execution")

-- DiGua's live NamePlateEnterCombat countdown is also authoritative when the
-- client does not expose a matching UNIT_SPELLCAST event to Shigure.
clear()
now = 120
Fuyutsui:ObserveAOEDiGuaBar(132334, 3, "准备吸奶盾", "nameplate11")
update(2, 1, "DiGua bar reserves absorb")
now = 123
update(2, 1, "DiGua bar waits its post-cast delay")
now = 125
update(2, 3, "DiGua bar opens absorb execution")
clear()

print("AOE warning production Lua replay passed")
