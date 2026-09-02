local addon = ...

local SUPPORTED_DIGUA_VERSION = "1.8.4"
local STANDARD_EVENT_NAMES = {
    ["准备AOE"] = true,
    ["准备吸奶盾"] = true,
}

-- This is the only machine-readable registry for the DiGua-to-Shigure mapping.
-- Keys are DiGua icon file IDs; values are the corresponding enemy cast Spell IDs.
local CAST_SPELL_BY_ICON = {
    [132334] = 1306517, -- 鲜血献祭 (heal-absorb aura is 1306550)
    [6238561] = 1294849, -- 振响
    [3154546] = 1238687, -- 苦难盛宴
    [463283] = 1246957, -- 原始回响
    [460698] = 373692, -- 地狱烈火
    [5764923] = 1303486, -- 蚀骨践踏
    [136016] = 1250937, -- 喷毒
    [451169] = 270293, -- 净化打击
    [136025] = 1305201, -- 采掘冲击
    [2576091] = 1305945, -- 暗影旋风斩
    [132862] = 1263636, -- 喷射孢子
    [840194] = 1234855, -- 险恶光环 (channel remains disabled in Fuyutsui pending live validation)
}

-- Derive the cast-side event type from the same registry used when replacing
-- DiGua timeline rows, so direct read-bar monitoring cannot drift from it.
local CAST_EVENT_TYPE_BY_SPELL = {}
for iconFileID, spellID in pairs(CAST_SPELL_BY_ICON) do
    CAST_EVENT_TYPE_BY_SPELL[spellID] = iconFileID == 132334 and 2 or 1
end

Fuyutsui.DiGuaBridge = {
    supportedVersion = SUPPORTED_DIGUA_VERSION,
    castSpellByIcon = CAST_SPELL_BY_ICON,
    castEventTypeBySpell = CAST_EVENT_TYPE_BY_SPELL,
}

local function IsSecret(value)
    return issecretvalue and issecretvalue(value)
end

local function SafeNumber(value)
    return not IsSecret(value) and type(value) == "number" and value or nil
end

local function SafeString(value)
    return not IsSecret(value) and type(value) == "string" and value or nil
end

local function SafeCall(fn, ...)
    if type(fn) ~= "function" then return nil end
    local ok, result = pcall(fn, ...)
    return ok and result or nil
end

local function DiGuaVersion()
    if C_AddOns and C_AddOns.GetAddOnMetadata then
        return SafeString(C_AddOns.GetAddOnMetadata("DiGuaTimelineAudioHelper", "Version"))
    end
    return SafeString(GetAddOnMetadata and GetAddOnMetadata("DiGuaTimelineAudioHelper", "Version"))
end

local function IsDiGuaLoaded()
    if C_AddOns and C_AddOns.IsAddOnLoaded then
        return C_AddOns.IsAddOnLoaded("DiGuaTimelineAudioHelper")
    end
    return IsAddOnLoaded and IsAddOnLoaded("DiGuaTimelineAudioHelper")
end

function Fuyutsui:InitializeDiGuaBridge()
    if self.diGuaBridgeInitialized then return true end
    if not IsDiGuaLoaded() or DiGuaVersion() ~= SUPPORTED_DIGUA_VERSION then return false end

    self.diGuaBridgeInitialized = true
    self.state.diGuaBridgeReady = true
    if self.UpdateStateBlock then
        self:UpdateStateBlock("状态", "DiGua桥接就绪")
    end

    local originalToReplacement = {}
    local replacementToOriginal = {}
    local pendingReplacements = {}
    local synchronousReemits = {}

    local function ForgetPair(originalID, replacementID)
        if originalID then originalToReplacement[originalID] = nil end
        if replacementID then replacementToOriginal[replacementID] = nil end
    end

    local function EventSignature(eventInfo)
        if type(eventInfo) ~= "table" then return nil end
        local eventName = SafeString(eventInfo.overrideName)
            or SafeString(eventInfo.name)
            or SafeString(eventInfo.spellName)
        local iconFileID = SafeNumber(eventInfo.iconFileID) or SafeNumber(eventInfo.icon)
        if not eventName or not iconFileID then return nil end
        return eventName .. ":" .. tostring(iconFileID)
    end

    local function ReemitWithCastSpell(eventInfo, originalID)
        if type(eventInfo) ~= "table" then return end
        originalID = SafeNumber(originalID) or SafeNumber(eventInfo.id)
        local eventName = SafeString(eventInfo.overrideName)
            or SafeString(eventInfo.name)
            or SafeString(eventInfo.spellName)
        local originalSpellID = SafeNumber(eventInfo.spellID)
        local iconFileID = SafeNumber(eventInfo.iconFileID) or SafeNumber(eventInfo.icon)
        if not STANDARD_EVENT_NAMES[eventName]
            or (originalSpellID and originalSpellID > 0)
            or (originalID and originalToReplacement[originalID])
            or not iconFileID then
            return
        end

        local castSpellID = CAST_SPELL_BY_ICON[iconFileID]
        if not castSpellID then return end
        if Fuyutsui.PublishAOEDiagnostic then
            Fuyutsui:PublishAOEDiagnostic("bridgeRequest", castSpellID)
        end
        local remaining = originalID
            and SafeCall(C_EncounterTimeline.GetEventTimeRemaining, originalID)
            or SafeNumber(eventInfo.duration)
        if not remaining or remaining <= 0 then return end

        local replacementID = SafeNumber(SafeCall(C_EncounterTimeline.AddScriptEvent, {
            spellID = castSpellID,
            iconFileID = iconFileID,
            duration = remaining,
            overrideName = eventName,
            icons = 0x1,
            severity = 2,
            maxQueueDuration = 0,
            paused = false,
        }))
        if replacementID and Fuyutsui.PublishAOEDiagnostic then
            Fuyutsui:PublishAOEDiagnostic("bridgeSuccess", castSpellID)
        end
        return replacementID
    end

    local function RememberPair(originalID, replacementID)
        if not originalID or not replacementID then return end
        originalToReplacement[originalID] = replacementID
        replacementToOriginal[replacementID] = originalID
    end

    local function QueuePendingReplacement(eventInfo, replacementID)
        local signature = EventSignature(eventInfo)
        if not signature or not replacementID then return end
        pendingReplacements[signature] = pendingReplacements[signature] or {}
        table.insert(pendingReplacements[signature], replacementID)
    end

    local function ClaimPendingReplacement(eventInfo)
        local signature = EventSignature(eventInfo)
        local queue = signature and pendingReplacements[signature] or nil
        if not queue or #queue == 0 then return nil end
        local replacementID = table.remove(queue, 1)
        if #queue == 0 then pendingReplacements[signature] = nil end
        return replacementID
    end

    local frame = CreateFrame("Frame")
    frame:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_ADDED")
    frame:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_REMOVED")
    frame:RegisterEvent("PLAYER_LEAVING_WORLD")
    frame:SetScript("OnEvent", function(_, event, arg)
        if event == "ENCOUNTER_TIMELINE_EVENT_ADDED" then
            if type(arg) ~= "table" then return end
            local originalID = SafeNumber(arg.id)
            local originalSpellID = SafeNumber(arg.spellID)
            if not originalID or (originalSpellID and originalSpellID > 0) then return end

            local replacementID = ClaimPendingReplacement(arg)
            if not replacementID then
                replacementID = ReemitWithCastSpell(arg, originalID)
                local signature = EventSignature(arg)
                if replacementID and signature then
                    synchronousReemits[signature] = (synchronousReemits[signature] or 0) + 1
                end
            end
            RememberPair(originalID, replacementID)
        elseif event == "ENCOUNTER_TIMELINE_EVENT_REMOVED" then
            local eventID = SafeNumber(arg)
            if eventID then
                local replacementID = originalToReplacement[eventID]
                local originalID = replacementToOriginal[eventID]
                ForgetPair(originalID or eventID, replacementID or eventID)
            end
        elseif event == "PLAYER_LEAVING_WORLD" then
            wipe(originalToReplacement)
            wipe(replacementToOriginal)
            wipe(pendingReplacements)
            wipe(synchronousReemits)
        end
    end)

    if hooksecurefunc then
        hooksecurefunc(C_EncounterTimeline, "AddScriptEvent", function(eventInfo)
            local signature = EventSignature(eventInfo)
            if not signature then return end
            local synchronousCount = synchronousReemits[signature] or 0
            if synchronousCount > 0 then
                synchronousReemits[signature] = synchronousCount == 1 and nil or synchronousCount - 1
                return
            end

            -- Event payload fields can become protected by the time ADDED is dispatched.
            -- Re-emit from DiGua's original API input, then pair IDs when ADDED arrives.
            QueuePendingReplacement(eventInfo, ReemitWithCastSpell(eventInfo))
        end)

        hooksecurefunc(C_EncounterTimeline, "CancelScriptEvent", function(eventID)
            eventID = SafeNumber(eventID)
            local replacementID = eventID and originalToReplacement[eventID] or nil
            if not replacementID then return end
            ForgetPair(eventID, replacementID)
            SafeCall(C_EncounterTimeline.CancelScriptEvent, replacementID)
        end)
    end

    return true
end

if not Fuyutsui:InitializeDiGuaBridge() then
    local loadFrame = CreateFrame("Frame")
    loadFrame:RegisterEvent("ADDON_LOADED")
    loadFrame:SetScript("OnEvent", function(self, _, loadedAddon)
        if loadedAddon == "DiGuaTimelineAudioHelper" and Fuyutsui:InitializeDiGuaBridge() then
            self:UnregisterEvent("ADDON_LOADED")
        end
    end)
end
