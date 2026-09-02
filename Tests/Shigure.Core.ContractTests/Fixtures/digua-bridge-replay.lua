local sourcePath = assert(arg[1], "diguabridge.lua path is required")
local eventHandler
local addHook
local cancelHook
local nextEventID = 1000
local remainingByID = {}
local addedEvents = {}
local canceledEvents = {}

function issecretvalue() return false end
function wipe(value) for key in pairs(value) do value[key] = nil end end
function CreateFrame()
    return {
        RegisterEvent = function() end,
        SetScript = function(_, _, handler) eventHandler = handler end,
    }
end
function hooksecurefunc(_, methodName, handler)
    if methodName == "AddScriptEvent" then
        addHook = handler
    elseif methodName == "CancelScriptEvent" then
        cancelHook = handler
    else
        error("unexpected secure hook: " .. tostring(methodName))
    end
end

C_AddOns = {
    IsAddOnLoaded = function(addonName)
        assert(addonName == "DiGuaTimelineAudioHelper")
        return true
    end,
    GetAddOnMetadata = function(addonName, field)
        assert(addonName == "DiGuaTimelineAudioHelper" and field == "Version")
        return "1.8.4"
    end,
}
C_EncounterTimeline = {
    GetEventTimeRemaining = function(eventID) return remainingByID[eventID] end,
    AddScriptEvent = function(eventInfo)
        nextEventID = nextEventID + 1
        local eventID = nextEventID
        addedEvents[eventID] = eventInfo
        if addHook then addHook(eventInfo) end
        return eventID
    end,
    CancelScriptEvent = function(eventID)
        canceledEvents[eventID] = true
        if cancelHook then cancelHook(eventID) end
    end,
}

Fuyutsui = {
    state = {},
    UpdateStateBlock = function() end,
}
assert(loadfile(sourcePath))("Fuyutsui", {})
assert(Fuyutsui.state.diGuaBridgeReady == true, "supported DiGua version must publish bridge readiness")
assert(type(eventHandler) == "function", "supported DiGua version must register timeline events")
assert(type(addHook) == "function", "supported DiGua version must observe raw AddScriptEvent input")
assert(type(cancelHook) == "function", "supported DiGua version must propagate cancellation")

local mappingCount = 0
for iconFileID, castSpellID in pairs(Fuyutsui.DiGuaBridge.castSpellByIcon) do
    mappingCount = mappingCount + 1
    assert(type(iconFileID) == "number" and iconFileID > 0, "icon registry key must be positive")
    assert(type(castSpellID) == "number" and castSpellID > 0, "cast Spell ID must be positive")

    local beforeID = nextEventID
    local duration = 10 + mappingCount
    local eventInfo = {
        overrideName = mappingCount % 2 == 0 and "准备AOE" or "准备吸奶盾",
        spellID = 0,
        iconFileID = iconFileID,
        duration = duration,
    }
    local originalID = C_EncounterTimeline.AddScriptEvent(eventInfo)
    local replacementID = originalID + 1
    assert(nextEventID == beforeID + 2, "each raw DiGua warning must be re-emitted once")
    local replacement = addedEvents[replacementID]
    assert(replacement.spellID == castSpellID, "bridge must use its canonical cast Spell ID")
    assert(replacement.iconFileID == iconFileID, "bridge must preserve the icon")
    assert(replacement.duration == duration, "bridge must preserve raw event duration")

    eventInfo.id = originalID
    remainingByID[originalID] = duration
    eventHandler(nil, "ENCOUNTER_TIMELINE_EVENT_ADDED", eventInfo)
    C_EncounterTimeline.CancelScriptEvent(originalID)
    assert(canceledEvents[replacementID] == true, "DiGua cancellation must propagate to the replacement")
end
assert(mappingCount == 12, "bridge registry must cover the 12 current Shigure warning icons")

local beforeIgnored = nextEventID
eventHandler(nil, "ENCOUNTER_TIMELINE_EVENT_ADDED", {
    id = 900,
    overrideName = "坦克尖刺",
    spellID = 0,
    iconFileID = 132334,
    duration = 10,
})
eventHandler(nil, "ENCOUNTER_TIMELINE_EVENT_ADDED", {
    id = 901,
    overrideName = "准备AOE",
    spellID = 777,
    iconFileID = 132334,
    duration = 10,
})
assert(nextEventID == beforeIgnored, "non-Shigure and already-identified events must be ignored")

print("DiGua bridge production Lua replay passed")
